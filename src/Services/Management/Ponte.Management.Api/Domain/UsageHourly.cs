namespace Ponte.Management.Api.Domain;

/// <summary>
/// Agregado de uso por hora (metering para cobranca e para o dashboard). Mantido
/// incrementalmente a partir dos eventos DeliveryAttempted com MERGE ... WITH (HOLDLOCK).
/// </summary>
public sealed class UsageHourly
{
    public Guid TenantId { get; set; }

    public Guid EndpointId { get; set; }

    public DateTime HourBucket { get; set; }

    public long Succeeded { get; set; }

    public long Failed { get; set; }

    public long Dead { get; set; }

    public long TotalDurationMs { get; set; }
}
