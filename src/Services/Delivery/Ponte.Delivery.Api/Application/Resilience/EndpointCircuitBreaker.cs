using System.Collections.Concurrent;

namespace Ponte.Delivery.Api.Application.Resilience;

public sealed class CircuitBreakerOptions
{
    public const string SectionName = "CircuitBreaker";

    /// <summary>Falhas consecutivas que abrem o circuito.</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>Quanto tempo o circuito fica aberto antes de testar de novo (half-open).</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}

public enum CircuitState
{
    Closed,
    Open,
    HalfOpen,
}

public readonly record struct CircuitPermit(bool Allowed, TimeSpan RetryAfter)
{
    public static readonly CircuitPermit Granted = new(true, TimeSpan.Zero);
}

public interface ICircuitBreakerRegistry
{
    CircuitPermit TryAcquire(Guid endpointId);

    void Record(Guid endpointId, bool healthy);

    CircuitState GetState(Guid endpointId);
}

/// <summary>
/// Um circuit breaker POR ENDPOINT. Se o servidor de um cliente cai, paramos de
/// martela-lo (e de gastar threads e conexoes com timeouts) sem afetar os demais tenants.
/// Implementado com o State pattern: cada estado decide o que fazer com requisicoes,
/// sucessos e falhas, e as transicoes ficam explicitas.
/// </summary>
public sealed class CircuitBreakerRegistry(
    IOptions<CircuitBreakerOptions> options,
    TimeProvider clock,
    ILogger<CircuitBreakerRegistry> logger) : ICircuitBreakerRegistry
{
    private readonly ConcurrentDictionary<Guid, Circuit> _circuits = new();

    public CircuitPermit TryAcquire(Guid endpointId) => GetCircuit(endpointId).TryAcquire();

    public void Record(Guid endpointId, bool healthy)
    {
        var circuit = GetCircuit(endpointId);
        if (healthy)
        {
            circuit.OnSuccess();
        }
        else
        {
            circuit.OnFailure();
        }
    }

    public CircuitState GetState(Guid endpointId) =>
        _circuits.TryGetValue(endpointId, out var circuit) ? circuit.State : CircuitState.Closed;

    private Circuit GetCircuit(Guid endpointId) =>
        _circuits.GetOrAdd(endpointId, id => new Circuit(id, options.Value, clock, logger));

    // -----------------------------------------------------------------------------
    // State pattern
    // -----------------------------------------------------------------------------

    private sealed class Circuit(Guid endpointId, CircuitBreakerOptions options, TimeProvider clock, ILogger logger)
    {
        private readonly object _sync = new();
        private CircuitStateBase _state = ClosedState.Instance;

        public Guid EndpointId { get; } = endpointId;

        public CircuitBreakerOptions Options { get; } = options;

        public TimeProvider Clock { get; } = clock;

        public int ConsecutiveFailures { get; set; }

        public CircuitState State
        {
            get
            {
                lock (_sync)
                {
                    return _state.Kind;
                }
            }
        }

        public CircuitPermit TryAcquire()
        {
            lock (_sync)
            {
                return _state.TryAcquire(this);
            }
        }

        public void OnSuccess()
        {
            lock (_sync)
            {
                _state.OnSuccess(this);
            }
        }

        public void OnFailure()
        {
            lock (_sync)
            {
                _state.OnFailure(this);
            }
        }

        public void TransitionTo(CircuitStateBase next)
        {
            if (next.Kind != _state.Kind)
            {
                logger.LogWarning("Circuito do endpoint {EndpointId}: {From} -> {To}", EndpointId, _state.Kind, next.Kind);
            }

            _state = next;
        }

        public void Trip() => TransitionTo(new OpenState(Clock.GetUtcNow() + Options.BreakDuration));
    }

    private abstract class CircuitStateBase
    {
        public abstract CircuitState Kind { get; }

        public abstract CircuitPermit TryAcquire(Circuit circuit);

        public abstract void OnSuccess(Circuit circuit);

        public abstract void OnFailure(Circuit circuit);
    }

    /// <summary>Fechado: tudo passa; conta falhas consecutivas.</summary>
    private sealed class ClosedState : CircuitStateBase
    {
        public static readonly ClosedState Instance = new();

        public override CircuitState Kind => CircuitState.Closed;

        public override CircuitPermit TryAcquire(Circuit circuit) => CircuitPermit.Granted;

        public override void OnSuccess(Circuit circuit) => circuit.ConsecutiveFailures = 0;

        public override void OnFailure(Circuit circuit)
        {
            circuit.ConsecutiveFailures++;
            if (circuit.ConsecutiveFailures >= circuit.Options.FailureThreshold)
            {
                circuit.Trip();
            }
        }
    }

    /// <summary>Aberto: ninguem passa ate o prazo vencer; entao vira half-open.</summary>
    private sealed class OpenState(DateTimeOffset openUntil) : CircuitStateBase
    {
        public override CircuitState Kind => CircuitState.Open;

        public override CircuitPermit TryAcquire(Circuit circuit)
        {
            var now = circuit.Clock.GetUtcNow();
            if (now < openUntil)
            {
                return new CircuitPermit(false, openUntil - now);
            }

            var halfOpen = new HalfOpenState();
            circuit.TransitionTo(halfOpen);
            return halfOpen.TryAcquire(circuit);
        }

        // Respostas de requisicoes que sairam antes de abrir: nao mudam nada.
        public override void OnSuccess(Circuit circuit)
        {
        }

        public override void OnFailure(Circuit circuit)
        {
        }
    }

    /// <summary>Half-open: deixa passar UMA requisicao de teste. Sucesso fecha, falha reabre.</summary>
    private sealed class HalfOpenState : CircuitStateBase
    {
        private bool _trialInFlight;

        public override CircuitState Kind => CircuitState.HalfOpen;

        public override CircuitPermit TryAcquire(Circuit circuit)
        {
            if (_trialInFlight)
            {
                return new CircuitPermit(false, TimeSpan.FromSeconds(5));
            }

            _trialInFlight = true;
            return CircuitPermit.Granted;
        }

        public override void OnSuccess(Circuit circuit)
        {
            circuit.ConsecutiveFailures = 0;
            circuit.TransitionTo(ClosedState.Instance);
        }

        public override void OnFailure(Circuit circuit) => circuit.Trip();
    }
}
