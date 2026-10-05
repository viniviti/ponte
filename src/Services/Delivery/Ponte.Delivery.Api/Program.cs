using Ponte.Delivery.Api.Api;
using Ponte.Delivery.Api.Application;
using Ponte.Delivery.Api.Application.Handlers;
using Ponte.Delivery.Api.Application.Resilience;
using Ponte.Delivery.Api.Domain;
using Ponte.Delivery.Api.Infrastructure;
using Ponte.Delivery.Api.Infrastructure.Http;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("ponte-delivery");
builder.Services.AddTenancy();
builder.Services.AddMemoryCache();

// --- Persistencia (PostgreSQL) ----------------------------------------------------
builder.Services.AddDbContextPool<DeliveryDbContext>((sp, options) =>
    options.UseNpgsql(
        sp.GetRequiredService<IConfiguration>().GetConnectionString("DeliveryDb"),
        npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3)));

builder.Services.AddDatabaseInitializer<DeliveryDbContext>(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<DeliveryDbContext>("postgres", tags: ["ready"]);

// --- Mensageria (RabbitMQ + Outbox/Inbox) ------------------------------------------
builder.Services.AddRabbitMqMessaging(builder.Configuration);
builder.Services.AddOutbox<DeliveryDbContext>(builder.Configuration, new PostgresOutboxDialect());

builder.Services.AddScoped<IMessageHandler<EventAccepted>, EventAcceptedHandler>();
builder.Services.AddScoped<IMessageHandler<EndpointUpserted>, EndpointUpsertedHandler>();
builder.Services.AddScoped<IMessageHandler<EndpointDeleted>, EndpointDeletedHandler>();
builder.Services.AddScoped<IMessageHandler<DeliveryJob>, DeliveryJobHandler>();

builder.Services.AddConsumer(ConsumerDefinition.ForQueue(Topology.Queues.DeliveryEvents).Handle<EventAccepted>());
builder.Services.AddConsumer(ConsumerDefinition.ForQueue(Topology.Queues.DeliveryEndpoints)
    .Handle<EndpointUpserted>()
    .Handle<EndpointDeleted>());
builder.Services.AddConsumer(ConsumerDefinition.ForQueue(Topology.Queues.DeliveryJobs).Handle<DeliveryJob>());

// --- Dominio e resiliencia ---------------------------------------------------------
builder.Services.Configure<CircuitBreakerOptions>(builder.Configuration.GetSection(CircuitBreakerOptions.SectionName));
builder.Services.AddSingleton<IRetryPolicy>(new ExponentialBackoffRetryPolicy(Topology.RetryTiers));
builder.Services.AddSingleton<ICircuitBreakerRegistry, CircuitBreakerRegistry>();
builder.Services.AddSingleton<EndpointBulkhead>();
builder.Services.AddSingleton<DeliveryMetrics>();
builder.Services.AddScoped<EndpointDirectory>();

// --- HTTP de saida -----------------------------------------------------------------
builder.Services.Configure<WebhookSenderOptions>(builder.Configuration.GetSection(WebhookSenderOptions.SectionName));
builder.Services
    .AddHttpClient<WebhookHttpSender>(client =>
    {
        client.Timeout = Timeout.InfiniteTimeSpan; // o timeout por tentativa e controlado no sender
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Ponte-Webhooks/1.0");
    })
    .ConfigurePrimaryHttpMessageHandler(sp =>
        SsrfSafeHandlerFactory.Create(sp.GetRequiredService<IOptions<WebhookSenderOptions>>().Value));

// Decorator registrado a mao: IWebhookSender = Instrumented(WebhookHttpSender)
builder.Services.AddScoped<IWebhookSender>(sp => new InstrumentedWebhookSender(
    sp.GetRequiredService<WebhookHttpSender>(),
    sp.GetRequiredService<DeliveryMetrics>()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapDefaultEndpoints();
app.MapDeliveriesEndpoints();

app.Run();
