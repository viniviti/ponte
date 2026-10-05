namespace Ponte.BuildingBlocks.Messaging;

/// <summary>
/// Conecta no broker e declara a topologia no startup, com backoff exponencial.
/// Nao bloqueia o host: a API sobe e responde /health/live enquanto o broker nao vem.
/// </summary>
internal sealed class TopologyInitializer(
    RabbitMqConnection connection,
    TopologyState state,
    IOptions<RabbitMqOptions> options,
    ILogger<TopologyInitializer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var channel = connection.CreateChannel();
                TopologyDeclarer.DeclareAll(channel, options.Value);
                state.MarkReady();
                logger.LogInformation("Topologia do RabbitMQ declarada");
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("RabbitMQ indisponivel ({Message}). Nova tentativa em {Delay}", ex.Message, delay);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }
}
