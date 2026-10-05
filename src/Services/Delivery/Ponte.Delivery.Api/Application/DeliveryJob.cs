namespace Ponte.Delivery.Api.Application;

/// <summary>
/// Mensagem interna do servico: "tente entregar esta delivery agora". Carrega so o Id;
/// o estado verdadeiro esta no banco, entao jobs duplicados ou atrasados sao inofensivos.
/// </summary>
public sealed record DeliveryJob(Guid DeliveryId);
