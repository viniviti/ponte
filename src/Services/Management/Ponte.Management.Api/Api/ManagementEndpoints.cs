using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Ponte.Management.Api.Application;
using Ponte.Management.Api.Domain;
using Ponte.Management.Api.Infrastructure;

namespace Ponte.Management.Api.Api;

public sealed record EndpointView(
    Guid Id,
    string Url,
    string? Description,
    IReadOnlyList<string> EventTypes,
    bool Active,
    int MaxConcurrency,
    long Version,
    bool SecretRotationInProgress,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    EndpointStats? Last24h)
{
    public static EndpointView From(WebhookEndpoint e, EndpointStats? stats, DateTimeOffset now) => new(
        e.Id, e.Url, e.Description, e.EventTypes, e.Active, e.MaxConcurrency, e.Version,
        e.PreviousSecretExpiresAt > now, e.CreatedAt, e.UpdatedAt, stats);
}

public sealed record EndpointSecretView(string Secret, DateTimeOffset? PreviousSecretExpiresAt);

public sealed record ApiKeyView(Guid Id, string Name, string DisplayPrefix, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);

public sealed record CreateApiKeyRequest(string? Name);

public sealed record TenantView(Guid Id, string Name, string Plan);

public sealed record ResolvedApiKey(Guid TenantId);

public static class ManagementEndpoints
{
    public const string ApiKeyHashHeader = "X-Api-Key-Hash";

    public static IEndpointRouteBuilder MapManagementEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireTenant();

        v1.MapGet("/tenant", GetTenantAsync).WithTags("Tenant");

        var endpoints = v1.MapGroup("/endpoints").WithTags("Endpoints");
        endpoints.MapGet("/", ListEndpointsAsync);
        endpoints.MapGet("/{id:guid}", GetEndpointAsync);
        endpoints.MapPost("/", CreateEndpointAsync);
        endpoints.MapPut("/{id:guid}", UpdateEndpointAsync);
        endpoints.MapDelete("/{id:guid}", DeleteEndpointAsync);
        endpoints.MapGet("/{id:guid}/secret", GetSecretAsync);
        endpoints.MapPost("/{id:guid}/rotate-secret", RotateSecretAsync);

        var keys = v1.MapGroup("/api-keys").WithTags("API keys");
        keys.MapGet("/", ListApiKeysAsync);
        keys.MapPost("/", CreateApiKeyAsync);
        keys.MapDelete("/{id:guid}", RevokeApiKeyAsync);

        v1.MapGet("/stats/overview", (ITenantContext tenant, StatsQuery stats, int? hours, CancellationToken ct) =>
                stats.OverviewAsync(tenant.TenantId, hours ?? 24, ct))
            .WithTags("Stats");

        // Rota interna: NAO e exposta pelo Gateway, so e alcancavel na rede privada.
        app.MapGet("/internal/api-keys/resolve", ResolveApiKeyAsync)
            .WithTags("Internal")
            .ExcludeFromDescription();

