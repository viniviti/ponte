using System.Security.Cryptography;

namespace Ponte.BuildingBlocks.Security;

/// <summary>
/// Assinatura compativel com a especificacao Standard Webhooks (standardwebhooks.com),
/// a mesma usada por Svix, Clerk, Resend e outros. Quem recebe pode validar com
/// qualquer biblioteca oficial da spec, em qualquer linguagem.
///
///   conteudo  = "{webhook-id}.{webhook-timestamp}.{corpo}"
///   assinatura = "v1," + base64(HMAC-SHA256(segredo, conteudo))
/// </summary>
public static class StandardWebhook
{
    public const string IdHeader = "webhook-id";
    public const string TimestampHeader = "webhook-timestamp";
    public const string SignatureHeader = "webhook-signature";

    private const string SecretPrefix = "whsec_";

    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    public static string GenerateSecret() => SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static string Sign(string secret, string messageId, long timestamp, string payload)
    {
        var key = DecodeSecret(secret);
        var content = Encoding.UTF8.GetBytes($"{messageId}.{timestamp}.{payload}");
        var hash = HMACSHA256.HashData(key, content);
        return "v1," + Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Valida a assinatura em tempo constante e rejeita timestamps fora da tolerancia
    /// (protecao contra replay). O header pode trazer varias assinaturas separadas por
    /// espaco, o que permite rotacionar o segredo sem downtime.
    /// </summary>
    public static bool Verify(
        string secret,
        string messageId,
        long timestamp,
        string payload,
        string signatureHeader,
        DateTimeOffset now,
        TimeSpan? tolerance = null)
    {
        var window = tolerance ?? DefaultTolerance;
        if (Math.Abs(now.ToUnixTimeSeconds() - timestamp) > window.TotalSeconds)
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(Sign(secret, messageId, timestamp, payload));
        foreach (var candidate in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), expected))
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] DecodeSecret(string secret) =>
        Convert.FromBase64String(secret.StartsWith(SecretPrefix, StringComparison.Ordinal) ? secret[SecretPrefix.Length..] : secret);
}
