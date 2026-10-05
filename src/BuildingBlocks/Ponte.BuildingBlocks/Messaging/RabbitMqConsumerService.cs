using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Ponte.BuildingBlocks.Messaging;

/// <summary>
/// Hospeda um consumidor. Ack manual apenas depois do handler concluir (at-least-once):
/// se o processo morrer no meio, a mensagem volta para a fila. Falhas repetidas
/// vao para a DLQ (x-delivery-limit nas quorum queues).
/// </summary>
internal sealed class RabbitMqConsumerService(
    ConsumerDefinition definition,
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConsumerService> logger) : BackgroundService
{
    private readonly object _ackLock = new();
    private IModel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                StartConsuming(stoppingToken);
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Consumidor {Consumer} falhou ao iniciar. Nova tentativa em 5s", definition.Name);
                DisposeChannel();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private void StartConsuming(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var channel = connection.CreateChannel();
        channel.BasicQos(prefetchSize: 0, prefetchCount: settings.PrefetchCount, global: false);

        var queue = definition.Queue;
        if (queue is null)
        {
            // Fila exclusiva com nome gerado pelo servidor; some quando a instancia cai.
            queue = channel.QueueDeclare(queue: string.Empty, durable: false, exclusive: true, autoDelete: true, arguments: null).QueueName;
            foreach (var (exchange, routingKey) in definition.Bindings)
            {
                channel.QueueBind(queue, exchange, routingKey);
            }
        }

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += (_, delivery) => OnMessageAsync(channel, delivery, settings, stoppingToken);

        channel.BasicConsume(queue, autoAck: false, consumer: consumer);
        _channel = channel;

        logger.LogInformation("Consumidor {Consumer} escutando a fila {Queue}", definition.Name, queue);
    }

    private async Task OnMessageAsync(IModel channel, BasicDeliverEventArgs delivery, RabbitMqOptions settings, CancellationToken stoppingToken)
    {
        // O buffer do corpo e reaproveitado pelo client depois do handler: copiamos.
        var body = delivery.Body.ToArray();
        var type = delivery.BasicProperties.Type;
        var context = new MessageContext(
            Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId) ? messageId : Guid.Empty,
            delivery.RoutingKey,
            delivery.Redelivered);

        using var activity = MessagingDiagnostics.StartConsume(definition.Name, delivery.BasicProperties, delivery.RoutingKey);

        if (type is null || !definition.Dispatchers.TryGetValue(type, out var dispatcher))
        {
            logger.LogWarning("Mensagem de tipo desconhecido {Type} em {Consumer}: enviada para a DLQ", type, definition.Name);
            Reject(channel, delivery.DeliveryTag, requeue: false);
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await dispatcher(scope.ServiceProvider, body, context, stoppingToken);
            Ack(channel, delivery.DeliveryTag);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            // Em quorum queues o broker conta as entregas e aplica x-delivery-limit.
            // Em filas classicas damos uma unica segunda chance antes da DLQ.
            var requeue = settings.UseQuorumQueues || !delivery.Redelivered;
            logger.LogError(ex, "Erro processando {Type} ({MessageId}) em {Consumer}. Requeue: {Requeue}",
                type, context.MessageId, definition.Name, requeue);

            Reject(channel, delivery.DeliveryTag, requeue);
        }
    }

    private void Ack(IModel channel, ulong deliveryTag)
    {
        lock (_ackLock)
        {
            channel.BasicAck(deliveryTag, multiple: false);
        }
    }

    private void Reject(IModel channel, ulong deliveryTag, bool requeue)
    {
        lock (_ackLock)
        {
            channel.BasicNack(deliveryTag, multiple: false, requeue: requeue);
        }
    }

    private void DisposeChannel()
    {
        try
        {
            _channel?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Falha ao descartar canal do consumidor");
        }

        _channel = null;
    }

    public override void Dispose()
    {
        DisposeChannel();
        base.Dispose();
    }
}
