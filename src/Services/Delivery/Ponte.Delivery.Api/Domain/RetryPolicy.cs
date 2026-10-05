namespace Ponte.Delivery.Api.Domain;

public readonly record struct RetryDecision(bool ShouldRetry, TimeSpan Delay)
{
    public static readonly RetryDecision GiveUp = new(false, TimeSpan.Zero);
}

/// <summary>Strategy: decide se e quando tentar de novo. Trocavel sem tocar no agregado.</summary>
public interface IRetryPolicy
{
    RetryDecision Decide(int attemptNumber, WebhookResult result);
}

/// <summary>
/// Backoff exponencial em degraus (5s, 30s, 2m, 10m, 1h, 6h): ~7h30 de cobertura para
/// o cliente se recuperar de um incidente sem perder nenhum evento.
/// </summary>
public sealed class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private readonly IReadOnlyList<TimeSpan> _tiers;

    public ExponentialBackoffRetryPolicy(IReadOnlyList<TimeSpan> tiers)
    {
        if (tiers.Count == 0)
        {
            throw new ArgumentException("Informe ao menos um degrau de retry.", nameof(tiers));
        }

        _tiers = tiers;
    }

    /// <summary>Primeira tentativa + uma por degrau.</summary>
    public int MaxAttempts => _tiers.Count + 1;

    public RetryDecision Decide(int attemptNumber, WebhookResult result)
    {
        if (result.IsSuccess || !IsRetryable(result) || attemptNumber >= MaxAttempts)
        {
            return RetryDecision.GiveUp;
        }

        var delay = _tiers[Math.Min(attemptNumber - 1, _tiers.Count - 1)];

        // Respeita Retry-After (429/503) arredondando para o degrau que cobre o pedido.
        if (result.RetryAfter is { } retryAfter && retryAfter > delay)
        {
            delay = Ceiling(retryAfter);
        }

        return new RetryDecision(true, delay);
    }

    /// <summary>Menor degrau maior ou igual ao minimo pedido (ou o maior degrau).</summary>
    public TimeSpan Ceiling(TimeSpan minimum) => _tiers.FirstOrDefault(t => t >= minimum, _tiers[^1]);

    /// <summary>
    /// 4xx significa "o problema esta na requisicao": repetir nao ajuda. Excecoes classicas:
    /// 408 (timeout), 425 (too early) e 429 (rate limit). Sem status = falha de rede.
    /// </summary>
    private static bool IsRetryable(WebhookResult result) => result.StatusCode switch
    {
        null => true,
        408 or 425 or 429 => true,
        >= 500 => true,
        >= 400 => false,
        _ => true,
    };
}
