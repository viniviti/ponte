using System.Net;
using Ponte.BuildingBlocks.Security;

namespace Ponte.BuildingBlocks.Tests.Unit;

public sealed class NetworkGuardTests
{
    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.16.5.4", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.169.254", true)] // metadata da AWS
    [InlineData("127.0.0.1", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    [InlineData("2001:4860:4860::8888", false)]
    public void IsPrivateOrReserved_bloqueia_redes_internas(string ip, bool expected)
    {
        NetworkGuard.IsPrivateOrReserved(IPAddress.Parse(ip)).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://api.cliente.com/webhooks", false, true)]
    [InlineData("http://api.cliente.com/webhooks", false, false)]
    [InlineData("http://localhost:5199/webhooks", true, true)]
    [InlineData("https://user:pass@api.cliente.com/webhooks", false, false)]
    [InlineData("https://10.0.0.5/webhooks", false, false)]
    [InlineData("isso nao e url", false, false)]
    [InlineData("ftp://api.cliente.com", true, false)]
    public void TryValidateEndpointUrl_valida_esquema_credenciais_e_ip(string url, bool allowInsecure, bool expected)
    {
        NetworkGuard.TryValidateEndpointUrl(url, allowInsecure, out var uri, out var error).Should().Be(expected);

        if (expected)
        {
            uri.Should().NotBeNull();
            error.Should().BeNull();
        }
        else
        {
            error.Should().NotBeNullOrWhiteSpace();
        }
    }
}
