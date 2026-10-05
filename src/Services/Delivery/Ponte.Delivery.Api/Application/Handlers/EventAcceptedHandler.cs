using Ponte.BuildingBlocks.Routing;
using Ponte.Delivery.Api.Domain;
using Ponte.Delivery.Api.Infrastructure;

namespace Ponte.Delivery.Api.Application.Handlers;

/// <summary>
/// Fan-out: um evento vira N entregas, uma por endpoint inscrito. Entregas e jobs sao
/// gravados juntos (Outbox) e a mensagem so e marcada como processada (Inbox) no
/// mesmo commit, entao reprocessar o evento nunca duplica entregas.
/// </summary>
public sealed class EventAcceptedHandler(
    DeliveryDbContext dbContext,
    EndpointDirectory directory,
    IInbox inbox,
    IOutbox outbox,
    TimeProvider clock,
    DeliveryMetrics metrics,
    ILogger<EventAcceptedHandler> logger) : IMessageHandler<EventAccepted>
{
    private const string ConsumerName = "delivery.fanout";

    public async Task HandleAsync(EventAccepted message, MessageContext context, CancellationToken cancellationToken)
    {
        if (!await inbox.TryBeginAsync(message.Id, ConsumerName, cancellationToken))
        {
            logger.LogDebug("Evento {EventId} ja distribuido, ignorando duplicata", message.EventId);
            return;
        }

        var subscriptions = await directory.GetActiveAsync(message.TenantId, cancellationToken);
        var now = clock.GetUtcNow();
        var created = 0;

        foreach (var subscription in subscriptions)
        {
            if (!EventTypePattern.MatchesAny(subscription.EventTypes, message.EventType))
            {
                continue;
            }

            var delivery = WebhookDelivery.Create(
                message.TenantId, message.EventId, subscription.Id, message.EventType, message.Payload, message.ReceivedAt, now);

            dbContext.Deliveries.Add(delivery);
            outbox.Enqueue(new DeliveryJob(delivery.Id), Topology.DeliveryExchange, Topology.DeliveryJobRoutingKey);
            created++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        metrics.FannedOut(created);

        logger.LogInformation("Evento {EventId} ({EventType}) distribuido para {Count} endpoint(s)",
            message.EventId, message.EventType, created);
    }
}
