using System.Globalization;

namespace Misearch.Core.Vpn;

public sealed class WireGuardProfile
{
    public string PrivateKey { get; set; } = "";
    public List<string> Addresses { get; set; } = new();
    public List<string> Dns { get; set; } = new();
    public int Mtu { get; set; } = 1420;
    public int ListenPort { get; set; }
    public string PublicKey { get; set; } = "";
    public string PresharedKey { get; set; } = "";
    public string EndpointHost { get; set; } = "";
    public int EndpointPort { get; set; } = 51820;
    public List<string> AllowedIps { get; set; } = new();
    public int PersistentKeepalive { get; set; }

    public static WireGuardProfile Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("El archivo WireGuard está vacío.");

        var profile = new WireGuardProfile();
        string section = "";
        var peerCount = 0;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                if (section.Equals("Peer", StringComparison.OrdinalIgnoreCase))
                    peerCount++;
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length == 0)
                continue;

            if (section.Equals("Interface", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "privatekey": profile.PrivateKey = value; break;
                    case "address":
                        profile.Addresses.AddRange(Split(value));
                        break;
                    case "dns":
                        profile.Dns.AddRange(Split(value));
                        break;
                    case "mtu":
                        if (int.TryParse(value, CultureInfo.InvariantCulture, out var mtu)) profile.Mtu = mtu;
                        break;
                    case "listenport":
                        if (int.TryParse(value, CultureInfo.InvariantCulture, out var lp)) profile.ListenPort = lp;
                        break;
                }
            }
            else if (section.Equals("Peer", StringComparison.OrdinalIgnoreCase) && peerCount == 1)
            {
                switch (key.ToLowerInvariant())
                {
                    case "publickey": profile.PublicKey = value; break;
                    case "presharedkey": profile.PresharedKey = value; break;
                    case "endpoint": ParseEndpoint(profile, value); break;
                    case "allowedips":
                        profile.AllowedIps.AddRange(Split(value));
                        break;
                    case "persistentkeepalive":
                        if (int.TryParse(value, CultureInfo.InvariantCulture, out var keep)) profile.PersistentKeepalive = keep;
                        break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(profile.PrivateKey))
            throw new FormatException("Falta PrivateKey en [Interface].");
        if (string.IsNullOrWhiteSpace(profile.PublicKey))
            throw new FormatException("Falta PublicKey en [Peer].");
        if (string.IsNullOrWhiteSpace(profile.EndpointHost))
            throw new FormatException("Falta Endpoint en [Peer].");
        if (profile.Addresses.Count == 0)
            throw new FormatException("Falta Address en [Interface].");
        if (profile.AllowedIps.Count == 0)
            profile.AllowedIps.Add("0.0.0.0/0");

        return profile;
    }

    public string ProxyAddress()
    {
        var chosen = Addresses.FirstOrDefault(a => a.Contains('.') && !a.Contains(':'))
                     ?? Addresses.FirstOrDefault(a => !a.Contains(':'))
                     ?? Addresses[0];
        if (!chosen.Contains('/'))
            chosen = chosen.Contains(':') ? chosen + "/128" : chosen + "/32";
        return chosen;
    }

    private static IEnumerable<string> Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void ParseEndpoint(WireGuardProfile profile, string value)
    {
        if (value.StartsWith('['))
        {
            var close = value.IndexOf(']');
            if (close < 0)
                throw new FormatException("Endpoint IPv6 inválido.");
            profile.EndpointHost = value[1..close];
            var rest = value[(close + 1)..];
            if (rest.StartsWith(':') &&
                int.TryParse(rest[1..], CultureInfo.InvariantCulture, out var port))
                profile.EndpointPort = port;
            return;
        }

        var colon = value.LastIndexOf(':');
        if (colon > 0 && int.TryParse(value[(colon + 1)..], CultureInfo.InvariantCulture, out var parsedPort))
        {
            profile.EndpointHost = value[..colon];
            profile.EndpointPort = parsedPort;
        }
        else
        {
            profile.EndpointHost = value;
        }
    }
}
