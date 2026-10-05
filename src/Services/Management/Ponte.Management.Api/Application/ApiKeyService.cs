using Ponte.BuildingBlocks.Security;
using Ponte.Management.Api.Domain;
using Ponte.Management.Api.Infrastructure;

namespace Ponte.Management.Api.Application;

public sealed record CreatedApiKey(Guid Id, string Name, string Key, string DisplayPrefix, DateTimeOffset CreatedAt);

public sealed class ApiKeyService(ManagementDbContext dbContext, TimeProvider clock)
{
    /// <summary>A chave em texto puro e devolvida UMA vez. Depois disso so existe o hash.</summary>
    public async Task<Result<CreatedApiKey>> CreateAsync(Guid tenantId, string? name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
        {
            return new Error("name_invalid", "Informe um nome de ate 80 caracteres para a chave.");
        }

        var plain = ApiKeys.Generate();
        var apiKey = ApiKey.Create(tenantId, name.Trim(), ApiKeys.DisplayPrefix(plain), ApiKeys.Hash(plain), clock.GetUtcNow());
        dbContext.ApiKeys.Add(apiKey);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreatedApiKey(apiKey.Id, apiKey.Name, plain, apiKey.DisplayPrefix, apiKey.CreatedAt);
    }

    public async Task<bool> RevokeAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var apiKey = await dbContext.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.TenantId == tenantId, cancellationToken);
        if (apiKey is null)
        {
            return false;
        }

        apiKey.Revoke(clock.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Usado pelo Gateway (com cache) para traduzir hash da chave em tenant.</summary>
    public Task<Guid?> ResolveTenantAsync(string keyHash, CancellationToken cancellationToken) =>
        dbContext.ApiKeys
            .AsNoTracking()
            .Where(k => k.KeyHash == keyHash && k.RevokedAt == null)
            .Select(k => (Guid?)k.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
}
