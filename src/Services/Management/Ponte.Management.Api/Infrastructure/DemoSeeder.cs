using Ponte.BuildingBlocks.Security;
using Ponte.Management.Api.Application;
using Ponte.Management.Api.Domain;

namespace Ponte.Management.Api.Infrastructure;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool Enabled { get; set; }

    public Guid TenantId { get; set; } = Guid.Parse("8f14e45f-ceea-4672-a0c2-9f3c5f3e1d01");

    public string TenantName { get; set; } = "Loja Demo";

    /// <summary>Chave conhecida para o docker-compose e para o console em modo demo.</summary>
    public string ApiKey { get; set; } = "pk_test_ponte_demo_key_0000000000000000";

    public List<SeedEndpoint> Endpoints { get; set; } = [];
}

public sealed class SeedEndpoint
{
    public string Url { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<string> EventTypes { get; set; } = [];

    public int MaxConcurrency { get; set; } = 10;
}

/// <summary>
/// Cria um tenant de demonstracao com endpoints apontando para o echo-receiver, para que
/// "docker compose up" ja mostre o fluxo completo (inclusive retries e DLQ).
/// Passa pelo EndpointService, entao a replica do Delivery e alimentada pelo Outbox normalmente.
/// </summary>
internal sealed class DemoSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<SeedOptions> options,
    TimeProvider clock,
    ILogger<DemoSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var seed = options.Value;
        if (!seed.Enabled)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ManagementDbContext>();

        if (await dbContext.Tenants.AnyAsync(t => t.Id == seed.TenantId, cancellationToken))
        {
            return;
        }

        var now = clock.GetUtcNow();
        dbContext.Tenants.Add(Tenant.Create(seed.TenantId, seed.TenantName, "pro", now));
        dbContext.ApiKeys.Add(ApiKey.Create(seed.TenantId, "Demo", ApiKeys.DisplayPrefix(seed.ApiKey), ApiKeys.Hash(seed.ApiKey), now));
        await dbContext.SaveChangesAsync(cancellationToken);

        var endpoints = scope.ServiceProvider.GetRequiredService<EndpointService>();
        foreach (var endpoint in seed.Endpoints)
        {
            var result = await endpoints.CreateAsync(
                seed.TenantId,
                new EndpointInput(endpoint.Url, endpoint.Description, endpoint.EventTypes, endpoint.MaxConcurrency, Active: true),
                cancellationToken);

            if (!result.IsSuccess)
            {
                logger.LogWarning("Endpoint de demo {Url} recusado: {Error}", endpoint.Url, result.Error!.Message);
            }
        }

        logger.LogInformation("Tenant de demonstracao criado. API key: {ApiKey}", seed.ApiKey);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
