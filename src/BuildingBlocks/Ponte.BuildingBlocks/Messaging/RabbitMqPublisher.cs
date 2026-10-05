using RabbitMQ.Client;

namespace Ponte.BuildingBlocks.Messaging;

public sealed record OutgoingMessage(
    Guid MessageId,
    string Exchange,
    string RoutingKey,
    string Type,
    ReadOnlyMemory<byte> Body,
    string? TraceParent);

public interface IMessagePublisher
{
    /// <summary>
    /// Publica o lote e so retorna depois que o broker confirmou (publisher confirms)
    /// todas as mensagens. Lancar excecao significa "nada garantido": o Outbox tenta de novo.
    /// </summary>
    Task PublishBatchAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken);
}

public sealed class RabbitMqPublisher(
    RabbitMqConnection connection,
    ILogger<RabbitMqPublisher> logger,
    TopologyState? topology = null)
    : IMessagePublisher, IDisposable
{
    private static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IModel? _channel;

    public async Task PublishBatchAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return;
        }

        if (topology is not null)
        {
            await topology.Ready.WaitAsync(cancellationToken);
        }

        // IModel nao e thread-safe para publicacao: serializamos o acesso ao canal.
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = EnsureChannel();

            foreach (var message in messages)
            {
                using var activity = MessagingDiagnostics.StartPublish(message.RoutingKey, message.TraceParent);

                var properties = channel.CreateBasicProperties();
                properties.Persistent = true;
                properties.ContentType = "application/json";
                properties.MessageId = message.MessageId.ToString();
                properties.Type = message.Type;
                properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                properties.Headers = new Dictionary<string, object>();

                var traceParent = activity?.Id ?? message.TraceParent;
                if (traceParent is not null)
                {
                    properties.Headers[MessagingDiagnostics.TraceParentHeader] = traceParent;
                }

                channel.BasicPublish(message.Exchange, message.RoutingKey, mandatory: false, properties, message.Body);
            }

            // Uma unica espera de confirmacao para o lote inteiro: ordem de grandeza mais
            // rapido do que confirmar mensagem a mensagem.
            channel.WaitForConfirmsOrDie(ConfirmTimeout);
        }
        catch
        {
            ResetChannel();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private IModel EnsureChannel()
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        ResetChannel();
        _channel = connection.CreateChannel();
        _channel.ConfirmSelect();
        return _channel;
    }

    private void ResetChannel()
    {
        try
        {
            _channel?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Falha ao descartar canal de publicacao");
        }

        _channel = null;
    }

    public void Dispose()
    {
        ResetChannel();
        _gate.Dispose();
    }
}
