namespace Ponte.BuildingBlocks.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string ConnectionString { get; set; } = "amqp://guest:guest@localhost:5672/";

    /// <summary>Nome exibido no painel do RabbitMQ (ajuda muito em incidente).</summary>
    public string ClientName { get; set; } = "ponte";

    /// <summary>Quantas mensagens nao confirmadas cada consumidor pode segurar (backpressure).</summary>
    public ushort PrefetchCount { get; set; } = 32;

    /// <summary>Quantos handlers rodam em paralelo por conexao.</summary>
    public int ConsumerConcurrency { get; set; } = 1;

    /// <summary>
    /// Quorum queues sao replicadas via Raft entre os nos do cluster: e a opcao de alta
    /// disponibilidade recomendada pelo RabbitMQ (e suportada pelo Amazon MQ).
    /// </summary>
    public bool UseQuorumQueues { get; set; } = true;

    /// <summary>Tentativas de processamento antes da mensagem ir para a DLQ (x-delivery-limit).</summary>
    public int DeliveryLimit { get; set; } = 5;
}
