namespace Ponte.Contracts;

/// <summary>
/// Contrato base de toda mensagem que atravessa a fronteira de um microsservico.
/// O <see cref="Id"/> e usado como MessageId no broker e como chave de idempotencia
/// no consumidor (Inbox), entao precisa ser estavel entre retentativas de publicacao.
/// </summary>
public abstract record IntegrationEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Define a routing key padrao de uma mensagem no exchange de topico <see cref="Topology.BusExchange"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MessageRouteAttribute(string routingKey) : Attribute
{
    public string RoutingKey { get; } = routingKey;
}
