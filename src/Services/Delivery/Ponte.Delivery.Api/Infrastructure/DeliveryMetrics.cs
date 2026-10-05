using System.Diagnostics.Metrics;

namespace Ponte.Delivery.Api.Infrastructure;

public sealed class DeliveryMetrics
{
    private readonly Counter<long> _attempts;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _fannedOut;
    private readonly Counter<long> _deferred;

    public DeliveryMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("Ponte.Delivery");
        _attempts = meter.CreateCounter<long>("ponte.delivery.attempts", description: "Tentativas de entrega por resultado");
        _duration = meter.CreateHistogram<double>("ponte.delivery.duration", unit: "ms", description: "Latencia do endpoint do cliente");
        _fannedOut = meter.CreateCounter<long>("ponte.delivery.fanned_out", description: "Entregas criadas a partir de eventos");
        _deferred = meter.CreateCounter<long>("ponte.delivery.deferred", description: "Entregas adiadas por circuit breaker/bulkhead");
    }

    public void Attempt(bool success, int? statusCode, double durationMs)
    {
        var statusClass = statusCode is { } code ? $"{code / 100}xx" : "network_error";
        _attempts.Add(1, new KeyValuePair<string, object?>("outcome", success ? "success" : "failure"),
            new KeyValuePair<string, object?>("status_class", statusClass));
        _duration.Record(durationMs, new KeyValuePair<string, object?>("status_class", statusClass));
    }

    public void FannedOut(int count) => _fannedOut.Add(count);

    public void Deferred(string reason) => _deferred.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
