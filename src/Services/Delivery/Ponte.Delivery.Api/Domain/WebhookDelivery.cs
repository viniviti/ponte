namespace Ponte.Delivery.Api.Domain;

public enum ClaimResult
{
    Claimed,
    AlreadyCompleted,
    InFlightElsewhere,
    NotDueYet,
}

public enum DeliveryStatus
{
    Pending,
    InFlight,
    Scheduled,
    Succeeded,
    Dead,
    Cancelled,
}

/// <summary>
/// Agregado: a entrega de UM evento para UM endpoint. Toda transicao de estado passa
/// por metodos daqui, entao nao existe "status invalido" escrito de fora.
///
///   Pending -> InFlight -> Succeeded
///                       -> Scheduled -> InFlight -> ...
///                       -> Dead (desistiu) -> Pending (replay manual)
/// </summary>
public sealed class WebhookDelivery
{
    private readonly List<DeliveryAttempt> _attempts = [];

    private WebhookDelivery()
    {
    }

    public Guid Id { get; private set; }

    public long Sequence { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid EventId { get; private set; }

    public Guid EndpointId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    /// <summary>Corpo exato que e assinado e enviado (envelope Standard Webhooks).</summary>
    public string Body { get; private set; } = string.Empty;

    public DeliveryStatus Status { get; private set; }

    /// <summary>Total de tentativas desde sempre (numera o historico).</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Tentativas do ciclo atual: zera no replay, alimenta a politica de retry.</summary>
    public int CycleAttempts { get; private set; }

    public int? LastStatusCode { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>Ate quando a instancia que "pegou" a entrega tem exclusividade.</summary>
    public DateTimeOffset? LeaseUntil { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Token de concorrencia otimista (xmin no PostgreSQL).</summary>
    public uint Version { get; private set; }

    public IReadOnlyCollection<DeliveryAttempt> Attempts => _attempts;

    public bool IsTerminal => Status is DeliveryStatus.Succeeded or DeliveryStatus.Dead or DeliveryStatus.Cancelled;

    public static WebhookDelivery Create(
        Guid tenantId,
        Guid eventId,
        Guid endpointId,
        string eventType,
        string payload,
        DateTimeOffset occurredAt,
        DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EventId = eventId,
        EndpointId = endpointId,
        EventType = eventType,
        Body = BuildEnvelope(eventType, occurredAt, payload),
        Status = DeliveryStatus.Pending,
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>Tolerancia para diferenca de relogio entre instancias.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Reivindica a entrega para esta instancia. Combinado com o token de concorrencia,
    /// garante que duas copias do mesmo job (at-least-once) nao disparem dois POSTs.
    /// </summary>
    public ClaimResult TryClaim(DateTimeOffset now, TimeSpan leaseDuration)
    {
        if (IsTerminal)
        {
            return ClaimResult.AlreadyCompleted;
        }

        if (Status == DeliveryStatus.InFlight && LeaseUntil > now)
        {
            return ClaimResult.InFlightElsewhere;
        }

        // O job legitimo chega pela fila de TTL depois de NextAttemptAt. Um job que chega
        // bem antes e uma copia antiga: ignoramos para nao entregar fora de hora.
        if (Status == DeliveryStatus.Scheduled && NextAttemptAt > now + ClockSkew)
        {
            return ClaimResult.NotDueYet;
        }

        Status = DeliveryStatus.InFlight;
        LeaseUntil = now + leaseDuration;
        UpdatedAt = now;
        return ClaimResult.Claimed;
    }

    public DeliveryAttempt RecordAttempt(WebhookResult result, IRetryPolicy retryPolicy, DateTimeOffset now)
    {
        if (Status != DeliveryStatus.InFlight)
        {
            throw new InvalidOperationException($"Tentativa registrada em entrega {Status}. Reivindique antes (TryClaim).");
        }

        AttemptCount++;
        CycleAttempts++;
        LastStatusCode = result.StatusCode;
        LeaseUntil = null;
        UpdatedAt = now;

        var decision = retryPolicy.Decide(CycleAttempts, result);
        if (result.IsSuccess)
        {
            Status = DeliveryStatus.Succeeded;
            NextAttemptAt = null;
            CompletedAt = now;
        }
        else if (decision.ShouldRetry)
        {
            Status = DeliveryStatus.Scheduled;
            NextAttemptAt = now + decision.Delay;
        }
        else
        {
            Status = DeliveryStatus.Dead;
            NextAttemptAt = null;
            CompletedAt = now;
        }

        var attempt = DeliveryAttempt.Create(Id, AttemptCount, result, now);
        _attempts.Add(attempt);
        return attempt;
    }

    /// <summary>Adia sem consumir tentativa (circuito aberto, bulkhead cheio, lease ativo).</summary>
    public void Defer(DateTimeOffset until, DateTimeOffset now)
    {
        if (IsTerminal)
        {
            return;
        }

        Status = DeliveryStatus.Scheduled;
        NextAttemptAt = until;
        LeaseUntil = null;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (IsTerminal)
        {
            return;
        }

        Status = DeliveryStatus.Cancelled;
        NextAttemptAt = null;
        LeaseUntil = null;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public Error? Replay(DateTimeOffset now)
    {
        if (Status is not (DeliveryStatus.Dead or DeliveryStatus.Cancelled))
        {
            return Error.Conflict("delivery_not_replayable", $"Somente entregas mortas ou canceladas podem ser reenviadas (status atual: {Status}).");
        }

        Status = DeliveryStatus.Pending;
        CycleAttempts = 0;
        NextAttemptAt = null;
        CompletedAt = null;
        UpdatedAt = now;
        return null;
    }

    public string OutcomeName => Status switch
    {
        DeliveryStatus.Succeeded => DeliveryOutcomes.Succeeded,
        DeliveryStatus.Dead => DeliveryOutcomes.Dead,
        _ => DeliveryOutcomes.Retrying,
    };

    /// <summary>Envelope recomendado pela spec Standard Webhooks: type, timestamp e data.</summary>
    internal static string BuildEnvelope(string eventType, DateTimeOffset occurredAt, string payload) =>
        $$"""{"type":{{JsonSerializer.Serialize(eventType)}},"timestamp":"{{occurredAt.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffZ}}","data":{{payload}}}""";
}

public sealed class DeliveryAttempt
{
    private DeliveryAttempt()
    {
    }

    public Guid Id { get; private set; }

    public Guid DeliveryId { get; private set; }

    public int AttemptNumber { get; private set; }

    public int? StatusCode { get; private set; }

    public bool Succeeded { get; private set; }

    public long DurationMs { get; private set; }

    public string? Error { get; private set; }

    public string? ResponseSnippet { get; private set; }

    public DateTimeOffset AttemptedAt { get; private set; }

    internal static DeliveryAttempt Create(Guid deliveryId, int attemptNumber, WebhookResult result, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        DeliveryId = deliveryId,
        AttemptNumber = attemptNumber,
        StatusCode = result.StatusCode,
        Succeeded = result.IsSuccess,
        DurationMs = (long)result.Duration.TotalMilliseconds,
        Error = result.Error is { Length: > 500 } e ? e[..500] : result.Error,
        ResponseSnippet = result.ResponseSnippet,
        AttemptedAt = now,
    };
}
