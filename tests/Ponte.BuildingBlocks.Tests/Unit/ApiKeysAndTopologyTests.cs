using Ponte.BuildingBlocks.Hosting;
using Ponte.BuildingBlocks.Security;
using Ponte.Contracts;

namespace Ponte.BuildingBlocks.Tests.Unit;

public sealed class ApiKeysTests
{
    [Fact]
    public void Generate_cria_chave_com_prefixo_e_alta_entropia()
    {
        var key = ApiKeys.Generate();

        key.Should().StartWith("pk_live_").And.HaveLength("pk_live_".Length + 64);
        ApiKeys.Generate().Should().NotBe(key);
    }

    [Fact]
    public void Hash_e_deterministico_e_nao_revela_a_chave()
    {
        var key = ApiKeys.Generate();

        ApiKeys.Hash(key).Should().Be(ApiKeys.Hash(key)).And.HaveLength(64).And.NotContain(key);
        ApiKeys.DisplayPrefix(key).Should().HaveLength(12);
    }
}

public sealed class TopologyTests
{
    [Theory]
    [InlineData(5, "retry.5s")]
    [InlineData(30, "retry.30s")]
    [InlineData(120, "retry.2m")]
    [InlineData(600, "retry.10m")]
    [InlineData(3600, "retry.1h")]
    [InlineData(21600, "retry.6h")]
    public void RetryRoutingKey_usa_nome_legivel_por_degrau(int seconds, string expected)
    {
        Topology.RetryRoutingKey(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }

    [Fact]
    public void RoutingKeyFor_le_o_atributo_do_contrato()
    {
        Topology.RoutingKeyFor<EventAccepted>().Should().Be("ingestion.event.accepted");
        Topology.RoutingKeyFor<DeliveryAttempted>().Should().Be("delivery.attempted");
    }

    [Fact]
    public void RoutingKeyFor_falha_para_tipo_sem_rota()
    {
        var act = () => Topology.RoutingKeyFor<string>();
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("abc", null)]
    [InlineData("-5", null)]
    [InlineData("42", 42L)]
    public void Pagination_ParseCursor_aceita_apenas_sequencias_positivas(string? cursor, long? expected)
    {
        Pagination.ParseCursor(cursor).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, 50)]
    [InlineData(0, 1)]
    [InlineData(10, 10)]
    [InlineData(5000, 200)]
    public void Pagination_ClampLimit_respeita_limites(int? limit, int expected)
    {
        Pagination.ClampLimit(limit).Should().Be(expected);
    }
}
