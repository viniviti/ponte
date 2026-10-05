using Ponte.Delivery.Api.Domain;
using Ponte.Delivery.Api.Infrastructure;

namespace Ponte.Delivery.Api.Application.Handlers;

/// <summary>
/// Mantem a replica local de endpoints. Idempotente por natureza: o campo Version
/// descarta mensagens repetidas ou fora de ordem, e o token de concorrencia (xmin)
/// resolve duas atualizacoes simultaneas.
/// </summary>
public sealed class EndpointUpsertedHandler(
    DeliveryDbContext dbContext,
    EndpointDirectory directory,
    TimeProvider clock,
    ILogger<EndpointUpsertedHandler> logger) : IMessageHandler<EndpointUpserted>
{
    public async Task HandleAsync(EndpointUpserted message, MessageContext context, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var replica = await dbContext.Endpoints.FirstOrDefaultAsync(e => e.Id == message.EndpointId, cancellationToken);

        if (replica is null)
        {
            dbContext.Endpoints.Add(EndpointReplica.From(message, now));
        }
        else if (!replica.Apply(message, now))
        {
            logger.LogDebug("Versao {Version} do endpoint {EndpointId} e obsoleta", message.Version, message.EndpointId);
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        directory.Invalidate(message.TenantId);
    }
}

public sealed class EndpointDeletedHandler(
    DeliveryDbContext dbContext,
    EndpointDirectory directory,
    TimeProvider clock) : IMessageHandler<EndpointDeleted>
{
    public async Task HandleAsync(EndpointDeleted message, MessageContext context, CancellationToken cancellationToken)
    {
        var replica = await dbContext.Endpoints.FirstOrDefaultAsync(e => e.Id == message.EndpointId, cancellationToken);
        if (replica is null || !replica.MarkDeleted(message.Version, clock.GetUtcNow()))
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        directory.Invalidate(message.TenantId);
    }
}
