using RabbitMQ.Client;

namespace Ponte.BuildingBlocks.Messaging;

/// <summary>
/// Uma conexao TCP por processo (recomendacao oficial do RabbitMQ), com canais leves
/// criados sob demanda. A recuperacao automatica reabre conexao, canais e consumidores
/// quando o broker cai e volta.
/// </summary>
public sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger) : IDisposable
{
    private readonly object _sync = new();
    private IConnection? _connection;
    private bool _disposed;

    public bool IsConnected => _connection is { IsOpen: true };

    public IConnection GetConnection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_connection is not null)
        {
            return _connection;
        }

        lock (_sync)
        {
            if (_connection is not null)
            {
                return _connection;
            }

            var settings = options.Value;
            var factory = new ConnectionFactory
            {
                Uri = new Uri(settings.ConnectionString),
                ClientProvidedName = settings.ClientName,
                DispatchConsumersAsync = true,
                ConsumerDispatchConcurrency = Math.Max(1, settings.ConsumerConcurrency),
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                RequestedHeartbeat = TimeSpan.FromSeconds(30),
            };

            _connection = factory.CreateConnection();
            _connection.ConnectionShutdown += (_, args) =>
                logger.LogWarning("Conexao com RabbitMQ encerrada: {Reason}", args.ReplyText);

            logger.LogInformation("Conectado ao RabbitMQ como {ClientName}", settings.ClientName);
            return _connection;
        }
    }

    public IModel CreateChannel() => GetConnection().CreateModel();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _connection?.Close(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Falha ao fechar conexao com RabbitMQ");
        }

        _connection?.Dispose();
    }
}
