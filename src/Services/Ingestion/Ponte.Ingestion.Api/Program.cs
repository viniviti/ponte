using Ponte.BuildingBlocks.Messaging;
using Ponte.Ingestion.Api.Api;
using Ponte.Ingestion.Api.Application;
using Ponte.Ingestion.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("ponte-ingestion");

// Corpo maximo da requisicao: payload (256 KB) + envelope.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 512 * 1024);

builder.Services.AddTenancy();

// Pool de DbContext: reaproveita instancias e reduz alocacao no caminho quente.
builder.Services.AddDbContextPool<IngestionDbContext>((sp, options) =>
    options.UseNpgsql(
        sp.GetRequiredService<IConfiguration>().GetConnectionString("IngestionDb"),
        npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3)));

builder.Services.AddDatabaseInitializer<IngestionDbContext>(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<IngestionDbContext>("postgres", tags: ["ready"]);

builder.Services.AddRabbitMqMessaging(builder.Configuration);
builder.Services.AddOutbox<IngestionDbContext>(builder.Configuration, new PostgresOutboxDialect());

builder.Services.AddSingleton<IngestionMetrics>();
builder.Services.AddScoped<IngestEventHandler>();

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
app.MapEventsEndpoints();

app.Run();
