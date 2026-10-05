using Microsoft.AspNetCore.Http.HttpResults;
using Ponte.Delivery.Api.Application;
using Ponte.Delivery.Api.Domain;
using Ponte.Delivery.Api.Infrastructure;

namespace Ponte.Delivery.Api.Api;

public sealed record DeliverySummary(
    Guid Id,
    long Sequence,
    Guid EventId,
    Guid EndpointId,
    string EventType,
    DeliveryStatus Status,
    int AttemptCount,
    int? LastStatusCode,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record AttemptView(
    int AttemptNumber,
    int? StatusCode,
    bool Succeeded,
    long DurationMs,
    string? Error,
    string? ResponseSnippet,
    DateTimeOffset AttemptedAt);

public sealed record DeliveryDetails(DeliverySummary Delivery, string Body, IReadOnlyList<AttemptView> Attempts);

public sealed record BulkReplayResponse(int Replayed);

public static class DeliveriesEndpoints
{
    private const int MaxBulkReplay = 1000;

    public static IEndpointRouteBuilder MapDeliveriesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/deliveries").WithTags("Deliveries").RequireTenant();

        group.MapGet("/", ListAsync).WithName("ListDeliveries");
        group.MapGet("/{id:guid}", GetAsync).WithName("GetDelivery");
        group.MapPost("/{id:guid}/replay", ReplayAsync).WithName("ReplayDelivery");
        group.MapPost("/replay-dead", ReplayDeadAsync)
            .WithName("ReplayDeadDeliveries")
            .WithSummary("Reenfileira todas as entregas mortas (opcionalmente de um endpoint), apos o cliente corrigir o servidor.");

        return app;
    }

    private static async Task<Ok<Page<DeliverySummary>>> ListAsync(
        ITenantContext tenant,
        DeliveryDbContext dbContext,
        string? status,
        Guid? endpointId,
        Guid? eventId,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        var take = Pagination.ClampLimit(limit);
        var after = Pagination.ParseCursor(cursor);
        var tenantId = tenant.TenantId;

        var query = dbContext.Deliveries.AsNoTracking().Where(d => d.TenantId == tenantId);

        // Aceita "dead", "Dead" etc. (o JSON de saida usa camelCase).
        if (Enum.TryParse<DeliveryStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(d => d.Status == parsedStatus);
        }

        if (endpointId is { } endpoint)
        {
            query = query.Where(d => d.EndpointId == endpoint);
        }

        if (eventId is { } evt)
        {
            query = query.Where(d => d.EventId == evt);
        }

        if (after is { } sequence)
        {
            query = query.Where(d => d.Sequence < sequence);
        }

        var items = await query
            .OrderByDescending(d => d.Sequence)
            .Take(take + 1)
            .Select(d => new DeliverySummary(
                d.Id, d.Sequence, d.EventId, d.EndpointId, d.EventType, d.Status, d.AttemptCount,
                d.LastStatusCode, d.NextAttemptAt, d.CreatedAt, d.CompletedAt))
            .ToListAsync(cancellationToken);

        var nextCursor = items.Count > take ? items[take - 1].Sequence.ToString() : null;
        return TypedResults.Ok(new Page<DeliverySummary>(items.Take(take).ToList(), nextCursor));
    }

    private static async Task<Results<Ok<DeliveryDetails>, NotFound>> GetAsync(
        Guid id,
        ITenantContext tenant,
        DeliveryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        var delivery = await dbContext.Deliveries
            .AsNoTracking()
            .Include(d => d.Attempts)
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId, cancellationToken);

        if (delivery is null)
        {
            return TypedResults.NotFound();
        }

        var summary = new DeliverySummary(
            delivery.Id, delivery.Sequence, delivery.EventId, delivery.EndpointId, delivery.EventType, delivery.Status,
            delivery.AttemptCount, delivery.LastStatusCode, delivery.NextAttemptAt, delivery.CreatedAt, delivery.CompletedAt);

        var attempts = delivery.Attempts
            .OrderBy(a => a.AttemptNumber)
            .Select(a => new AttemptView(a.AttemptNumber, a.StatusCode, a.Succeeded, a.DurationMs, a.Error, a.ResponseSnippet, a.AttemptedAt))
            .ToList();

        return TypedResults.Ok(new DeliveryDetails(summary, delivery.Body, attempts));
    }

    private static async Task<IResult> ReplayAsync(
        Guid id,
        ITenantContext tenant,
        DeliveryDbContext dbContext,
        IOutbox outbox,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        var delivery = await dbContext.Deliveries.FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId, cancellationToken);
        if (delivery is null)
        {
            return TypedResults.NotFound();
        }

        var error = delivery.Replay(clock.GetUtcNow());
        if (error is not null)
        {
            return error.ToProblem();
        }

        outbox.Enqueue(new DeliveryJob(delivery.Id), Topology.DeliveryExchange, Topology.DeliveryJobRoutingKey);
        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.Accepted($"/v1/deliveries/{delivery.Id}");
    }

    private static async Task<Ok<BulkReplayResponse>> ReplayDeadAsync(
        ITenantContext tenant,
        DeliveryDbContext dbContext,
        IOutbox outbox,
        TimeProvider clock,
        Guid? endpointId,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        var query = dbContext.Deliveries.Where(d => d.TenantId == tenantId && d.Status == DeliveryStatus.Dead);
        if (endpointId is { } endpoint)
        {
            query = query.Where(d => d.EndpointId == endpoint);
        }

        var dead = await query.OrderBy(d => d.Sequence).Take(MaxBulkReplay).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();

        foreach (var delivery in dead)
        {
            if (delivery.Replay(now) is null)
            {
                outbox.Enqueue(new DeliveryJob(delivery.Id), Topology.DeliveryExchange, Topology.DeliveryJobRoutingKey);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new BulkReplayResponse(dead.Count));
    }
}
