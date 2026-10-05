namespace Ponte.Management.Api.Domain;

public sealed class Tenant
{
    private Tenant()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Plan { get; private set; } = "free";

    public DateTimeOffset CreatedAt { get; private set; }

    public static Tenant Create(Guid id, string name, string plan, DateTimeOffset now) => new()
    {
        Id = id,
        Name = name,
        Plan = plan,
        CreatedAt = now,
    };
}

public sealed class ApiKey
{
    private ApiKey()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Inicio da chave, so para exibicao. A chave inteira nunca e armazenada.</summary>
    public string DisplayPrefix { get; private set; } = string.Empty;

    public string KeyHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    public static ApiKey Create(Guid tenantId, string name, string displayPrefix, string keyHash, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name,
        DisplayPrefix = displayPrefix,
        KeyHash = keyHash,
        CreatedAt = now,
    };

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
