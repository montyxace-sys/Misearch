using Xunit;

using Misearch.Core.Vpn;

namespace Misearch.CoreTests;

public sealed class WireGuardProfileTests
{
    private const string Sample = """
        [Interface]
        PrivateKey = aGVsbG8tcHJpdmF0ZS1rZXk=
        Address = 10.7.0.2/32, fd01::2/128
        DNS = 1.1.1.1, 2606:4700:4700::1111
        MTU = 1420

        [Peer]
        PublicKey = aGVsbG8tcHVibGljLWtleQ==
        Endpoint = engage.cloudflareclient.com:2408
        AllowedIPs = 0.0.0.0/0, ::/0
        PersistentKeepalive = 25
        """;

    [Fact]
    public void A_complete_profile_is_parsed()
    {
        var profile = WireGuardProfile.Parse(Sample);

        Assert.Equal("aGVsbG8tcHJpdmF0ZS1rZXk=", profile.PrivateKey);
        Assert.Equal("engage.cloudflareclient.com", profile.EndpointHost);
        Assert.Equal(2408, profile.EndpointPort);
        Assert.Equal(1420, profile.Mtu);
        Assert.Equal(25, profile.PersistentKeepalive);
        Assert.Equal(2, profile.AllowedIps.Count);
        Assert.Equal(2, profile.Addresses.Count);
    }

    [Fact]
    public void The_ipv4_address_is_preferred_for_the_tunnel()
    {
        Assert.Equal("10.7.0.2/32", WireGuardProfile.Parse(Sample).ProxyAddress());
    }

    [Fact]
    public void An_ipv6_only_profile_gets_a_128_prefix()
    {
        var profile = WireGuardProfile.Parse("""
            [Interface]
            PrivateKey = a2V5
            Address = fd01::2

            [Peer]
            PublicKey = cHVi
            Endpoint = [2606:4700::1]:2408
            AllowedIPs = ::/0
            """);

        Assert.Equal("fd01::2/128", profile.ProxyAddress());
        Assert.Equal("2606:4700::1", profile.EndpointHost);
        Assert.Equal(2408, profile.EndpointPort);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[Interface]\nAddress = 10.0.0.1/32")]
    public void An_incomplete_profile_is_rejected(string text)
    {
        Assert.Throws<FormatException>(() => WireGuardProfile.Parse(text));
    }

    [Fact]
    public void Comments_and_blank_lines_are_ignored()
    {
        var profile = WireGuardProfile.Parse("""
            # perfil de prueba
            [Interface]
            PrivateKey = a2V5
            Address = 10.0.0.1/32

            ; comentario
            [Peer]
            PublicKey = cHVi
            Endpoint = ejemplo.com
            AllowedIPs = 0.0.0.0/0
            """);

        Assert.Equal(51820, profile.EndpointPort);
        Assert.Equal("ejemplo.com", profile.EndpointHost);
    }
}
