using System.Text;
using YamlDotNet.RepresentationModel;

namespace Misearch.Core.Vpn;

public static class MihomoConfigBuilder
{
    public const string ProxyName = "Misearch";

    public static string BuildDirect(int port) => $"""
        mixed-port: {port}
        allow-lan: false
        mode: rule
        log-level: warning
        ipv6: false
        rules:
          - MATCH,DIRECT
        """;

    public static string BuildWireGuard(WireGuardProfile profile, int port)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"mixed-port: {port}");
        sb.AppendLine("allow-lan: false");
        sb.AppendLine("mode: rule");
        sb.AppendLine("log-level: warning");
        sb.AppendLine("ipv6: false");
        sb.AppendLine("dns:");
        sb.AppendLine("  enable: true");
        sb.AppendLine("  ipv6: false");
        sb.AppendLine("  nameserver:");
        var dns = profile.Dns.Where(d => d.Contains('.') || d.Contains(':')).ToList();
        if (dns.Count == 0)
            dns.Add("1.1.1.1");
        foreach (var server in dns)
            sb.AppendLine($"    - {Quote(server)}");
        sb.AppendLine("proxies:");
        sb.AppendLine($"  - name: {ProxyName}");
        sb.AppendLine("    type: wireguard");
        sb.AppendLine($"    server: {Quote(profile.EndpointHost)}");
        sb.AppendLine($"    port: {profile.EndpointPort}");
        sb.AppendLine($"    ip: {Quote(profile.ProxyAddress())}");
        sb.AppendLine($"    private-key: {Quote(profile.PrivateKey)}");
        sb.AppendLine($"    public-key: {Quote(profile.PublicKey)}");
        if (!string.IsNullOrWhiteSpace(profile.PresharedKey))
            sb.AppendLine($"    preshared-key: {Quote(profile.PresharedKey)}");
        sb.AppendLine("    allowed-ips:");
        foreach (var cidr in profile.AllowedIps)
            sb.AppendLine($"      - {Quote(cidr)}");
        sb.AppendLine("    udp: true");
        if (profile.Mtu > 0)
            sb.AppendLine($"    mtu: {profile.Mtu}");
        if (profile.Dns.Count > 0 && profile.Dns[0].Contains('.'))
            sb.AppendLine($"    remote-dns-resolve: true");
        sb.AppendLine("rules:");
        sb.AppendLine($"  - MATCH,{ProxyName}");
        return sb.ToString();
    }

    public static string BuildFromClash(string clashYaml, int port)
    {
        var stream = new YamlStream();
        using (var reader = new StringReader(clashYaml))
            stream.Load(reader);

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new FormatException("El YAML de Clash/Mihomo no tiene una raíz válida.");
        if (!root.Children.ContainsKey("proxies") && !root.Children.ContainsKey("proxy-providers"))
            throw new FormatException("El perfil no contiene «proxies» ni «proxy-providers».");

        root.Children[new YamlScalarNode("mixed-port")] = new YamlScalarNode(port.ToString());
        root.Children[new YamlScalarNode("allow-lan")] = new YamlScalarNode("false");
        root.Children[new YamlScalarNode("mode")] = new YamlScalarNode("rule");

        if (root.Children.TryGetValue("tun", out var tunNode) && tunNode is YamlMappingNode tun)
            tun.Children[new YamlScalarNode("enable")] = new YamlScalarNode("false");

        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    private static string Quote(string value)
    {
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }
}
