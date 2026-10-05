using Ponte.Delivery.Api.Domain;

namespace Ponte.Delivery.Tests.Unit;

public sealed class RetryPolicyTests
{
    private readonly ExponentialBackoffRetryPolicy _policy = new(Ponte.Contracts.Topology.RetryTiers);

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 30)]
    [InlineData(3, 120)]
    [InlineData(4, 600)]
    [InlineData(5, 3600)]
    [InlineData(6, 21600)]
    public void Atraso_cresce_exponencialmente_por_degrau(int attempt, int expectedSeconds)
    {
        var decision = _policy.Decide(attempt, TestData.Status(500));

        decision.ShouldRetry.Should().BeTrue();
        decision.Delay.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void Desiste_depois_da_ultima_tentativa()
    {
        _policy.MaxAttempts.Should().Be(7);
        _policy.Decide(7, TestData.Status(500)).Should().Be(RetryDecision.GiveUp);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(410)]
    [InlineData(422)]
    public void Erros_4xx_nao_sao_repetidos(int status)
    {
        _policy.Decide(1, TestData.Status(status)).ShouldRetry.Should().BeFalse();
    }

    [Theory]
    [InlineData(408)]
    [InlineData(425)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public void Erros_transitorios_sao_repetidos(int status)
    {
        _policy.Decide(1, TestData.Status(status)).ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public void Falha_de_rede_e_repetida()
    {
        _policy.Decide(1, TestData.NetworkError()).ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public void Retry_After_maior_que_o_degrau_e_respeitado_arredondando_para_cima()
    {
        var decision = _policy.Decide(1, TestData.Status(429, retryAfter: TimeSpan.FromSeconds(90)));

        decision.Delay.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void Sucesso_nunca_gera_retry()
    {
        _policy.Decide(1, TestData.Status(200)).ShouldRetry.Should().BeFalse();
    }
}
