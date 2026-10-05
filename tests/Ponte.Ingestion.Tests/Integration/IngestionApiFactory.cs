using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ponte.Ingestion.Api;
using Testcontainers.PostgreSql;

namespace Ponte.Ingestion.Tests.Integration;

/// <summary>
/// Sobe a API de verdade (pipeline HTTP, DI, EF Core) contra um PostgreSQL em container.
/// Os hosted services de mensageria sao removidos: aqui validamos que o evento e o
/// outbox sao gravados juntos; a publicacao no broker tem teste proprio.
/// </summary>
public sealed class IngestionApiFactory : WebApplicationFactory<IngestionApiMarker>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:IngestionDb"] = _postgres.GetConnectionString(),
        }));

        builder.ConfigureTestServices(services =>
        {
            var messagingHostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService) && !IsDatabaseInitializer(d))
                .ToList();

            foreach (var descriptor in messagingHostedServices)
            {
                services.Remove(descriptor);
            }
        });
    }

    private static bool IsDatabaseInitializer(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType is { IsGenericType: true } type
        && type.GetGenericTypeDefinition().Name.StartsWith("DatabaseInitializer", StringComparison.Ordinal);
}
