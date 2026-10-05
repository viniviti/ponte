namespace Ponte.Contracts;

// ---------------------------------------------------------------------------
// Ingestion -> Delivery
// ---------------------------------------------------------------------------

/// <summary>Um evento foi aceito pela API de ingestao e precisa ser distribuido.</summary>
[MessageRoute("ingestion.event.accepted")]
public sealed record EventAccepted(
    Guid EventId,
    Guid TenantId,
    string EventType,
    string Payload,
    DateTimeOffset ReceivedAt) : IntegrationEvent;

// ---------------------------------------------------------------------------
// Management -> Delivery (replica de leitura dos endpoints)
// ---------------------------------------------------------------------------

/// <summary>
/// Estado completo de um endpoint (event-carried state transfer). O campo
/// <see cref="Version"/> permite descartar mensagens fora de ordem. Durante a rotacao
/// de segredo, <see cref="PreviousSecret"/> continua assinando ate expirar (zero downtime).
/// </summary>
[MessageRoute("management.endpoint.upserted")]
public sealed record EndpointUpserted(
    Guid EndpointId,
    Guid TenantId,
    string Url,
    string Secret,
    IReadOnlyList<string> EventTypes,
    bool Active,
    int MaxConcurrency,
    long Version,
    string? PreviousSecret = null,
    DateTimeOffset? PreviousSecretExpiresAt = null) : IntegrationEvent;

[MessageRoute("management.endpoint.deleted")]
public sealed record EndpointDeleted(Guid EndpointId, Guid TenantId, long Version) : IntegrationEvent;

// ---------------------------------------------------------------------------
// Delivery -> Management (metering + tempo real)
// ---------------------------------------------------------------------------

[MessageRoute("delivery.attempted")]
public sealed record DeliveryAttempted(
    Guid DeliveryId,
    Guid EventId,
    Guid EndpointId,
    Guid TenantId,
    string EventType,
    int AttemptNumber,
    string Outcome,
    int? StatusCode,
    long DurationMs,
    DateTimeOffset AttemptedAt,
    DateTimeOffset? NextAttemptAt) : IntegrationEvent;

/// <summary>Valores possiveis de <see cref="DeliveryAttempted.Outcome"/>.</summary>
public static class DeliveryOutcomes
{
    public const string Succeeded = "succeeded";
    public const string Retrying = "retrying";
    public const string Dead = "dead";
}
