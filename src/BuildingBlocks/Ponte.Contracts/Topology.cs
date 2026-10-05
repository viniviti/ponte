namespace Ponte.Contracts;

/// <summary>
/// Topologia completa do RabbitMQ em um unico lugar. Todos os servicos declaram a
/// topologia inteira no startup (operacao idempotente), entao nenhuma mensagem e
/// perdida por ser publicada antes do consumidor existir.
/// </summary>
public static class Topology
{
    /// <summary>Exchange de topico para eventos de integracao entre servicos (pub/sub).</summary>
    public const string BusExchange = "ponte.bus";

    /// <summary>Exchange direct que alimenta a fila de trabalho de entregas (competing consumers).</summary>
    public const string DeliveryExchange = "ponte.delivery";

    /// <summary>Exchange direct que roteia para as filas de espera (retry com TTL).</summary>
    public const string RetryExchange = "ponte.delivery.retry";

    /// <summary>Dead-letter exchange para mensagens envenenadas (falha de processamento).</summary>
    public const string DeadLetterExchange = "ponte.dlx";

    public const string DeliveryJobRoutingKey = "delivery.job";

    public static class Queues
    {
        public const string DeliveryEvents = "delivery.events";
        public const string DeliveryEndpoints = "delivery.endpoints";
        public const string DeliveryJobs = "delivery.jobs";
        public const string ManagementMetering = "management.metering";
    }

    /// <summary>
    /// Degraus de espera do retry exponencial. Cada degrau e uma fila com
    /// x-message-ttl que, ao expirar, devolve a mensagem para <see cref="Queues.DeliveryJobs"/>.
    /// Filas por degrau evitam o bloqueio de cabeca de fila que o TTL por mensagem causa.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> RetryTiers =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6),
    ];

    public static string RetryRoutingKey(TimeSpan tier) => $"retry.{FormatTier(tier)}";

    public static string RetryQueueName(TimeSpan tier) => $"delivery.retry.{FormatTier(tier)}";

    public static string DeadLetterQueueName(string queue) => $"{queue}.dlq";

    public static string RoutingKeyFor<T>() => RoutingKeyFor(typeof(T));

    public static string RoutingKeyFor(Type messageType)
    {
        var attribute = (MessageRouteAttribute?)Attribute.GetCustomAttribute(messageType, typeof(MessageRouteAttribute));
        return attribute?.RoutingKey
               ?? throw new InvalidOperationException($"{messageType.Name} nao possui [MessageRoute].");
    }

    private static string FormatTier(TimeSpan tier) => tier switch
    {
        { TotalHours: >= 1 } when tier.TotalHours % 1 == 0 => $"{(int)tier.TotalHours}h",
        { TotalMinutes: >= 1 } when tier.TotalMinutes % 1 == 0 => $"{(int)tier.TotalMinutes}m",
        _ => $"{(int)tier.TotalSeconds}s",
    };
}
