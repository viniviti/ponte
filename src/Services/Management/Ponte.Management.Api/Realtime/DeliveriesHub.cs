using Microsoft.AspNetCore.SignalR;

namespace Ponte.Management.Api.Realtime;

public sealed record LiveDeliveryAttempt(
    Guid DeliveryId,
    Guid EventId,
    Guid EndpointId,
    string EventType,
    int AttemptNumber,
    string Outcome,
    int? StatusCode,
    long DurationMs,
    DateTimeOffset AttemptedAt,
    DateTimeOffset? NextAttemptAt);

/// <summary>Contrato tipado do cliente: erro de digitacao no nome do metodo vira erro de compilacao.</summary>
public interface IDeliveriesClient
{
    Task DeliveryAttempted(LiveDeliveryAttempt attempt);
}

/// <summary>
/// Hub de tempo real do console. Cada conexao entra no grupo do seu tenant, entao um
/// tenant nunca recebe eventos de outro.
/// </summary>
public sealed class DeliveriesHub : Hub<IDeliveriesClient>
{
    public const string Route = "/hubs/deliveries";

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        if (httpContext is null || !TenantHeader.TryRead(httpContext, out var tenantId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tenantId));
        await base.OnConnectedAsync();
    }

    public static string GroupName(Guid tenantId) => $"tenant:{tenantId:N}";
}

/// <summary>
/// Consumidor pub/sub (fila exclusiva por instancia). Se houver 3 replicas do
/// Management, as 3 recebem o evento e cada uma avisa os navegadores conectados nela.
/// </summary>
public sealed class RealtimeHandler(IHubContext<DeliveriesHub, IDeliveriesClient> hub) : IMessageHandler<DeliveryAttempted>
{
    public Task HandleAsync(DeliveryAttempted message, MessageContext context, CancellationToken cancellationToken) =>
        hub.Clients.Group(DeliveriesHub.GroupName(message.TenantId)).DeliveryAttempted(new LiveDeliveryAttempt(
            message.DeliveryId,
            message.EventId,
            message.EndpointId,
            message.EventType,
            message.AttemptNumber,
            message.Outcome,
            message.StatusCode,
            message.DurationMs,
            message.AttemptedAt,
            message.NextAttemptAt));
}
