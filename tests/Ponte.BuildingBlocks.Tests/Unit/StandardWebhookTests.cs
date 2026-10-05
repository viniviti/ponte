using Ponte.BuildingBlocks.Security;

namespace Ponte.BuildingBlocks.Tests.Unit;

public sealed class StandardWebhookTests
{
    // Vetor oficial de exemplo da especificacao Standard Webhooks.
    private const string SpecSecret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
    private const string SpecMessageId = "msg_p5jXN8AQM9LWM0D4loKWxJek";
    private const long SpecTimestamp = 1614265330;
    private const string SpecPayload = "{\"test\": 2432232314}";
    private const string SpecSignature = "v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=";

    private static readonly DateTimeOffset SpecNow = DateTimeOffset.FromUnixTimeSeconds(SpecTimestamp);

    [Fact]
    public void Sign_produz_a_assinatura_do_vetor_oficial_da_spec()
    {
        StandardWebhook.Sign(SpecSecret, SpecMessageId, SpecTimestamp, SpecPayload).Should().Be(SpecSignature);
    }

    [Fact]
    public void Verify_aceita_assinatura_valida_dentro_da_tolerancia()
    {
        StandardWebhook.Verify(SpecSecret, SpecMessageId, SpecTimestamp, SpecPayload, SpecSignature, SpecNow.AddMinutes(4))
            .Should().BeTrue();
    }

    [Fact]
    public void Verify_rejeita_payload_adulterado()
    {
        StandardWebhook.Verify(SpecSecret, SpecMessageId, SpecTimestamp, "{\"test\": 1}", SpecSignature, SpecNow)
            .Should().BeFalse();
    }

    [Fact]
    public void Verify_rejeita_timestamp_antigo_para_impedir_replay()
    {
        StandardWebhook.Verify(SpecSecret, SpecMessageId, SpecTimestamp, SpecPayload, SpecSignature, SpecNow.AddMinutes(6))
            .Should().BeFalse();
    }

    [Fact]
    public void Verify_aceita_qualquer_uma_das_assinaturas_durante_rotacao_de_segredo()
    {
        var oldSecret = StandardWebhook.GenerateSecret();
        var header = $"{StandardWebhook.Sign(oldSecret, SpecMessageId, SpecTimestamp, SpecPayload)} {SpecSignature}";

        StandardWebhook.Verify(SpecSecret, SpecMessageId, SpecTimestamp, SpecPayload, header, SpecNow).Should().BeTrue();
        StandardWebhook.Verify(oldSecret, SpecMessageId, SpecTimestamp, SpecPayload, header, SpecNow).Should().BeTrue();
    }

    [Fact]
    public void GenerateSecret_gera_256_bits_com_prefixo_whsec()
    {
        var secret = StandardWebhook.GenerateSecret();

        secret.Should().StartWith("whsec_");
        Convert.FromBase64String(secret["whsec_".Length..]).Should().HaveCount(32);
    }
}
