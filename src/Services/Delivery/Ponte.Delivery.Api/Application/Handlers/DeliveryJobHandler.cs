using Ponte.Delivery.Api.Application.Resilience;
using Ponte.Delivery.Api.Domain;
using Ponte.Delivery.Api.Infrastructure;
using Ponte.Delivery.Api.Infrastructure.Http;

namespace Ponte.Delivery.Api.Application.Handlers;

/// <summary>
/// Executa uma tentativa de entrega. O fluxo e:
///   1. reivindica a entrega (lease + concorrencia otimista) para nao duplicar POST;
///   2. consulta circuit breaker e bulkhead do endpoint (se negar, adia sem gastar tentativa);
///   3. envia o POST assinado;
///   4. grava a tentativa, agenda o retry na fila de espera certa e publica DeliveryAttempted,
///      tudo no mesmo commit (Outbox).
/// </summary>
public sealed class DeliveryJobHandler(
    DeliveryDbContext dbContext,
    IOutbox outbox,
    IWebhookSender sender,
    IRetryPolicy retryPolicy,
    ICircuitBreakerRegistry circuitBreakers,
    EndpointBulkhead bulkhead,
    DeliveryMetrics metrics,
    TimeProvider clock,
    ILogger<DeliveryJobHandler> logger) : IMessageHandler<DeliveryJob>
{
    /// <summary>Maior que o timeout HTTP: se a instancia morrer, outra assume depois disso.</summary>
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan ShortestTier = Topology.RetryTiers[0];

    public async Task HandleAsync(DeliveryJob job, MessageContext context, CancellationToken cancellationToken)
    {
        var delivery = await dbContext.Deliveries.FirstOrDefaultAsync(d => d.Id == job.DeliveryId, cancellationToken);
        if (delivery is null || delivery.IsTerminal)
        {
            return; // job duplicado ou atrasado: nada a fazer
        }

        var now = clock.GetUtcNow();
        switch (delivery.TryClaim(now, LeaseDuration))
        {
            case ClaimResult.AlreadyCompleted:
            case ClaimResult.NotDueYet:
                return;

            case ClaimResult.InFlightElsewhere:
                // Outra instancia esta no meio desta entrega. Volta daqui a pouco: se ela
                // concluir, o proximo job vira no-op; se ela morreu, o lease expira.
                ScheduleJob(delivery.Id, ShortestTier);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogDebug("Entrega {DeliveryId} reivindicada por outra instancia", delivery.Id);
            return;
        }

        var endpoint = await dbContext.Endpoints.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == delivery.EndpointId, cancellationToken);

        if (endpoint is null || endpoint.Deleted || !endpoint.Active)
        {
            delivery.Cancel(clock.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        // Bulkhead antes do breaker: assim nao "gastamos" a requisicao de teste do half-open.
        if (!bulkhead.TryEnter(endpoint.Id, endpoint.MaxConcurrency, out var lease))
        {
            await DeferAsync(delivery, ShortestTier, "bulkhead_full", cancellationToken);
            return;
        }

        WebhookResult result;
        using (lease)
        {
            var permit = circuitBreakers.TryAcquire(endpoint.Id);
            if (!permit.Allowed)
            {
                await DeferAsync(delivery, permit.RetryAfter, "circuit_open", cancellationToken);
                return;
            }

            var request = new WebhookRequest(
                new Uri(endpoint.Url), endpoint.SigningSecrets(clock.GetUtcNow()), delivery.EventId, delivery.EventType, delivery.Body, delivery.AttemptCount + 1);

            try
            {
                result = await sender.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = WebhookResult.Failure($"Erro inesperado: {ex.Message}", TimeSpan.Zero);
            }

            circuitBreakers.Record(endpoint.Id, healthy: !result.IndicatesUnhealthyEndpoint);
        }

        now = clock.GetUtcNow();
        var attempt = delivery.RecordAttempt(result, retryPolicy, now);
        dbContext.Attempts.Add(attempt);

        if (delivery.Status == DeliveryStatus.Scheduled && delivery.NextAttemptAt is { } nextAttempt)
        {
            ScheduleJob(delivery.Id, nextAttempt - now);
        }

        outbox.Enqueue(new DeliveryAttempted(
            delivery.Id,
            delivery.EventId,
            delivery.EndpointId,
            delivery.TenantId,
            delivery.EventType,
            attempt.AttemptNumber,
            delivery.OutcomeName,
            result.StatusCode,
            attempt.DurationMs,
            attempt.AttemptedAt,
            delivery.NextAttemptAt));

        // O POST ja aconteceu: gravamos o resultado mesmo se o host estiver desligando.
        await dbContext.SaveChangesAsync(CancellationToken.None);

        if (delivery.Status == DeliveryStatus.Dead)
        {
            logger.LogWarning("Entrega {DeliveryId} para {EndpointId} desistiu apos {Attempts} tentativas",
                delivery.Id, delivery.EndpointId, delivery.AttemptCount);
        }
    }

    private async Task DeferAsync(WebhookDelivery delivery, TimeSpan minimumDelay, string reason, CancellationToken cancellationToken)
    {
        var tier = RetryTiers.Ceiling(minimumDelay);
        var now = clock.GetUtcNow();
        delivery.Defer(now + tier, now);
        ScheduleJob(delivery.Id, tier);
        await dbContext.SaveChangesAsync(cancellationToken);
        metrics.Deferred(reason);
    }

    private void ScheduleJob(Guid deliveryId, TimeSpan minimumDelay) =>
        outbox.Enqueue(
            new DeliveryJob(deliveryId),
            Topology.RetryExchange,
            Topology.RetryRoutingKey(RetryTiers.Ceiling(minimumDelay)));
}

internal static class RetryTiers
{
    /// <summary>Menor degrau de espera que cobre o atraso pedido (as filas de TTL sao fixas).</summary>
    public static TimeSpan Ceiling(TimeSpan minimum) =>
        Topology.RetryTiers.FirstOrDefault(t => t >= minimum, Topology.RetryTiers[^1]);
}
