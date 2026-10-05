namespace Ponte.BuildingBlocks.Messaging;

internal delegate Task MessageDispatcher(
    IServiceProvider services,
    ReadOnlyMemory<byte> body,
    MessageContext context,
    CancellationToken cancellationToken);

/// <summary>
/// Descreve um consumidor: de qual fila ler e qual handler atende cada tipo de mensagem.
/// Uma fila pode carregar varios tipos (ex.: EndpointUpserted e EndpointDeleted), e o
/// roteamento interno usa a propriedade AMQP "type".
/// </summary>
public sealed class ConsumerDefinition
{
    private readonly Dictionary<string, MessageDispatcher> _dispatchers = new(StringComparer.Ordinal);
    private readonly List<(string Exchange, string RoutingKey)> _bindings = [];

    private ConsumerDefinition(string name, string? queue)
    {
        Name = name;
        Queue = queue;
    }

    public string Name { get; }

    /// <summary>Fila duravel compartilhada. Nulo = fila exclusiva temporaria por instancia.</summary>
    public string? Queue { get; }

    internal IReadOnlyList<(string Exchange, string RoutingKey)> Bindings => _bindings;

    internal IReadOnlyDictionary<string, MessageDispatcher> Dispatchers => _dispatchers;

    /// <summary>
    /// Competing consumers: varias instancias leem a mesma fila e cada mensagem e
    /// processada por apenas uma delas. Ideal para trabalho (entregas, metering).
    /// </summary>
    public static ConsumerDefinition ForQueue(string queue) => new(queue, queue);

    /// <summary>
    /// Pub/sub por instancia: cada instancia cria sua propria fila exclusiva ligada ao
    /// topico, entao TODAS recebem a mensagem. Ideal para fan-out em tempo real (SignalR).
    /// </summary>
    public static ConsumerDefinition ForEachInstance(string name, string exchange, string routingKey)
    {
        var definition = new ConsumerDefinition(name, queue: null);
        definition._bindings.Add((exchange, routingKey));
        return definition;
    }

    /// <summary>Atende TMessage com o IMessageHandler&lt;TMessage&gt; registrado no DI.</summary>
    public ConsumerDefinition Handle<TMessage>() => Handle<TMessage, IMessageHandler<TMessage>>();

    /// <summary>
    /// Atende TMessage com um handler especifico. Necessario quando o mesmo tipo de
    /// mensagem tem handlers diferentes em consumidores diferentes (ex.: metering e tempo real).
    /// </summary>
    public ConsumerDefinition Handle<TMessage, THandler>()
        where THandler : IMessageHandler<TMessage>
    {
        _dispatchers[typeof(TMessage).Name] = static async (services, body, context, cancellationToken) =>
        {
            var message = JsonSerializer.Deserialize<TMessage>(body.Span, MessagingJson.Options)
                          ?? throw new InvalidOperationException($"Mensagem {typeof(TMessage).Name} vazia.");

            var handler = services.GetRequiredService<THandler>();
            await handler.HandleAsync(message, context, cancellationToken);
        };

        return this;
    }
}
