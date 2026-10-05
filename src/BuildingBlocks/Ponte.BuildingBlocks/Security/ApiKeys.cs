using System.Security.Cryptography;

namespace Ponte.BuildingBlocks.Security;

/// <summary>
/// API keys nunca sao armazenadas em texto puro: guardamos apenas o SHA-256.
/// A chave tem alta entropia (256 bits), entao um hash rapido e suficiente (nao e senha).
/// </summary>
public static class ApiKeys
{
    public const string Header = "X-Api-Key";
    public const string Prefix = "pk_";

    public static string Generate(string environment = "live")
    {
        var random = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        return $"{Prefix}{environment}_{random}";
    }

    public static string Hash(string apiKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey))).ToLowerInvariant();

    /// <summary>Trecho exibivel na UI para o usuario reconhecer a chave (ex.: pk_live_3fa9...).</summary>
    public static string DisplayPrefix(string apiKey) => apiKey.Length <= 12 ? apiKey : apiKey[..12];
}
