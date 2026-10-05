using System.Collections.Concurrent;

namespace Ponte.Delivery.Api.Application.Resilience;

/// <summary>
/// Bulkhead por endpoint: limita quantas entregas simultaneas cada endpoint recebe.
/// Um endpoint lento nao consegue sequestrar todos os workers e atrasar os outros
/// tenants (isolamento de falha, como as anteparas de um navio).
/// </summary>
public sealed class EndpointBulkhead
{
    private readonly ConcurrentDictionary<Guid, Compartment> _compartments = new();

    public bool TryEnter(Guid endpointId, int maxConcurrency, out Lease lease)
    {
        var limit = Math.Max(1, maxConcurrency);
        var compartment = _compartments.AddOrUpdate(
            endpointId,
            _ => new Compartment(limit),
            (_, existing) => existing.Limit == limit ? existing : new Compartment(limit));

        if (compartment.Semaphore.Wait(0))
        {
            lease = new Lease(compartment.Semaphore);
            return true;
        }

        lease = default;
        return false;
    }

    public int Available(Guid endpointId) =>
        _compartments.TryGetValue(endpointId, out var compartment) ? compartment.Semaphore.CurrentCount : -1;

    private sealed class Compartment(int limit)
    {
        public int Limit { get; } = limit;

        public SemaphoreSlim Semaphore { get; } = new(limit, limit);
    }

    public readonly struct Lease(SemaphoreSlim? semaphore) : IDisposable
    {
        public void Dispose() => semaphore?.Release();
    }
}
