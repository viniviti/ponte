using System.Text.Json;
using Ponte.Ingestion.Api.Application;

namespace Ponte.Ingestion.Tests.Unit;

public sealed class EventValidatorTests
{
    private static readonly JsonElement ObjectPayload = JsonDocument.Parse("""{"id":1}""").RootElement;

    [Theory]
    [InlineData("order.paid")]
    [InlineData("invoice.payment_failed")]
    [InlineData("user")]
    [InlineData("billing.subscription.renewed")]
    public void Aceita_tipos_validos(string eventType)
    {
        EventValidator.Validate(eventType, ObjectPayload, null).Should().BeNull();
    }

    [Theory]
    [InlineData(null, "event_type_required")]
    [InlineData("", "event_type_required")]
    [InlineData("Order.Paid", "event_type_invalid")]
    [InlineData("order paid", "event_type_invalid")]
    [InlineData("order..paid", "event_type_invalid")]
    [InlineData("order.*", "event_type_invalid")]
    public void Rejeita_tipos_invalidos(string? eventType, string expectedCode)
    {
        EventValidator.Validate(eventType, ObjectPayload, null)!.Code.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("\"texto\"")]
    [InlineData("null")]
    public void Payload_precisa_ser_objeto_ou_array(string json)
    {
        var payload = JsonDocument.Parse(json).RootElement;

        EventValidator.Validate("order.paid", payload, null)!.Code.Should().Be("payload_invalid");
    }

    [Fact]
    public void Payload_ausente_e_rejeitado()
    {
        EventValidator.Validate("order.paid", default, null)!.Code.Should().Be("payload_invalid");
    }

    [Fact]
    public void Idempotency_key_longa_demais_e_rejeitada()
    {
        EventValidator.Validate("order.paid", ObjectPayload, new string('k', 129))!.Code.Should().Be("idempotency_key_invalid");
    }
}
