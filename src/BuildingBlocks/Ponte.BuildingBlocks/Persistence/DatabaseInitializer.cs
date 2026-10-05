using Microsoft.EntityFrameworkCore;

namespace Ponte.BuildingBlocks.Persistence;

/// <summary>
/// Garante o schema no startup, esperando o banco ficar disponivel (docker-compose sobe
/// tudo junto). Em producao o recomendado e aplicar migrations no pipeline
/// (dotnet ef migrations bundle) e desligar isto com Database:AutoCreate=false.
/// </summary>
internal sealed class DatabaseInitializer<TContext>(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseInitializer<TContext>> logger) : IHostedService
    where TContext : DbContext
{
    private const int MaxAttempts = 30;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<TContext>();
                var created = await dbContext.Database.EnsureCreatedAsync(cancellationToken);
                logger.LogInformation("Schema de {Context} {Status}", typeof(TContext).Name, created ? "criado" : "ja existente");
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && ex is not OperationCanceledException)
            {
                logger.LogWarning("Banco de {Context} indisponivel (tentativa {Attempt}/{Max}): {Message}",
                    typeof(TContext).Name, attempt, MaxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class DatabaseInitializerExtensions
{
    public static IServiceCollection AddDatabaseInitializer<TContext>(this IServiceCollection services, IConfiguration configuration)
        where TContext : DbContext
    {
        if (configuration.GetValue("Database:AutoCreate", defaultValue: true))
        {
            services.AddHostedService<DatabaseInitializer<TContext>>();
        }

        return services;
    }
}
