using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ponte.BuildingBlocks.Persistence;
using Ponte.Management.Api.Application;
using Ponte.Management.Api.Infrastructure;
using Testcontainers.MsSql;

namespace Ponte.Management.Tests.Integration;

/// <summary>Um SQL Server por classe de teste (o container demora alguns segundos para subir).</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public ServiceProvider Services { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();

        var services = new ServiceCollection();
        services.AddDbContext<ManagementDbContext>(o => o.UseSqlServer(_sqlServer.GetConnectionString()));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new ManagementOptions { AllowInsecureEndpointUrls = false }));
        services.AddScoped<IOutbox, Outbox<ManagementDbContext>>();
        services.AddScoped<IInbox, Inbox<ManagementDbContext>>();
        services.AddScoped<EndpointService>();
        services.AddScoped<ApiKeyService>();
        services.AddScoped<MeteringHandler>();
        services.AddScoped<StatsQuery>();
        Services = services.BuildServiceProvider();

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ManagementDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
        await _sqlServer.DisposeAsync();
    }
}
