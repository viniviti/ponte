namespace Ponte.BuildingBlocks.Messaging;

public sealed record MessageContext(Guid MessageId, string RoutingKey, bool Redelivered);

/// <summary>
/// Handler de uma mensagem. Roda dentro de um escopo de DI proprio, entao pode
/// depender de DbContext e de servicos scoped normalmente.
/// </summary>
public interface IMessageHandler<in TMessage>
{
    Task HandleAsync(TMessage message, MessageContext context, CancellationToken cancellationToken);
}
