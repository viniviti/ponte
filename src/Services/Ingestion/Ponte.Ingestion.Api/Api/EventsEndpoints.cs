using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Ponte.Ingestion.Api.Application;
using Ponte.Ingestion.Api.Infrastructure;

namespace Ponte.Ingestion.Api.Api;

public sealed record IngestEventRequest(string? EventType, JsonElement Payload);

public sealed record IngestEventResponse(Guid Id, DateTimeOffset ReceivedAt);

public sealed record EventSummary(Guid Id, long Sequence, string EventType, DateTimeOffset ReceivedAt);

public sealed record EventDetails(Guid Id, string EventType, JsonElement Payload, string? IdempotencyKey, DateTimeOffset ReceivedAt);

public static class EventsEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    public static IEndpointRouteBuilder MapEventsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/events").WithTags("Events").RequireTenant();

        group.MapPost("/", IngestAsync)
            .WithName("IngestEvent")
            .WithSummary("Recebe um evento e agenda a entrega para todos os endpoints inscritos.");

        group.MapGet("/", ListAsync).WithName("ListEvents");
        group.MapGet("/{id:guid}", GetAsync).WithName("GetEvent");

        return app;
    }

    private static async Task<IResult> IngestAsync(
        IngestEventRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        ITenantContext tenant,
        IngestEventHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new IngestEventCommand(tenant.TenantId, request.EventType, request.Payload, idempotencyKey),
            cancellationToken);

        return result.Match<IResult>(
            ok =>
            {
                if (ok.Replayed)
                {
                    httpContext.Response.Headers[ReplayedHeader] = "true";
                }

                return TypedResults.Accepted($"/v1/events/{ok.EventId}", new IngestEventResponse(ok.EventId, ok.ReceivedAt));
            },
            error => error.ToProblem());
    }

    private static async Task<Ok<Page<EventSummary>>> ListAsync(
        ITenantContext tenant,
        IngestionDbContext dbContext,
        string? eventType,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        var take = Pagination.ClampLimit(limit);
        var after = Pagination.ParseCursor(cursor);
        var tenantId = tenant.TenantId;

        var query = dbContext.Events.AsNoTracking().Where(e => e.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(e => e.EventType == eventType);
        }

        if (after is { } sequence)
        {
            query = query.Where(e => e.Sequence < sequence);
        }

        var items = await query
            .OrderByDescending(e => e.Sequence)
            .Take(take + 1)
            .Select(e => new EventSummary(e.Id, e.Sequence, e.EventType, e.ReceivedAt))
            .ToListAsync(cancellationToken);

        var nextCursor = items.Count > take ? items[take - 1].Sequence.ToString() : null;
        return TypedResults.Ok(new Page<EventSummary>(items.Take(take).ToList(), nextCursor));
    }

    private static async Task<Results<Ok<EventDetails>, NotFound>> GetAsync(
        Guid id,
        ITenantContext tenant,
        IngestionDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        var stored = await dbContext.Events
            .AsNoTracking()
            .Where(e => e.Id == id && e.TenantId == tenantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            return TypedResults.NotFound();
        }

        var payload = JsonSerializer.Deserialize<JsonElement>(stored.Payload);
        return TypedResults.Ok(new EventDetails(stored.Id, stored.EventType, payload, stored.IdempotencyKey, stored.ReceivedAt));
    }
}
