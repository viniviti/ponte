using System.Threading.RateLimiting;
using Ponte.BuildingBlocks.Hosting;
using Ponte.Gateway;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("ponte-gateway");

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<ApiKeyResolver>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Management"] ?? "http://localhost:5103");
    client.Timeout = TimeSpan.FromSeconds(3);
});

// Rate limit por tenant (token bucket): permite rajadas curtas e protege os servicos
// de um cliente que dispara em loop, sem afetar os demais tenants.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "1";
        return ValueTask.CompletedTask;
    };

    var limits = builder.Configuration.GetSection("RateLimiting");
    var tokenLimit = limits.GetValue("TokenLimit", 2000);
    var tokensPerSecond = limits.GetValue("TokensPerSecond", 1000);

    options.AddPolicy("per-tenant", httpContext =>
    {
        var partition = httpContext.Items[GatewayItems.TenantId] as string
                        ?? httpContext.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous";

        return RateLimitPartition.GetTokenBucketLimiter(partition, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = tokenLimit,
            TokensPerPeriod = tokensPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });
});

builder.Services.AddCors(options => options.AddPolicy("console", policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:5173" })
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    .WithExposedHeaders("Idempotent-Replayed", "Location", "Retry-After")));

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseCors("console");
app.MapDefaultEndpoints();

app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
app.UseRateLimiter();

app.MapReverseProxy().RequireRateLimiting("per-tenant");

app.Run();
