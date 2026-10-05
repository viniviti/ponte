namespace Ponte.Ingestion.Api.Domain;

public sealed class IngestedEvent
{
    private IngestedEvent()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Sequencia monotonica usada na paginacao por keyset.</summary>
    public long Sequence { get; private set; }

    public Guid TenantId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    /// <summary>SHA-256 do payload original: detecta reuso da mesma Idempotency-Key com outro corpo.</summary>
    public string PayloadHash { get; private set; } = string.Empty;

    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public static IngestedEvent Create(
        Guid tenantId,
        string eventType,
        string payload,
        string payloadHash,
        string? idempotencyKey,
        DateTimeOffset receivedAt) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EventType = eventType,
        Payload = payload,
        PayloadHash = payloadHash,
        IdempotencyKey = idempotencyKey,
        ReceivedAt = receivedAt,
    };
}
