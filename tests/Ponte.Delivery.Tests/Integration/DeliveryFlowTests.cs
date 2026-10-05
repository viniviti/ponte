using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Ponte.BuildingBlocks.Messaging;
using Ponte.BuildingBlocks.Persistence;
using Ponte.Contracts;
using Ponte.Delivery.Api.Application;
using Ponte.Delivery.Api.Application.Handlers;
using Ponte.Delivery.Api.Application.Resilience;
using Ponte.Delivery.Api.Domain;
using Ponte.Delivery.Api.Infrastructure;
using Ponte.Delivery.Api.Infrastructure.Http;
using Testcontainers.PostgreSql;

namespace Ponte.Delivery.Tests.Integration;

/// <summary>
/// Fluxo do servico de entregas contra um PostgreSQL real: fan-out, retry,
/// idempotencia e circuit breaker, com o envio HTTP substituido por um fake.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DeliveryFlowTests : IAsyncLifetime
{
    private static readonly MessageContext Context = new(Guid.NewGuid(), "test", Redelivered: false);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly FakeTimeProvider _clock = new(TestData.Now);
    private readonly IWebhookSender _sender = Substitute.For<IWebhookSender>();
    private readonly Guid _tenantId = Guid.NewGuid();
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddMemoryCache();
        services.AddDbContext<DeliveryDbContext>(o => o.UseNpgsql(_postgres.GetConnectionString()));
        services.AddSingleton<TimeProvider>(_clock);
        services.AddScoped<IOutbox, Outbox<DeliveryDbContext>>();
        services.AddScoped<IInbox, Inbox<DeliveryDbContext>>();
        services.AddScoped<EndpointDirectory>();
        services.AddSingleton<IRetryPolicy>(new ExponentialBackoffRetryPolicy(Topology.RetryTiers));
        services.Configure<CircuitBreakerOptions>(o =>
        {
            o.FailureThreshold = 1;
            o.BreakDuration = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<ICircuitBreakerRegistry, CircuitBreakerRegistry>();
        services.AddSingleton<EndpointBulkhead>();
        services.AddSingleton<DeliveryMetrics>();
        services.AddSingleton(_sender);
        services.AddScoped<EventAcceptedHandler>();
        services.AddScoped<EndpointUpsertedHandler>();
        services.AddScoped<DeliveryJobHandler>();
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DeliveryDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Fan_out_cria_uma_entrega_por_endpoint_inscrito_e_e_idempotente()
    {
        await SeedEndpointAsync("order.*");
        await SeedEndpointAsync("invoice.#");
        await SeedEndpointAsync("#");

        var accepted = NewEvent("order.paid");
        await HandleAsync<EventAcceptedHandler, EventAccepted>(accepted);
        await HandleAsync<EventAcceptedHandler, EventAccepted>(accepted); // duplicata do broker

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();

        (await db.Deliveries.CountAsync()).Should().Be(2, "order.* e # casam; invoice.# nao");
        (await db.Set<OutboxMessage>().CountAsync(m => m.Type == nameof(DeliveryJob))).Should().Be(2);
    }

    [Fact]
    public async Task Falha_agenda_retry_no_degrau_certo_e_publica_DeliveryAttempted()
    {
        var deliveryId = await CreateDeliveryAsync();
        _sender.SendAsync(Arg.Any<WebhookRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Status(503));

        await HandleAsync<DeliveryJobHandler, DeliveryJob>(new DeliveryJob(deliveryId));

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
        var delivery = await db.Deliveries.Include(d => d.Attempts).SingleAsync(d => d.Id == deliveryId);

        delivery.Status.Should().Be(DeliveryStatus.Scheduled);
        delivery.Attempts.Should().ContainSingle().Which.StatusCode.Should().Be(503);

        var outbox = await db.Set<OutboxMessage>().ToListAsync();
        outbox.Should().Contain(m => m.Exchange == Topology.RetryExchange && m.RoutingKey == "retry.5s");
        outbox.Should().Contain(m => m.Type == nameof(DeliveryAttempted) && m.Payload.Contains("\"outcome\":\"retrying\""));
    }

    [Fact]
    public async Task Job_duplicado_nao_dispara_dois_POSTs()
    {
        var deliveryId = await CreateDeliveryAsync();
        _sender.SendAsync(Arg.Any<WebhookRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Status(200));

        await HandleAsync<DeliveryJobHandler, DeliveryJob>(new DeliveryJob(deliveryId));
        await HandleAsync<DeliveryJobHandler, DeliveryJob>(new DeliveryJob(deliveryId));

        await _sender.Received(1).SendAsync(Arg.Any<WebhookRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Circuito_aberto_adia_a_entrega_sem_gastar_tentativa()
    {
        var endpointId = await SeedEndpointAsync("#");
        var first = await CreateDeliveryAsync(endpointId);
        var second = await CreateDeliveryAsync(endpointId);
        _sender.SendAsync(Arg.Any<WebhookRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.NetworkError());

        await HandleAsync<DeliveryJobHandler, DeliveryJob>(new DeliveryJob(first)); // falha e abre o circuito (threshold = 1)
        _clock.Advance(TimeSpan.FromSeconds(5));
        await HandleAsync<DeliveryJobHandler, DeliveryJob>(new DeliveryJob(second));

        await _sender.Received(1).SendAsync(Arg.Any<WebhookRequest>(), Arg.Any<CancellationToken>());

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
        var deferred = await db.Deliveries.SingleAsync(d => d.Id == second);

        deferred.Status.Should().Be(DeliveryStatus.Scheduled);
        deferred.AttemptCount.Should().Be(0);

        // Faltam 25s para o circuito fechar: o degrau que cobre isso e o de 30s.
        (await db.Set<OutboxMessage>().CountAsync(m => m.RoutingKey == "retry.30s")).Should().Be(1);
    }

    private async Task<Guid> SeedEndpointAsync(string pattern)
    {
        var message = new EndpointUpserted(
            Guid.NewGuid(), _tenantId, "https://cliente.example.com/hook", "whsec_dGVzdGUtdGVzdGUtdGVzdGU=",
            [pattern], Active: true, MaxConcurrency: 5, Version: 1);

        await HandleAsync<EndpointUpsertedHandler, EndpointUpserted>(message);
        return message.EndpointId;
    }

    private async Task<Guid> CreateDeliveryAsync(Guid? endpointId = null)
    {
        var endpoint = endpointId ?? await SeedEndpointAsync("#");
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
        var delivery = WebhookDelivery.Create(_tenantId, Guid.NewGuid(), endpoint, "order.paid", """{"id":1}""", TestData.Now, TestData.Now);
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery.Id;
    }

    private EventAccepted NewEvent(string eventType) =>
        new(Guid.NewGuid(), _tenantId, eventType, """{"id":1}""", TestData.Now);

    private async Task HandleAsync<THandler, TMessage>(TMessage message)
        where THandler : IMessageHandler<TMessage>
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<THandler>().HandleAsync(message, Context, CancellationToken.None);
    }
}
