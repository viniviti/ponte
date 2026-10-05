using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ponte.BuildingBlocks.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<TopologyState>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        services.AddHostedService<TopologyInitializer>();

        // Degraded (e nao Unhealthy): sem broker a API continua aceitando requisicoes, porque
        // as mensagens ficam no outbox. Tirar a instancia de rotacao pioraria o incidente.
        services.AddHealthChecks().AddCheck<RabbitMqHealthCheck>("rabbitmq", failureStatus: HealthStatus.Degraded, tags: ["ready"]);
        return services;
    }

    /// <summary>
    /// Registra um consumidor. Usamos AddSingleton&lt;IHostedService&gt; (e nao AddHostedService)
    /// porque este ultimo deduplica pelo tipo e so o primeiro consumidor subiria.
    /// </summary>
    public static IServiceCollection AddConsumer(this IServiceCollection services, ConsumerDefinition definition)
    {
        services.AddSingleton<IHostedService>(sp => new RabbitMqConsumerService(
            definition,
            sp.GetRequiredService<RabbitMqConnection>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptions<RabbitMqOptions>>(),
            sp.GetRequiredService<ILogger<RabbitMqConsumerService>>()));

        return services;
    }
}
