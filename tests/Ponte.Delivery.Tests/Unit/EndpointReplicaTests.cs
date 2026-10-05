using Ponte.Contracts;
using Ponte.Delivery.Api.Domain;

namespace Ponte.Delivery.Tests.Unit;

public sealed class EndpointReplicaTests
{
    private static readonly Guid EndpointId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Mensagem_com_versao_antiga_e_ignorada()
    {
        var replica = EndpointReplica.From(Upserted(version: 3, url: "https://v3.example.com"), TestData.Now);

        replica.Apply(Upserted(version: 2, url: "https://v2.example.com"), TestData.Now).Should().BeFalse();
        replica.Url.Should().Be("https://v3.example.com");

        replica.Apply(Upserted(version: 4, url: "https://v4.example.com"), TestData.Now).Should().BeTrue();
        replica.Url.Should().Be("https://v4.example.com");
    }

    [Fact]
    public void Tombstone_impede_que_upsert_atrasado_ressuscite_endpoint_removido()
    {
        var replica = EndpointReplica.From(Upserted(version: 1), TestData.Now);

        replica.MarkDeleted(version: 3, TestData.Now).Should().BeTrue();
        replica.Apply(Upserted(version: 2), TestData.Now).Should().BeFalse();

        replica.Deleted.Should().BeTrue();
        replica.Active.Should().BeFalse();
    }

    [Fact]
    public void Segredo_anterior_assina_junto_ate_expirar()
    {
        var expires = TestData.Now.AddHours(24);
        var replica = EndpointReplica.From(
            Upserted(version: 2) with { PreviousSecret = "whsec_old", PreviousSecretExpiresAt = expires },
            TestData.Now);

        replica.SigningSecrets(TestData.Now).Should().Equal("whsec_new", "whsec_old");
        replica.SigningSecrets(expires.AddSeconds(1)).Should().Equal("whsec_new");
    }

    private static EndpointUpserted Upserted(long version, string url = "https://example.com/hook") =>
        new(EndpointId, TenantId, url, "whsec_new", ["order.*"], Active: true, MaxConcurrency: 5, version);
}
