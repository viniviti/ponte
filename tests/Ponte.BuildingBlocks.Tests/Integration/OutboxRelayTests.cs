using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ponte.BuildingBlocks.Messaging;
using Ponte.BuildingBlocks.Persistence;
using Ponte.Contracts;
using Testcontainers.PostgreSql;

namespace Ponte.BuildingBlocks.Tests.Integration;

public sealed class OutboxTestDbContext(DbContextOptions<OutboxTestDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddMessagingTables();
}

[Trait("Category", "Integration")]
public sealed class OutboxRelayTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var services = new ServiceCollection();
        services.AddDbContext<OutboxTestDbContext>(o => o.UseNpgsql(_postgres.GetConnectionString()));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IOutbox, Outbox<OutboxTestDbContext>>();
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Publica_mensagens_pendentes_e_marca_como_processadas()
    {
        await EnqueueAsync(3);
        var publisher = new RecordingPublisher();

        var published = await CreateRelay(publisher).ProcessBatchAsync(CancellationToken.None);

        published.Should().Be(3);
        publisher.Messages.Should().HaveCount(3).And.OnlyContain(m => m.RoutingKey == "ingestion.event.accepted");
        (await PendingCountAsync()).Should().Be(0);
        (await CreateRelay(publisher).ProcessBatchAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task Falha_no_broker_mantem_mensagens_pendentes_e_conta_tentativas()
    {
        await EnqueueAsync(2);

        var published = await CreateRelay(new FailingPublisher()).ProcessBatchAsync(CancellationToken.None);

        published.Should().Be(0);
        await using var scope = _services.CreateAsyncScope();
        var messages = await scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>()
            .Set<OutboxMessage>().AsNoTracking().ToListAsync();

        messages.Should().OnlyContain(m => m.ProcessedAt == null && m.Attempts == 1 && m.LastError != null);
    }

    [Fact]
    public async Task Relays_concorrentes_nunca_publicam_a_mesma_mensagem_duas_vezes()
    {
        await EnqueueAsync(60);
        var publisher = new RecordingPublisher(delay: TimeSpan.FromMilliseconds(30));

        async Task DrainAsync()
        {
            var relay = CreateRelay(publisher, batchSize: 7);
            while (await relay.ProcessBatchAsync(CancellationToken.None) > 0)
            {
            }
        }

        // FOR UPDATE SKIP LOCKED: cada relay pega um lote diferente.
        await Task.WhenAll(DrainAsync(), DrainAsync(), DrainAsync());

        publisher.Messages.Select(m => m.MessageId).Should().OnlyHaveUniqueItems().And.HaveCount(60);
        (await PendingCountAsync()).Should().Be(0);
    }

    private OutboxRelay<OutboxTestDbContext> CreateRelay(IMessagePublisher publisher, int batchSize = 100) => new(
        _services.GetRequiredService<IServiceScopeFactory>(),
        publisher,
        new PostgresOutboxDialect(),
        Options.Create(new OutboxOptions { BatchSize = batchSize }),
        TimeProvider.System,
        NullLogger<OutboxRelay<OutboxTestDbContext>>.Instance);

    private async Task EnqueueAsync(int count)
    {
        await using var scope = _services.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
        for (var i = 0; i < count; i++)
        {
            outbox.Enqueue(new EventAccepted(Guid.NewGuid(), Guid.NewGuid(), "order.paid", "{}", DateTimeOffset.UtcNow));
        }

        await scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>().SaveChangesAsync();
    }

    private async Task<int> PendingCountAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>()
            .Set<OutboxMessage>().CountAsync(m => m.ProcessedAt == null);
    }

    private sealed class RecordingPublisher(TimeSpan? delay = null) : IMessagePublisher
    {
        private readonly ConcurrentQueue<OutgoingMessage> _messages = new();

        public IReadOnlyCollection<OutgoingMessage> Messages => _messages.ToArray();

        public async Task PublishBatchAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken)
        {
            if (delay is { } d)
            {
                await Task.Delay(d, cancellationToken);
            }

            foreach (var message in messages)
            {
                _messages.Enqueue(message);
            }
        }
    }

    private sealed class FailingPublisher : IMessagePublisher
    {
        public Task PublishBatchAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("broker fora do ar");
    }
}
