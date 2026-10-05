using System.Diagnostics.Metrics;

namespace Ponte.Ingestion.Api.Infrastructure;

public sealed class IngestionMetrics
{
    private readonly Counter<long> _accepted;
    private readonly Counter<long> _replayed;

    public IngestionMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("Ponte.Ingestion");
        _accepted = meter.CreateCounter<long>("ponte.events.accepted", description: "Eventos aceitos pela API");
        _replayed = meter.CreateCounter<long>("ponte.events.idempotent_replays", description: "Requisicoes repetidas com a mesma Idempotency-Key");
    }

    public void Accepted() => _accepted.Add(1);

    public void Replayed() => _replayed.Add(1);
}
