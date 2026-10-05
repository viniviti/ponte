using Ponte.Management.Api.Infrastructure;

namespace Ponte.Management.Api.Application;

/// <summary>
/// Competing consumer da fila management.metering: cada tentativa de entrega e contada
/// exatamente uma vez no agregado horario (Inbox + MERGE na mesma transacao).
/// </summary>
public sealed class MeteringHandler(ManagementDbContext dbContext, IInbox inbox) : IMessageHandler<DeliveryAttempted>
{
    private const string ConsumerName = "management.metering";

    public async Task HandleAsync(DeliveryAttempted message, MessageContext context, CancellationToken cancellationToken)
    {
        var hour = new DateTime(
            message.AttemptedAt.UtcDateTime.Year,
            message.AttemptedAt.UtcDateTime.Month,
            message.AttemptedAt.UtcDateTime.Day,
            message.AttemptedAt.UtcDateTime.Hour,
            0, 0, DateTimeKind.Utc);

        var succeeded = message.Outcome == DeliveryOutcomes.Succeeded ? 1 : 0;
        var failed = 1 - succeeded;
        var dead = message.Outcome == DeliveryOutcomes.Dead ? 1 : 0;

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            if (!await inbox.TryBeginAsync(message.Id, ConsumerName, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            // Upsert atomico no SQL Server. HOLDLOCK evita a corrida classica do MERGE
            // (dois consumidores inserindo a mesma chave ao mesmo tempo).
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                MERGE usage_hourly WITH (HOLDLOCK) AS target
                USING (SELECT {message.TenantId} AS tenant_id, {message.EndpointId} AS endpoint_id, {hour} AS hour_bucket) AS source
                   ON target.tenant_id = source.tenant_id
                  AND target.endpoint_id = source.endpoint_id
                  AND target.hour_bucket = source.hour_bucket
                WHEN MATCHED THEN UPDATE SET
                    succeeded = target.succeeded + {succeeded},
                    failed = target.failed + {failed},
                    dead = target.dead + {dead},
                    total_duration_ms = target.total_duration_ms + {message.DurationMs}
                WHEN NOT MATCHED THEN
                    INSERT (tenant_id, endpoint_id, hour_bucket, succeeded, failed, dead, total_duration_ms)
                    VALUES (source.tenant_id, source.endpoint_id, source.hour_bucket, {succeeded}, {failed}, {dead}, {message.DurationMs});
                """, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }
}
