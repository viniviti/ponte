using RabbitMQ.Client;

namespace Ponte.BuildingBlocks.Messaging;

/// <summary>
/// Propaga o contexto W3C (traceparent) pelo header da mensagem, ligando o trace da
/// requisicao HTTP original ao consumo no outro servico, mesmo passando pelo Outbox.
/// </summary>
public static class MessagingDiagnostics
{
    public const string SourceName = "Ponte.Messaging";
    public const string TraceParentHeader = "traceparent";

    private static readonly ActivitySource Source = new(SourceName);

    public static Activity? StartPublish(string routingKey, string? traceParent)
    {
        ActivityContext.TryParse(traceParent, null, out var parent);
        var activity = Source.StartActivity($"publish {routingKey}", ActivityKind.Producer, parent);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", routingKey);
        return activity;
    }

    public static Activity? StartConsume(string consumer, IBasicProperties properties, string routingKey)
    {
        string? traceParent = null;
        if (properties.Headers is not null
            && properties.Headers.TryGetValue(TraceParentHeader, out var raw)
            && raw is byte[] bytes)
        {
            traceParent = Encoding.UTF8.GetString(bytes);
        }

        ActivityContext.TryParse(traceParent, null, out var parent);
        var activity = Source.StartActivity($"consume {consumer}", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.operation", "process");
        activity?.SetTag("messaging.rabbitmq.destination.routing_key", routingKey);
        activity?.SetTag("messaging.message.id", properties.MessageId);
        return activity;
    }
}
