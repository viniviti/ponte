using System.Net;
using System.Net.Sockets;
using System.Text;
using Ponte.BuildingBlocks.Security;
using Ponte.Delivery.Api.Domain;

namespace Ponte.Delivery.Api.Infrastructure.Http;

public sealed class WebhookSenderOptions
{
    public const string SectionName = "Webhooks";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Somente desenvolvimento: permite entregar para a rede local (ex.: echo-receiver no compose).</summary>
    public bool AllowPrivateNetworks { get; set; }

    public int MaxConnectionsPerServer { get; set; } = 64;

    public int ResponseSnippetBytes { get; set; } = 1024;
}

public sealed record WebhookRequest(
    Uri Url,
    IReadOnlyList<string> Secrets,
    Guid MessageId,
    string EventType,
    string Body,
    int AttemptNumber);

/// <summary>Abstracao do envio: permite o decorator de metricas e fakes nos testes.</summary>
public interface IWebhookSender
{
    Task<WebhookResult> SendAsync(WebhookRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Faz o POST assinado. Nunca lanca excecao para falha do endpoint: timeout, DNS,
/// conexao recusada e 5xx viram <see cref="WebhookResult"/>, porque isso e regra de
/// negocio (retry), nao erro do nosso sistema.
/// </summary>
public sealed class WebhookHttpSender(HttpClient httpClient, TimeProvider clock, IOptions<WebhookSenderOptions> options)
    : IWebhookSender
{
    public const string AttemptHeader = "ponte-attempt";
    public const string EventTypeHeader = "ponte-event-type";

    public async Task<WebhookResult> SendAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var messageId = $"msg_{request.MessageId:N}";
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds();

        // Varias assinaturas separadas por espaco: durante a rotacao de segredo o cliente
        // valida com o segredo antigo OU o novo, sem janela de falha.
        var signature = string.Join(' ', request.Secrets.Select(secret => StandardWebhook.Sign(secret, messageId, timestamp, request.Body)));

        using var message = new HttpRequestMessage(HttpMethod.Post, request.Url)
        {
            Content = new StringContent(request.Body, Encoding.UTF8, "application/json"),
        };
        message.Headers.TryAddWithoutValidation(StandardWebhook.IdHeader, messageId);
        message.Headers.TryAddWithoutValidation(StandardWebhook.TimestampHeader, timestamp.ToString());
        message.Headers.TryAddWithoutValidation(StandardWebhook.SignatureHeader, signature);
        message.Headers.TryAddWithoutValidation(AttemptHeader, request.AttemptNumber.ToString());
        message.Headers.TryAddWithoutValidation(EventTypeHeader, request.EventType);

        var started = clock.GetTimestamp();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Timeout);

        try
        {
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var snippet = await ReadSnippetAsync(response, settings.ResponseSnippetBytes, timeout.Token);

            return new WebhookResult(
                (int)response.StatusCode,
                clock.GetElapsedTime(started),
                Error: null,
                snippet,
                ParseRetryAfter(response));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return WebhookResult.Failure($"Timeout apos {settings.Timeout.TotalSeconds:0}s", clock.GetElapsedTime(started));
        }
        catch (HttpRequestException ex)
        {
            return WebhookResult.Failure(ex.Message, clock.GetElapsedTime(started));
        }
    }

    private static async Task<string?> ReadSnippetAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[maxBytes];
        var read = await stream.ReadAtLeastAsync(buffer, maxBytes, throwOnEndOfStream: false, cancellationToken);
        return read == 0 ? null : Encoding.UTF8.GetString(buffer, 0, read);
    }

    private TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        return retryAfter?.Date is { } date ? date - clock.GetUtcNow() : null;
    }
}

/// <summary>Decorator: adiciona metricas sem poluir o sender com preocupacoes de observabilidade.</summary>
public sealed class InstrumentedWebhookSender(IWebhookSender inner, DeliveryMetrics metrics) : IWebhookSender
{
    public async Task<WebhookResult> SendAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        var result = await inner.SendAsync(request, cancellationToken);
        metrics.Attempt(result.IsSuccess, result.StatusCode, result.Duration.TotalMilliseconds);
        return result;
    }
}

/// <summary>
/// Handler HTTP com protecao SSRF no momento da CONEXAO: resolvemos o DNS, validamos o
/// IP e conectamos nesse mesmo IP. Validar so a URL no cadastro nao basta, porque o
/// dono do dominio pode trocar o DNS depois (DNS rebinding).
/// </summary>
public static class SsrfSafeHandlerFactory
{
    public static SocketsHttpHandler Create(WebhookSenderOptions options) => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        MaxConnectionsPerServer = options.MaxConnectionsPerServer,
        ConnectCallback = async (context, cancellationToken) =>
        {
            var host = context.DnsEndPoint.Host;
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            var target = addresses.FirstOrDefault(a => options.AllowPrivateNetworks || !NetworkGuard.IsPrivateOrReserved(a))
                         ?? throw new HttpRequestException($"Destino bloqueado: {host} resolve para uma rede privada ou reservada.");

            var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };
}