        return app;
    }

    private static async Task<Results<Ok<TenantView>, NotFound>> GetTenantAsync(
        ITenantContext tenant, ManagementDbContext dbContext, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        var view = await dbContext.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new TenantView(t.Id, t.Name, t.Plan))
            .FirstOrDefaultAsync(cancellationToken);

        return view is null ? TypedResults.NotFound() : TypedResults.Ok(view);
    }

    private static async Task<Ok<List<EndpointView>>> ListEndpointsAsync(
        ITenantContext tenant, ManagementDbContext dbContext, StatsQuery stats, TimeProvider clock, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        var endpoints = await dbContext.Endpoints.AsNoTracking()
            .Where(e => e.TenantId == tenantId && !e.Deleted)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        var perEndpoint = await stats.PerEndpointAsync(tenantId, 24, cancellationToken);
        var now = clock.GetUtcNow();

        return TypedResults.Ok(endpoints
            .Select(e => EndpointView.From(e, perEndpoint.GetValueOrDefault(e.Id), now))
            .ToList());
    }

    private static async Task<Results<Ok<EndpointView>, NotFound>> GetEndpointAsync(
        Guid id, ITenantContext tenant, ManagementDbContext dbContext, TimeProvider clock, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        var endpoint = await dbContext.Endpoints.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId && !e.Deleted, cancellationToken);

        return endpoint is null ? TypedResults.NotFound() : TypedResults.Ok(EndpointView.From(endpoint, null, clock.GetUtcNow()));
    }

    private static async Task<IResult> CreateEndpointAsync(
        EndpointInput input, ITenantContext tenant, EndpointService service, TimeProvider clock, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(tenant.TenantId, input, cancellationToken);
        return result.Match<IResult>(
            e => TypedResults.Created($"/v1/endpoints/{e.Id}", EndpointView.From(e, null, clock.GetUtcNow())),
            error => error.ToProblem());
    }

    private static async Task<IResult> UpdateEndpointAsync(
        Guid id, EndpointInput input, ITenantContext tenant, EndpointService service, TimeProvider clock, CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(tenant.TenantId, id, input, cancellationToken);
        return result.Match<IResult>(e => TypedResults.Ok(EndpointView.From(e, null, clock.GetUtcNow())), error => error.ToProblem());
    }

    private static async Task<IResult> DeleteEndpointAsync(
        Guid id, ITenantContext tenant, EndpointService service, CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(tenant.TenantId, id, cancellationToken);
        return result.Match<IResult>(_ => TypedResults.NoContent(), error => error.ToProblem());
    }

    private static async Task<Results<Ok<EndpointSecretView>, NotFound>> GetSecretAsync(
        Guid id, ITenantContext tenant, ManagementDbContext dbContext, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        var secret = await dbContext.Endpoints.AsNoTracking()
            .Where(e => e.Id == id && e.TenantId == tenantId && !e.Deleted)
            .Select(e => new EndpointSecretView(e.Secret, e.PreviousSecretExpiresAt))
            .FirstOrDefaultAsync(cancellationToken);

        return secret is null ? TypedResults.NotFound() : TypedResults.Ok(secret);
    }

    private static async Task<IResult> RotateSecretAsync(
        Guid id, ITenantContext tenant, EndpointService service, CancellationToken cancellationToken)
    {
        var result = await service.RotateSecretAsync(tenant.TenantId, id, cancellationToken);
        return result.Match<IResult>(
            e => TypedResults.Ok(new EndpointSecretView(e.Secret, e.PreviousSecretExpiresAt)),
            error => error.ToProblem());
    }

    private static async Task<Ok<List<ApiKeyView>>> ListApiKeysAsync(
        ITenantContext tenant, ManagementDbContext dbContext, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        var keys = await dbContext.ApiKeys.AsNoTracking()
            .Where(k => k.TenantId == tenantId)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyView(k.Id, k.Name, k.DisplayPrefix, k.CreatedAt, k.RevokedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(keys);
    }

    private static async Task<IResult> CreateApiKeyAsync(
        CreateApiKeyRequest request, ITenantContext tenant, ApiKeyService service, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(tenant.TenantId, request.Name, cancellationToken);
        return result.Match<IResult>(k => TypedResults.Created($"/v1/api-keys/{k.Id}", k), error => error.ToProblem());
    }

    private static async Task<Results<NoContent, NotFound>> RevokeApiKeyAsync(
        Guid id, ITenantContext tenant, ApiKeyService service, CancellationToken cancellationToken) =>
        await service.RevokeAsync(tenant.TenantId, id, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static async Task<Results<Ok<ResolvedApiKey>, NotFound>> ResolveApiKeyAsync(
        [FromHeader(Name = ApiKeyHashHeader)] string? keyHash, ApiKeyService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(keyHash))
        {
            return TypedResults.NotFound();
        }

        var tenantId = await service.ResolveTenantAsync(keyHash, cancellationToken);
        return tenantId is { } id ? TypedResults.Ok(new ResolvedApiKey(id)) : TypedResults.NotFound();
    }
}
