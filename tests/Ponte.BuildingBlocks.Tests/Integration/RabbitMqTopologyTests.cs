using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ponte.BuildingBlocks.Messaging;
using Ponte.Contracts;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace Ponte.BuildingBlocks.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class RabbitMqTopologyTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder().WithImage("rabbitmq:3.13-management-alpine").Build();
    private RabbitMqOptions _options = null!;
    private RabbitMqConnection _connection = null!;

    public async Task InitializeAsync()
    {
        await _rabbit.StartAsync();
        _options = new RabbitMqOptions { ConnectionString = _rabbit.GetConnectionString(), ClientName = "tests" };
        _connection = new RabbitMqConnection(Options.Create(_options), NullLogger<RabbitMqConnection>.Instance);

        using var channel = _connection.CreateChannel();
        TopologyDeclarer.DeclareAll(channel, _options);
    }

    public async Task DisposeAsync()
    {
        _connection.Dispose();
        await _rabbit.DisposeAsync();
    }

    [Fact]
    public async Task Evento_publicado_no_topico_chega_na_fila_do_consumidor()
    {
        var message = NewEvent();
        await PublishAsync(message, Topology.BusExchange, Topology.RoutingKeyFor<EventAccepted>());

        var received = await GetAsync(Topology.Queues.DeliveryEvents, TimeSpan.FromSeconds(5));

        received.Should().NotBeNull();
        received!.BasicProperties.Type.Should().Be(nameof(EventAccepted));
        received.BasicProperties.MessageId.Should().Be(message.Id.ToString());
        System.Text.Json.JsonSerializer.Deserialize<EventAccepted>(received.Body.Span, MessagingJson.Options)!
            .EventId.Should().Be(message.EventId);
    }

    [Fact]
    public async Task Fila_de_espera_devolve_a_mensagem_para_a_fila_de_jobs_quando_o_TTL_expira()
    {
        var message = NewEvent();
        await PublishAsync(message, Topology.RetryExchange, Topology.RetryRoutingKey(Topology.RetryTiers[0]));

        (await GetAsync(Topology.Queues.DeliveryJobs, TimeSpan.FromSeconds(1))).Should().BeNull("ainda esta esperando o TTL");

        var received = await GetAsync(Topology.Queues.DeliveryJobs, TimeSpan.FromSeconds(15));
        received.Should().NotBeNull();
        received!.BasicProperties.MessageId.Should().Be(message.Id.ToString());
    }

    [Fact]
    public async Task Consumidor_hospedado_entrega_a_mensagem_ao_handler_via_DI()
    {
        var handler = new CapturingHandler();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:ConnectionString"] = _options.ConnectionString,
            ["RabbitMq:ClientName"] = "tests-consumer",
        });
        builder.Services.AddRabbitMqMessaging(builder.Configuration);
        builder.Services.AddSingleton<IMessageHandler<EventAccepted>>(handler);
        builder.Services.AddConsumer(ConsumerDefinition.ForQueue(Topology.Queues.DeliveryEvents).Handle<EventAccepted>());

        using var host = builder.Build();
        await host.StartAsync();

        var message = NewEvent();
        await PublishAsync(message, Topology.BusExchange, Topology.RoutingKeyFor<EventAccepted>());

        var received = await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        received.EventId.Should().Be(message.EventId);

        await host.StopAsync();
    }

    private static EventAccepted NewEvent() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "order.paid", """{"id":1}""", DateTimeOffset.UtcNow);

    private async Task PublishAsync(EventAccepted message, string exchange, string routingKey)
    {
        using var publisher = new RabbitMqPublisher(_connection, NullLogger<RabbitMqPublisher>.Instance);
        var body = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(message, MessagingJson.Options);
        await publisher.PublishBatchAsync(
            [new OutgoingMessage(message.Id, exchange, routingKey, nameof(EventAccepted), body, null)],
            CancellationToken.None);
    }

    private async Task<BasicGetResult?> GetAsync(string queue, TimeSpan timeout)
    {
        using var channel = _connection.CreateChannel();
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            var result = channel.BasicGet(queue, autoAck: true);
            if (result is not null)
            {
                return result;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        return null;
    }

    private sealed class CapturingHandler : IMessageHandler<EventAccepted>
    {
        public TaskCompletionSource<EventAccepted> Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task HandleAsync(EventAccepted message, MessageContext context, CancellationToken cancellationToken)
        {
            Received.TrySetResult(message);
            return Task.CompletedTask;
        }
    }
}
