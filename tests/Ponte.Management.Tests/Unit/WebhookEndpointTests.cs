using Ponte.Management.Api.Domain;

namespace Ponte.Management.Tests.Unit;

public sealed class WebhookEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_gera_segredo_whsec_e_comeca_na_versao_1()
    {
        var endpoint = NewEndpoint();

        endpoint.Secret.Should().StartWith("whsec_");
        endpoint.Version.Should().Be(1);
        endpoint.Active.Should().BeTrue();
    }

    [Fact]
    public void Create_normaliza_os_padroes_de_evento()
    {
        var endpoint = WebhookEndpoint.Create(Guid.NewGuid(), new Uri("https://x.com"), null, [" Order.* ", "order.*", "invoice.#"], 5, Now);

        endpoint.EventTypes.Should().Equal("invoice.#", "order.*");
    }

    [Fact]
    public void Cada_mudanca_incrementa_a_versao()
    {
        var endpoint = NewEndpoint();

        endpoint.Update(new Uri("https://novo.example.com"), "novo", ["order.paid"], 3, active: false, Now);
        endpoint.RotateSecret(Now);
        endpoint.Delete(Now);

        endpoint.Version.Should().Be(4);
        endpoint.ToDeletedEvent().Version.Should().Be(4);
    }

    [Fact]
    public void Rotacao_mantem_o_segredo_anterior_valido_por_24h()
    {
        var endpoint = NewEndpoint();
        var original = endpoint.Secret;

        endpoint.RotateSecret(Now);

        endpoint.Secret.Should().NotBe(original);
        endpoint.PreviousSecret.Should().Be(original);
        endpoint.PreviousSecretExpiresAt.Should().Be(Now.AddHours(24));

        var message = endpoint.ToUpsertedEvent();
        message.PreviousSecret.Should().Be(original);
        message.Version.Should().Be(2);
    }

    private static WebhookEndpoint NewEndpoint() =>
        WebhookEndpoint.Create(Guid.NewGuid(), new Uri("https://cliente.example.com/hook"), "ERP", ["order.*"], 10, Now);
}
