namespace Ponte.BuildingBlocks.Messaging;

/// <summary>
/// Sinaliza quando a topologia (exchanges, filas e bindings) terminou de ser declarada.
/// O publisher espera por isso: publicar num exchange que ainda nao tem a fila ligada
/// faria o RabbitMQ descartar a mensagem silenciosamente no primeiro deploy.
/// </summary>
public sealed class TopologyState
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Ready => _ready.Task;

    public bool IsReady => _ready.Task.IsCompleted;

    public void MarkReady() => _ready.TrySetResult();
}
