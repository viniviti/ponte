using System.Net;
using System.Net.Sockets;

namespace Ponte.BuildingBlocks.Security;

/// <summary>
/// Protecao contra SSRF. Um webhook e literalmente "o cliente escolhe uma URL e o nosso
/// servidor faz a requisicao", entao sem isso qualquer tenant poderia mirar a rede
/// interna ou o metadata da AWS (169.254.169.254).
/// </summary>
public static class NetworkGuard
{
    public static bool IsPrivateOrReserved(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                0 or 10 or 127 => true,
                100 when b[1] is >= 64 and <= 127 => true,   // CGNAT
                169 when b[1] == 254 => true,                // link-local / metadata da cloud
                172 when b[1] is >= 16 and <= 31 => true,
                192 when b[1] == 168 => true,
                192 when b[1] == 0 && b[2] == 0 => true,
                198 when b[1] is 18 or 19 => true,           // benchmark
                >= 224 => true,                              // multicast e reservado
                _ => false,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return address.IsIPv6LinkLocal
                   || address.IsIPv6SiteLocal
                   || address.IsIPv6Multicast
                   || (b[0] & 0xFE) == 0xFC; // fc00::/7 unique local
        }

        return true;
    }

    /// <summary>Validacao de formato no cadastro. A barreira definitiva acontece na conexao (DNS rebinding).</summary>
    public static bool TryValidateEndpointUrl(string url, bool allowInsecure, out Uri? uri, out string? error)
    {
        uri = null;
        error = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            error = "URL invalida.";
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttps && !(allowInsecure && parsed.Scheme == Uri.UriSchemeHttp))
        {
            error = "O endpoint precisa usar HTTPS.";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            error = "Credenciais na URL nao sao permitidas.";
            return false;
        }

        if (!allowInsecure && IPAddress.TryParse(parsed.DnsSafeHost, out var literal) && IsPrivateOrReserved(literal))
        {
            error = "Enderecos de rede privada nao sao permitidos.";
            return false;
        }

        uri = parsed;
        return true;
    }
}
