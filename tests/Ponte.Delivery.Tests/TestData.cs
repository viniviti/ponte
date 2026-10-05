using Ponte.Delivery.Api.Domain;

namespace Ponte.Delivery.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    public static IRetryPolicy RetryPolicy { get; } = new ExponentialBackoffRetryPolicy(Ponte.Contracts.Topology.RetryTiers);

    public static WebhookDelivery NewDelivery(string eventType = "order.paid", string payload = """{"id":1}""") =>
        WebhookDelivery.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), eventType, payload, Now, Now);

    public static WebhookResult Status(int code, TimeSpan? retryAfter = null) =>
        new(code, TimeSpan.FromMilliseconds(42), null, null, retryAfter);

    public static WebhookResult NetworkError() => WebhookResult.Failure("connection refused", TimeSpan.FromMilliseconds(3));
}
