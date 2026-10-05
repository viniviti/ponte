using Ponte.BuildingBlocks.Routing;
using Ponte.BuildingBlocks.Security;
using Ponte.Management.Api.Domain;
using Ponte.Management.Api.Infrastructure;

namespace Ponte.Management.Api.Application;

public sealed record EndpointInput(
    string? Url,
    string? Description,
    IReadOnlyList<string>? EventTypes,
    int? MaxConcurrency,
    bool? Active);

/// <summary>
/// Casos de uso de endpoints. Toda escrita grava a entidade E o evento de integracao
/// no mesmo SaveChanges (Outbox), entao a replica do Delivery nunca fica para tras
/// por uma publicacao que falhou.
/// </summary>
public sealed class EndpointService(
    ManagementDbContext dbContext,
    IOutbox outbox,
    TimeProvider clock,
    IOptions<ManagementOptions> options)
{
    private const int DefaultMaxConcurrency = 10;
    private const int MaxConcurrencyLimit = 100;

    public async Task<Result<WebhookEndpoint>> CreateAsync(Guid tenantId, EndpointInput input, CancellationToken cancellationToken)
    {
        var count = await dbContext.Endpoints.CountAsync(e => e.TenantId == tenantId && !e.Deleted, cancellationToken);
        if (count >= options.Value.MaxEndpointsPerTenant)
        {
            return new Error("endpoint_limit_reached", $"Limite de {options.Value.MaxEndpointsPerTenant} endpoints por tenant atingido.");
        }

        var validation = Validate(input, out var url, out var eventTypes, out var maxConcurrency);
        if (validation is not null)
        {
            return validation;
        }

        var endpoint = WebhookEndpoint.Create(tenantId, url!, input.Description?.Trim(), eventTypes, maxConcurrency, clock.GetUtcNow());
        dbContext.Endpoints.Add(endpoint);
        outbox.Enqueue(endpoint.ToUpsertedEvent());
        await dbContext.SaveChangesAsync(cancellationToken);
        return endpoint;
    }

    public async Task<Result<WebhookEndpoint>> UpdateAsync(Guid tenantId, Guid id, EndpointInput input, CancellationToken cancellationToken)
    {
        var endpoint = await FindAsync(tenantId, id, cancellationToken);
        if (endpoint is null)
        {
            return NotFound(id);
        }

        var validation = Validate(input, out var url, out var eventTypes, out var maxConcurrency);
        if (validation is not null)
        {
            return validation;
        }

        endpoint.Update(url!, input.Description?.Trim(), eventTypes, maxConcurrency, input.Active ?? endpoint.Active, clock.GetUtcNow());
        return await SaveAsync(endpoint, endpoint.ToUpsertedEvent(), cancellationToken);
    }

    public async Task<Result<WebhookEndpoint>> RotateSecretAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var endpoint = await FindAsync(tenantId, id, cancellationToken);
        if (endpoint is null)
        {
            return NotFound(id);
        }

        endpoint.RotateSecret(clock.GetUtcNow());
        return await SaveAsync(endpoint, endpoint.ToUpsertedEvent(), cancellationToken);
    }

    public async Task<Result<WebhookEndpoint>> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var endpoint = await FindAsync(tenantId, id, cancellationToken);
        if (endpoint is null)
        {
            return NotFound(id);
        }

        endpoint.Delete(clock.GetUtcNow());
        return await SaveAsync(endpoint, endpoint.ToDeletedEvent(), cancellationToken);
    }

    private async Task<Result<WebhookEndpoint>> SaveAsync(WebhookEndpoint endpoint, IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        outbox.Enqueue(integrationEvent);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return endpoint;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("endpoint_modified", "O endpoint foi alterado por outra pessoa. Recarregue e tente de novo.");
        }
    }

    private Task<WebhookEndpoint?> FindAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        dbContext.Endpoints.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId && !e.Deleted, cancellationToken);

    private static Error NotFound(Guid id) => Error.NotFound("endpoint_not_found", $"Endpoint {id} nao encontrado.");

    private Error? Validate(EndpointInput input, out Uri? url, out string[] eventTypes, out int maxConcurrency)
    {
        eventTypes = input.EventTypes?.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct().ToArray() ?? [];
        maxConcurrency = input.MaxConcurrency ?? DefaultMaxConcurrency;
        url = null;

        if (!NetworkGuard.TryValidateEndpointUrl(input.Url ?? string.Empty, options.Value.AllowInsecureEndpointUrls, out url, out var urlError))
        {
            return new Error("url_invalid", urlError!);
        }

        if (eventTypes.Length == 0)
        {
            return new Error("event_types_required", "Inscreva o endpoint em ao menos um tipo de evento (ex.: order.* ou #).");
        }

        if (eventTypes.Length > EventTypePattern.MaxPatternsPerEndpoint)
        {
            return new Error("event_types_too_many", $"Maximo de {EventTypePattern.MaxPatternsPerEndpoint} padroes por endpoint.");
        }

        var invalid = eventTypes.FirstOrDefault(t => !EventTypePattern.IsValid(t));
        if (invalid is not null)
        {
            return new Error("event_type_pattern_invalid", $"Padrao invalido: '{invalid}'. Use segmentos [a-z0-9_-], '*' ou '#'.");
        }

        if (maxConcurrency is < 1 or > MaxConcurrencyLimit)
        {
            return new Error("max_concurrency_invalid", $"maxConcurrency deve estar entre 1 e {MaxConcurrencyLimit}.");
        }

        return null;
    }
}
