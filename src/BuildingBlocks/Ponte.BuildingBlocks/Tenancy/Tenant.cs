using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Ponte.BuildingBlocks.Security;

namespace Ponte.BuildingBlocks.Tenancy;

/// <summary>
/// O Gateway autentica a API key e injeta X-Tenant-Id (apagando qualquer valor vindo do
/// cliente). Os servicos internos confiam nesse header porque so sao alcancaveis pela
/// rede privada (gateway offloading).
/// </summary>
public static class TenantHeader
{
    public const string Name = "X-Tenant-Id";

    public static bool TryRead(HttpContext context, out Guid tenantId) =>
        Guid.TryParse(context.Request.Headers[Name].ToString(), out tenantId) && tenantId != Guid.Empty;
}

public interface ITenantContext
{
    Guid TenantId { get; }
}

internal sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid TenantId =>
        accessor.HttpContext is { } context && TenantHeader.TryRead(context, out var tenantId)
            ? tenantId
            : throw new UnauthorizedAccessException("Requisicao sem tenant.");
}

public static class TenancyExtensions
{
    public static IServiceCollection AddTenancy(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, HttpTenantContext>();
        return services;
    }

    /// <summary>Rejeita com 401 qualquer requisicao do grupo que nao venha com tenant.</summary>
    public static RouteGroupBuilder RequireTenant(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            if (!TenantHeader.TryRead(context.HttpContext, out _))
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "tenant_required",
                    detail: $"Header {TenantHeader.Name} ausente. Chame a API pelo Gateway com {ApiKeys.Header}.");
            }

            return await next(context);
        });

        return group;
    }
}
