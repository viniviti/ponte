using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Ponte.Contracts;
using Ponte.Ingestion.Api.Domain;
using Ponte.Ingestion.Api.Infrastructure;

namespace Ponte.Ingestion.Api.Application;

public sealed record IngestEventCommand(Guid TenantId, string? EventType, JsonElement Payload, string? IdempotencyKey);

public sealed record IngestEventResult(Guid EventId, DateTimeOffset ReceivedAt, bool Replayed);

/// <summary>
/// Caso de uso principal da ingestao. O caminho quente faz UMA ida ao banco:
/// INSERT do evento + INSERT no outbox na mesma transacao, e responde 202.
/// A publicacao no RabbitMQ acontece depois, fora da requisicao.
/// </summary>
public sealed class IngestEventHandler(
    IngestionDbContext dbContext,
    IOutbox outbox,
    TimeProvider clock,
    IngestionMetrics metrics)
{
    public async Task<Result<IngestEventResult>> HandleAsync(IngestEventCommand command, CancellationToken cancellationToken)
    {
        var error = EventValidator.Validate(command.EventType, command.Payload, command.IdempotencyKey);
        if (error is not null)
        {
            return error;
        }

        var payload = command.Payload.GetRawText();
        if (Encoding.UTF8.GetByteCount(payload) > EventValidator.MaxPayloadBytes)
        {
            return new Error("payload_too_large", $"payload excede {EventValidator.MaxPayloadBytes / 1024} KB.");
        }

        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

        if (command.IdempotencyKey is not null)
        {
            var existing = await FindByIdempotencyKeyAsync(command.TenantId, command.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                return Replay(existing, command.EventType!, payloadHash);
            }
        }

        var ingested = IngestedEvent.Create(
            command.TenantId, command.EventType!, payload, payloadHash, command.IdempotencyKey, clock.GetUtcNow());

        dbContext.Events.Add(ingested);
        outbox.Enqueue(new EventAccepted(ingested.Id, ingested.TenantId, ingested.EventType, payload, ingested.ReceivedAt));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (command.IdempotencyKey is not null && IsUniqueViolation(ex))
        {
            // Corrida: outra requisicao com a mesma chave gravou primeiro. O indice unico
            // decidiu o vencedor; devolvemos o evento dele.
            dbContext.ChangeTracker.Clear();
            var winner = await FindByIdempotencyKeyAsync(command.TenantId, command.IdempotencyKey, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return Replay(winner, command.EventType!, payloadHash);
        }

        metrics.Accepted();
        return new IngestEventResult(ingested.Id, ingested.ReceivedAt, Replayed: false);
    }

    private Result<IngestEventResult> Replay(StoredKey existing, string eventType, string payloadHash)
    {
        if (existing.EventType != eventType || existing.PayloadHash != payloadHash)
        {
            return Error.Conflict(
                "idempotency_key_reused",
                "Esta Idempotency-Key ja foi usada com um corpo diferente.");
        }

        metrics.Replayed();
        return new IngestEventResult(existing.Id, existing.ReceivedAt, Replayed: true);
    }

    private Task<StoredKey?> FindByIdempotencyKeyAsync(Guid tenantId, string key, CancellationToken cancellationToken) =>
        dbContext.Events
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.IdempotencyKey == key)
            .Select(e => new StoredKey(e.Id, e.EventType, e.PayloadHash, e.ReceivedAt))
            .FirstOrDefaultAsync(cancellationToken);

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private sealed record StoredKey(Guid Id, string EventType, string PayloadHash, DateTimeOffset ReceivedAt);
}
