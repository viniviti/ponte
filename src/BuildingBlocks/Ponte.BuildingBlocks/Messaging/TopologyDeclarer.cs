using Ponte.Contracts;
using RabbitMQ.Client;

namespace Ponte.BuildingBlocks.Messaging;

/// <summary>
/// Declara exchanges, filas e bindings. Toda declaracao no RabbitMQ e idempotente,
/// entao rodar em todos os servicos no startup e seguro e elimina corridas de deploy.
/// </summary>
public static class TopologyDeclarer
{
    public static void DeclareAll(IModel channel, RabbitMqOptions options)
    {
        channel.ExchangeDeclare(Topology.BusExchange, ExchangeType.Topic, durable: true, autoDelete: false);
        channel.ExchangeDeclare(Topology.DeliveryExchange, ExchangeType.Direct, durable: true, autoDelete: false);
        channel.ExchangeDeclare(Topology.RetryExchange, ExchangeType.Direct, durable: true, autoDelete: false);
        channel.ExchangeDeclare(Topology.DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false);

        // Filas de trabalho (competing consumers): cada mensagem vai para UM consumidor.
        DeclareWorkQueue(channel, options, Topology.Queues.DeliveryEvents,
            (Topology.BusExchange, Topology.RoutingKeyFor<EventAccepted>()));

        DeclareWorkQueue(channel, options, Topology.Queues.DeliveryEndpoints,
            (Topology.BusExchange, "management.endpoint.*"));

        DeclareWorkQueue(channel, options, Topology.Queues.DeliveryJobs,
            (Topology.DeliveryExchange, Topology.DeliveryJobRoutingKey));

        DeclareWorkQueue(channel, options, Topology.Queues.ManagementMetering,
            (Topology.BusExchange, Topology.RoutingKeyFor<DeliveryAttempted>()));

        // Filas de espera do retry: sem consumidor. Quando o TTL expira, o RabbitMQ
        // faz dead-letter de volta para a fila de entregas.
        foreach (var tier in Topology.RetryTiers)
        {
            var queue = Topology.RetryQueueName(tier);
            var arguments = BaseArguments(options);
            arguments["x-message-ttl"] = (int)tier.TotalMilliseconds;
            arguments["x-dead-letter-exchange"] = Topology.DeliveryExchange;
            arguments["x-dead-letter-routing-key"] = Topology.DeliveryJobRoutingKey;

            channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false, arguments: arguments);
            channel.QueueBind(queue, Topology.RetryExchange, Topology.RetryRoutingKey(tier));
        }
    }

    private static void DeclareWorkQueue(
        IModel channel,
        RabbitMqOptions options,
        string queue,
        params (string Exchange, string RoutingKey)[] bindings)
    {
        var deadLetterQueue = Topology.DeadLetterQueueName(queue);
        channel.QueueDeclare(deadLetterQueue, durable: true, exclusive: false, autoDelete: false, arguments: BaseArguments(options));
        channel.QueueBind(deadLetterQueue, Topology.DeadLetterExchange, queue);

        var arguments = BaseArguments(options);
        arguments["x-dead-letter-exchange"] = Topology.DeadLetterExchange;
        arguments["x-dead-letter-routing-key"] = queue;
        if (options.UseQuorumQueues)
        {
            arguments["x-delivery-limit"] = options.DeliveryLimit;
        }

        channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false, arguments: arguments);
        foreach (var (exchange, routingKey) in bindings)
        {
            channel.QueueBind(queue, exchange, routingKey);
        }
    }

    private static Dictionary<string, object> BaseArguments(RabbitMqOptions options)
    {
        var arguments = new Dictionary<string, object>();
        if (options.UseQuorumQueues)
        {
            arguments["x-queue-type"] = "quorum";
        }

        return arguments;
    }
}
