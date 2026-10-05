using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Ponte.BuildingBlocks.Security;
using Ponte.BuildingBlocks.Tenancy;

namespace Ponte.Gateway;

public static class GatewayItems
{
    public const string TenantId = "ponte.tenant_id";
}

/// <summary>
/// Traduz API key em tenant consultando o Management, com cache em memoria.
/// Efeito colateral bom: se o Management cair, chaves ja vistas continuam funcionando
/// e a ingestao de eventos nao para (o caminho critico nao depende do Management).
/// </summary>
public sealed class ApiKeyResolver(HttpClient httpClient, IMemoryCache cache)
{
    private static readonly TimeSpan PositiveTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromSeconds(10);

    public async Task<Guid?> ResolveAsync(string apiKey, CancellationToken cancellationToken)
    {
        if (!apiKey.StartsWith(ApiKeys.Prefix, StringComparison.Ordinal) || apiKey.Length > 128)
        {
            return null;
        }

        var hash = ApiKeys.Hash(apiKey);
        if (cache.TryGetValue(hash, out Guid? cached))
        {
            return cached;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/internal/api-keys/resolve");
        request.Headers.Add("X-Api-Key-Hash", hash);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        Guid? tenantId = null;
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadFromJsonAsync<ResolvedApiKey>(cancellationToken);
            tenantId = body?.TenantId;
        }
        else if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode(); // 5xx: nao cacheia, o middleware devolve 503
        }

        cache.Set(hash, tenantId, tenantId is null ? NegativeTtl : PositiveTtl);
        return tenantId;
    }

    private sealed record ResolvedApiKey(Guid TenantId);
}

/// <summary>
/// Gateway offloading de autenticacao: valida a API key uma vez na borda e injeta
/// X-Tenant-Id para os servicos internos. Qualquer X-Tenant-Id vindo do cliente e
/// descartado antes, entao nao da para se passar por outro tenant.
/// </summary>
public sealed class ApiKeyAuthenticationMiddleware(RequestDelegate next, ILogger<ApiKeyAuthenticationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ApiKeyResolver resolver)
    {
        context.Request.Headers.Remove(TenantHeader.Name);

        var path = context.Request.Path;
        if (path.StartsWithSegments("/health") || HttpMethods.IsOptions(context.Request.Method))
        {
            await next(context);
            return;
        }

        var apiKey = context.Request.Headers[ApiKeys.Header].ToString();

        // SignalR: o negotiate (HTTP) envia "Authorization: Bearer <token>" e o handshake
        // de WebSocket manda "?access_token=" (navegadores nao enviam headers customizados ali).
        if (string.IsNullOrEmpty(apiKey) && path.StartsWithSegments("/hubs"))
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            apiKey = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? authorization["Bearer ".Length..].Trim()
                : context.Request.Query["access_token"].ToString();
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized, "api_key_required", $"Envie a chave no header {ApiKeys.Header}.");
            return;
        }

        Guid? tenantId;
        try
        {
            tenantId = await resolver.ResolveAsync(apiKey, context.RequestAborted);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !context.RequestAborted.IsCancellationRequested)
        {
            logger.LogError(ex, "Management indisponivel para validar API key");
            await RejectAsync(context, StatusCodes.Status503ServiceUnavailable, "auth_unavailable", "Nao foi possivel validar a chave agora. Tente novamente.");
            return;
        }

        if (tenantId is null)
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized, "api_key_invalid", "API key invalida ou revogada.");
            return;
        }

        context.Request.Headers.Remove(ApiKeys.Header);
        context.Request.Headers[TenantHeader.Name] = tenantId.Value.ToString();
        context.Items[GatewayItems.TenantId] = tenantId.Value.ToString();

        await next(context);
    }

    private static Task RejectAsync(HttpContext context, int status, string code, string detail) =>
        Results.Problem(statusCode: status, title: code, detail: detail).ExecuteAsync(context);
}
