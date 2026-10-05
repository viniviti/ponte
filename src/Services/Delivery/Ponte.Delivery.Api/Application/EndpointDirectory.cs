using Microsoft.Extensions.Caching.Memory;
using Ponte.Delivery.Api.Infrastructure;

namespace Ponte.Delivery.Api.Application;

public sealed record EndpointSubscription(Guid Id, IReadOnlyList<string> EventTypes);

/// <summary>
/// Cache-aside das inscricoes por tenant. No pico, milhares de eventos por segundo do
/// mesmo tenant consultariam a mesma lista: com cache de poucos segundos isso vira
/// uma consulta. Invalidado localmente quando chega atualizacao de endpoint.
/// </summary>
public sealed class EndpointDirectory(DeliveryDbContext dbContext, IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<EndpointSubscription>> GetActiveAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(Key(tenantId), out IReadOnlyList<EndpointSubscription>? cached) && cached is not null)
        {
            return cached;
        }

        var subscriptions = await dbContext.Endpoints
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Active && !e.Deleted)
            .Select(e => new EndpointSubscription(e.Id, e.EventTypes))
            .ToListAsync(cancellationToken);

        cache.Set(Key(tenantId), (IReadOnlyList<EndpointSubscription>)subscriptions, Ttl);
        return subscriptions;
    }

    public void Invalidate(Guid tenantId) => cache.Remove(Key(tenantId));

    private static string Key(Guid tenantId) => $"endpoints:{tenantId:N}";
}
