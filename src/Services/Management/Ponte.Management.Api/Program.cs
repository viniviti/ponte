using Ponte.BuildingBlocks.Messaging;
using Ponte.Management.Api.Api;
using Ponte.Management.Api.Application;
using Ponte.Management.Api.Infrastructure;
using Ponte.Management.Api.Realtime;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("ponte-management");
builder.Services.AddTenancy();

// --- Persistencia (SQL Server) -----------------------------------------------------
builder.Services.AddDbContextPool<ManagementDbContext>((sp, options) =>
    options.UseSqlServer(
        sp.GetRequiredService<IConfiguration>().GetConnectionString("ManagementDb"),
        sql => sql.EnableRetryOnFailure(maxRetryCount: 3)));

builder.Services.AddDatabaseInitializer<ManagementDbContext>(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<ManagementDbContext>("sqlserver", tags: ["ready"]);

// --- Mensageria ---------------------------------------------------------------------
builder.Services.AddRabbitMqMessaging(builder.Configuration);
builder.Services.AddOutbox<ManagementDbContext>(builder.Configuration, new SqlServerOutboxDialect());

builder.Services.AddScoped<MeteringHandler>();
builder.Services.AddScoped<RealtimeHandler>();

// Fila (competing consumers): cada tentativa e contada uma unica vez no metering.
builder.Services.AddConsumer(ConsumerDefinition
    .ForQueue(Topology.Queues.ManagementMetering)
    .Handle<DeliveryAttempted, MeteringHandler>());

// Topico (pub/sub por instancia): TODAS as replicas recebem e avisam seus navegadores.
builder.Services.AddConsumer(ConsumerDefinition
    .ForEachInstance("management.realtime", Topology.BusExchange, Topology.RoutingKeyFor<DeliveryAttempted>())
    .Handle<DeliveryAttempted, RealtimeHandler>());

// --- Aplicacao ----------------------------------------------------------------------
builder.Services.Configure<ManagementOptions>(builder.Configuration.GetSection(ManagementOptions.SectionName));
builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.SectionName));
builder.Services.AddScoped<EndpointService>();
builder.Services.AddScoped<ApiKeyService>();
builder.Services.AddScoped<StatsQuery>();
builder.Services.AddHostedService<DemoSeeder>(); // registrado depois do DatabaseInitializer: roda com o schema pronto

builder.Services.AddSignalR();
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
app.MapManagementEndpoints();
app.MapHub<DeliveriesHub>(DeliveriesHub.Route);

app.Run();
