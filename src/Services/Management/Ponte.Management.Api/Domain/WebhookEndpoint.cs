using Ponte.BuildingBlocks.Security;

namespace Ponte.Management.Api.Domain;

/// <summary>
/// Endpoint de webhook de um tenant. Cada mudanca incrementa <see cref="Version"/>, que
/// viaja no evento EndpointUpserted e permite ao Delivery ignorar mensagens fora de ordem.
/// </summary>
public sealed class WebhookEndpoint
{
    public static readonly TimeSpan SecretRotationGracePeriod = TimeSpan.FromHours(24);

    private WebhookEndpoint()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Url { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string Secret { get; private set; } = string.Empty;

    public string? PreviousSecret { get; private set; }

    public DateTimeOffset? PreviousSecretExpiresAt { get; private set; }

    /// <summary>Armazenado como JSON (primitive collection do EF Core 8 no SQL Server).</summary>
    public string[] EventTypes { get; private set; } = [];

    public bool Active { get; private set; }

    public int MaxConcurrency { get; private set; }

    public long Version { get; private set; }

    public bool Deleted { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>rowversion do SQL Server: concorrencia otimista entre duas edicoes no console.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public static WebhookEndpoint Create(
        Guid tenantId,
        Uri url,
        string? description,
        IEnumerable<string> eventTypes,
        int maxConcurrency,
        DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Url = url.ToString(),
        Description = description,
        Secret = StandardWebhook.GenerateSecret(),
        EventTypes = Normalize(eventTypes),
        Active = true,
        MaxConcurrency = maxConcurrency,
        Version = 1,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Update(Uri url, string? description, IEnumerable<string> eventTypes, int maxConcurrency, bool active, DateTimeOffset now)
    {
        Url = url.ToString();
        Description = description;
        EventTypes = Normalize(eventTypes);
        MaxConcurrency = maxConcurrency;
        Active = active;
        Touch(now);
    }

    /// <summary>
    /// Gera um novo segredo e mantem o anterior valido por 24h: as entregas saem com as
    /// duas assinaturas, e o cliente troca o segredo no seu lado sem perder eventos.
    /// </summary>
    public void RotateSecret(DateTimeOffset now)
    {
        PreviousSecret = Secret;
        PreviousSecretExpiresAt = now + SecretRotationGracePeriod;
        Secret = StandardWebhook.GenerateSecret();
        Touch(now);
    }

    public void Delete(DateTimeOffset now)
    {
        Deleted = true;
        Active = false;
        Touch(now);
    }

    public EndpointUpserted ToUpsertedEvent() =>
        new(Id, TenantId, Url, Secret, EventTypes, Active, MaxConcurrency, Version, PreviousSecret, PreviousSecretExpiresAt);

    public EndpointDeleted ToDeletedEvent() => new(Id, TenantId, Version);

    private void Touch(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now;
    }

    private static string[] Normalize(IEnumerable<string> eventTypes) =>
        eventTypes.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct().Order().ToArray();
}
