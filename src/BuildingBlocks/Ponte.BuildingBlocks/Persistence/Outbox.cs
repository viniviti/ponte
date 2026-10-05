using Microsoft.EntityFrameworkCore;
using Ponte.BuildingBlocks.Messaging;
using Ponte.Contracts;

namespace Ponte.BuildingBlocks.Persistence;

public interface IOutbox
{
    /// <summary>Enfileira um evento de integracao no exchange de topico, com a routing key do contrato.</summary>
    void Enqueue<TEvent>(TEvent message) where TEvent : IntegrationEvent;

    /// <summary>Enfileira uma mensagem com destino explicito (ex.: jobs internos e filas de retry).</summary>
    void Enqueue<TMessage>(TMessage message, string exchange, string routingKey) where TMessage : notnull;
}

/// <summary>
/// Adiciona a mensagem ao DbContext atual SEM salvar. Quem chama faz um unico
/// SaveChanges, e o EF grava negocio + outbox na mesma transacao.
/// </summary>
public sealed class Outbox<TContext>(TContext dbContext, TimeProvider clock) : IOutbox
    where TContext : DbContext
{
    public void Enqueue<TEvent>(TEvent message) where TEvent : IntegrationEvent =>
        Add(message, message.Id, Topology.BusExchange, Topology.RoutingKeyFor(message.GetType()));

    public void Enqueue<TMessage>(TMessage message, string exchange, string routingKey) where TMessage : notnull =>
        Add(message, message is IntegrationEvent e ? e.Id : Guid.NewGuid(), exchange, routingKey);

    private void Add(object message, Guid messageId, string exchange, string routingKey)
    {
        var type = message.GetType();
        dbContext.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = messageId,
            Exchange = exchange,
            RoutingKey = routingKey,
            Type = type.Name,
            Payload = JsonSerializer.Serialize(message, type, MessagingJson.Options),
            TraceParent = Activity.Current?.Id,
            CreatedAt = clock.GetUtcNow(),
        });
    }
}

public interface IInbox
{
    /// <summary>
    /// Retorna false se a mensagem ja foi processada por este consumidor. Caso contrario,
    /// registra o processamento no DbContext (gravado junto com o SaveChanges do handler).
    /// A chave primaria (message_id, consumer) resolve a corrida entre duas instancias.
    /// </summary>
    Task<bool> TryBeginAsync(Guid messageId, string consumer, CancellationToken cancellationToken);
}

public sealed class Inbox<TContext>(TContext dbContext, TimeProvider clock) : IInbox
    where TContext : DbContext
{
    public async Task<bool> TryBeginAsync(Guid messageId, string consumer, CancellationToken cancellationToken)
    {
        var alreadyProcessed = await dbContext.Set<ProcessedMessage>()
            .AnyAsync(x => x.MessageId == messageId && x.Consumer == consumer, cancellationToken);

        if (alreadyProcessed)
        {
            return false;
        }

        dbContext.Set<ProcessedMessage>().Add(new ProcessedMessage
        {
            MessageId = messageId,
            Consumer = consumer,
            ProcessedAt = clock.GetUtcNow(),
        });

        return true;
    }
}
