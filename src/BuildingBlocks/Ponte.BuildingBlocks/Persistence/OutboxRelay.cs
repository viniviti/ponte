using Microsoft.EntityFrameworkCore;
using Ponte.BuildingBlocks.Messaging;

namespace Ponte.BuildingBlocks.Persistence;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public bool RelayEnabled { get; set; } = true;

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public int BatchSize { get; set; } = 200;

    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(3);
}

/// <summary>
/// Le o outbox em lotes e publica no RabbitMQ com publisher confirms.
/// Garantia: at-least-once. Se publicar e cair antes do commit, a mensagem sai de novo,
/// e por isso todo consumidor e idempotente (Inbox).
/// </summary>
public sealed class OutboxRelay<TContext>(
    IServiceScopeFactory scopeFactory,
    IMessagePublisher publisher,
    IOutboxSqlDialect dialect,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<OutboxRelay<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private readonly OutboxOptions _options = options.Value;
    private DateTimeOffset _lastCleanup = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PollingInterval);
        try
        {
            do
            {
                await DrainAsync(stoppingToken);
                await CleanupIfDueAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // desligamento normal
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Enquanto vier lote cheio, continua sem esperar o proximo tick (rajadas).
            while (await ProcessBatchAsync(cancellationToken) == _options.BatchSize)
            {
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha no relay do outbox de {Context}", typeof(TContext).Name);
        }
    }

    /// <summary>Processa um lote. Retorna quantas mensagens foram publicadas.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TContext>();

        // Com EnableRetryOnFailure, transacoes explicitas precisam rodar dentro da strategy.
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            var batch = await dbContext.Set<OutboxMessage>()
                .FromSqlRaw(dialect.LockPendingBatchSql, _options.BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return 0;
            }

            var published = 0;
            try
            {
                var messages = batch
                    .Select(m => new OutgoingMessage(m.Id, m.Exchange, m.RoutingKey, m.Type, Encoding.UTF8.GetBytes(m.Payload), m.TraceParent))
                    .ToList();

                await publisher.PublishBatchAsync(messages, cancellationToken);

                var now = clock.GetUtcNow();
                foreach (var message in batch)
                {
                    message.ProcessedAt = now;
                }

                published = batch.Count;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Broker recusou lote de {Count} mensagens do outbox", batch.Count);
                foreach (var message in batch)
                {
                    message.Attempts++;
                    message.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return published;
        });
    }

    private async Task CleanupIfDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (now - _lastCleanup < TimeSpan.FromMinutes(10))
        {
            return;
        }

        _lastCleanup = now;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TContext>();
            var threshold = now - _options.Retention;

            var outbox = await dbContext.Set<OutboxMessage>()
                .Where(x => x.ProcessedAt != null && x.ProcessedAt < threshold)
                .ExecuteDeleteAsync(cancellationToken);

            var inbox = await dbContext.Set<ProcessedMessage>()
                .Where(x => x.ProcessedAt < threshold)
                .ExecuteDeleteAsync(cancellationToken);

            if (outbox + inbox > 0)
            {
                logger.LogInformation("Limpeza: {Outbox} mensagens do outbox e {Inbox} do inbox removidas", outbox, inbox);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Falha na limpeza do outbox");
        }
    }
}

public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddOutbox<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        IOutboxSqlDialect dialect)
        where TContext : DbContext
    {
        var section = configuration.GetSection(OutboxOptions.SectionName);
        services.Configure<OutboxOptions>(section);
        services.AddSingleton(dialect);
        services.AddScoped<IOutbox, Outbox<TContext>>();
        services.AddScoped<IInbox, Inbox<TContext>>();
        services.AddSingleton<OutboxRelay<TContext>>();

        if (section.GetValue(nameof(OutboxOptions.RelayEnabled), defaultValue: true))
        {
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<OutboxRelay<TContext>>());
        }

        return services;
    }
}
