using System.Text.RegularExpressions;

namespace Ponte.Ingestion.Api.Application;

public static partial class EventValidator
{
    public const int MaxEventTypeLength = 128;
    public const int MaxIdempotencyKeyLength = 128;
    public const int MaxPayloadBytes = 256 * 1024;

    /// <summary>Formato "dominio.acao" em minusculas, ex.: order.paid, invoice.payment_failed.</summary>
    [GeneratedRegex("^[a-z0-9_-]+(\\.[a-z0-9_-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex EventTypePattern();

    public static Error? Validate(string? eventType, JsonElement payload, string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return new Error("event_type_required", "Informe o eventType.");
        }

        if (eventType.Length > MaxEventTypeLength || !EventTypePattern().IsMatch(eventType))
        {
            return new Error("event_type_invalid", "eventType deve seguir o formato 'dominio.acao' em minusculas (ex.: order.paid).");
        }

        if (payload.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            return new Error("payload_invalid", "payload deve ser um objeto ou array JSON.");
        }

        if (idempotencyKey is { Length: 0 or > MaxIdempotencyKeyLength })
        {
            return new Error("idempotency_key_invalid", $"Idempotency-Key deve ter entre 1 e {MaxIdempotencyKeyLength} caracteres.");
        }

        return null;
    }
}
