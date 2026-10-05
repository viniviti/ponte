using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Ponte.Delivery.Api.Application.Resilience;

namespace Ponte.Delivery.Tests.Unit;

public sealed class CircuitBreakerTests
{
    private readonly FakeTimeProvider _clock = new(TestData.Now);
    private readonly CircuitBreakerRegistry _breakers;
    private readonly Guid _endpoint = Guid.NewGuid();

    public CircuitBreakerTests()
    {
        _breakers = new CircuitBreakerRegistry(
            Options.Create(new CircuitBreakerOptions { FailureThreshold = 3, BreakDuration = TimeSpan.FromSeconds(30) }),
            _clock,
            NullLogger<CircuitBreakerRegistry>.Instance);
    }

    [Fact]
    public void Abre_depois_de_N_falhas_consecutivas()
    {
        Fail(3);

        _breakers.GetState(_endpoint).Should().Be(CircuitState.Open);
        var permit = _breakers.TryAcquire(_endpoint);
        permit.Allowed.Should().BeFalse();
        permit.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Sucesso_zera_a_contagem_de_falhas()
    {
        Fail(2);
        _breakers.Record(_endpoint, healthy: true);
        Fail(2);

        _breakers.GetState(_endpoint).Should().Be(CircuitState.Closed);
    }

    [Fact]
    public void Depois_do_prazo_vira_half_open_e_libera_uma_unica_requisicao_de_teste()
    {
        Fail(3);
        _clock.Advance(TimeSpan.FromSeconds(31));

        _breakers.TryAcquire(_endpoint).Allowed.Should().BeTrue();
        _breakers.GetState(_endpoint).Should().Be(CircuitState.HalfOpen);
        _breakers.TryAcquire(_endpoint).Allowed.Should().BeFalse("so uma requisicao de teste por vez");
    }

    [Fact]
    public void Teste_bem_sucedido_no_half_open_fecha_o_circuito()
    {
        Fail(3);
        _clock.Advance(TimeSpan.FromSeconds(31));
        _breakers.TryAcquire(_endpoint);

        _breakers.Record(_endpoint, healthy: true);

        _breakers.GetState(_endpoint).Should().Be(CircuitState.Closed);
        _breakers.TryAcquire(_endpoint).Allowed.Should().BeTrue();
    }

    [Fact]
    public void Teste_com_falha_no_half_open_reabre_o_circuito()
    {
        Fail(3);
        _clock.Advance(TimeSpan.FromSeconds(31));
        _breakers.TryAcquire(_endpoint);

        _breakers.Record(_endpoint, healthy: false);

        _breakers.GetState(_endpoint).Should().Be(CircuitState.Open);
    }

    [Fact]
    public void Circuitos_sao_isolados_por_endpoint()
    {
        Fail(3);

        _breakers.TryAcquire(Guid.NewGuid()).Allowed.Should().BeTrue();
    }

    private void Fail(int times)
    {
        for (var i = 0; i < times; i++)
        {
            _breakers.TryAcquire(_endpoint);
            _breakers.Record(_endpoint, healthy: false);
        }
    }
}

public sealed class EndpointBulkheadTests
{
    [Fact]
    public void Limita_entregas_simultaneas_por_endpoint()
    {
        var bulkhead = new EndpointBulkhead();
        var endpoint = Guid.NewGuid();

        bulkhead.TryEnter(endpoint, 2, out var first).Should().BeTrue();
        bulkhead.TryEnter(endpoint, 2, out var second).Should().BeTrue();
        bulkhead.TryEnter(endpoint, 2, out _).Should().BeFalse();
        bulkhead.TryEnter(Guid.NewGuid(), 2, out var other).Should().BeTrue("outro endpoint tem compartimento proprio");

        first.Dispose();
        bulkhead.TryEnter(endpoint, 2, out var third).Should().BeTrue();

        second.Dispose();
        third.Dispose();
        other.Dispose();
    }
}
