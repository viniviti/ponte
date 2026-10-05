namespace Ponte.Delivery.Api.Domain;

/// <summary>Resultado de UMA tentativa HTTP contra o endpoint do cliente.</summary>
public sealed record WebhookResult(
    int? StatusCode,
    TimeSpan Duration,
    string? Error,
    string? ResponseSnippet,
    TimeSpan? RetryAfter)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    /// <summary>
    /// So falhas de infraestrutura contam para o circuit breaker. Um 400 significa que o
    /// endpoint esta de pe e respondendo; derrubar o circuito por isso seria errado.
    /// </summary>
    public bool IndicatesUnhealthyEndpoint => StatusCode is null or 429 or >= 500;

    public static WebhookResult Success(int statusCode, TimeSpan duration) => new(statusCode, duration, null, null, null);

    public static WebhookResult Failure(string error, TimeSpan duration) => new(null, duration, error, null, null);
}
