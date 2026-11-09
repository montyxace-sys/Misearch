using Xunit;

using Misearch.Core.Vpn;

namespace Misearch.CoreTests;

public sealed class MihomoConfigBuilderTests
{
    [Fact]
    public void The_direct_config_opens_the_requested_port()
    {
        var yaml = MihomoConfigBuilder.BuildDirect(7891);

        Assert.Contains("mixed-port: 7891", yaml);
        Assert.Contains("MATCH,DIRECT", yaml);
        Assert.DoesNotContain("proxies:", yaml);
    }

    [Fact]
    public void The_wireguard_config_carries_the_keys_and_the_endpoint()
    {
        var profile = WireGuardProfile.Parse("""
            [Interface]
            PrivateKey = a2V5
            Address = 10.7.0.2/32
            DNS = 1.1.1.1

            [Peer]
            PublicKey = cHVi
            Endpoint = engage.cloudflareclient.com:2408
            AllowedIPs = 0.0.0.0/0
            """);

        var yaml = MihomoConfigBuilder.BuildWireGuard(profile, 7890);

        Assert.Contains("mixed-port: 7890", yaml);
        Assert.Contains("type: wireguard", yaml);
        Assert.Contains("\"engage.cloudflareclient.com\"", yaml);
        Assert.Contains("port: 2408", yaml);
        Assert.Contains("\"10.7.0.2/32\"", yaml);
        Assert.Contains("MATCH,Misearch", yaml);
    }

    [Fact]
    public void The_warp_config_includes_the_reserved_bytes()
    {
        var warp = new WarpProfileData
        {
            PrivateKey = "a2V5",
            PeerPublicKey = "cHVi",
            AddressV4 = "172.16.0.2",
            Reserved = [1, 2, 3]
        };

        var yaml = MihomoConfigBuilder.BuildWarp(warp, 7890);

        Assert.Contains("reserved: [1, 2, 3]", yaml);
        Assert.Contains("\"172.16.0.2/32\"", yaml);
        Assert.Contains("MATCH,Misearch", yaml);
    }

    [Fact]
    public void An_imported_clash_profile_is_pinned_to_the_browser_port()
    {
        var yaml = MihomoConfigBuilder.BuildFromClash("""
            mixed-port: 7890
            allow-lan: true
            mode: global
            tun:
              enable: true
            proxies:
              - name: nodo
                type: ss
            """, 45678);

        Assert.Contains("mixed-port: 45678", yaml);
        Assert.Contains("allow-lan: false", yaml);
        Assert.Contains("mode: rule", yaml);
        Assert.Contains("enable: false", yaml);
    }

    [Fact]
    public void A_clash_profile_without_proxies_is_rejected()
    {
        Assert.Throws<FormatException>(() =>
            MihomoConfigBuilder.BuildFromClash("mode: rule\n", 7890));
    }
}
