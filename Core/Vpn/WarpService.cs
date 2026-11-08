using System.Net.Http.Json;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Misearch.Core.Vpn;

public sealed class WarpProfileData
{
    public string PrivateKey { get; set; } = "";
    public string PeerPublicKey { get; set; } = "";
    public string AddressV4 { get; set; } = "";
    public string AddressV6 { get; set; } = "";
    public string EndpointHost { get; set; } = "engage.cloudflareclient.com";
    public int EndpointPort { get; set; } = 2408;
    public int[] Reserved { get; set; } = [0, 0, 0];
}

public static class WarpService
{
    private const string RegisterUrl = "https://api.cloudflareclient.com/v0a2158/reg";

    public static async Task<WarpProfileData> RegisterAsync(CancellationToken ct = default)
    {
        var generator = new X25519KeyPairGenerator();
        generator.Init(new X25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        var privateKey = ((X25519PrivateKeyParameters)pair.Private).GetEncoded();
        var publicKey = ((X25519PublicKeyParameters)pair.Public).GetEncoded();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Post, RegisterUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", "okhttp/3.12.1");
        request.Headers.TryAddWithoutValidation("CF-Client-Version", "a-6.30-3596");
        request.Content = JsonContent.Create(new
        {
            key = Convert.ToBase64String(publicKey),
            install_id = "",
            fcm_token = "",
            tos = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
            model = "PC",
            type = "Windows",
            locale = "es_ES"
        });

        using var response = await http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Cloudflare WARP rechazó el registro ({(int)response.StatusCode}): {json[..Math.Min(json.Length, 180)]}");

        using var doc = JsonDocument.Parse(json);
        var config = doc.RootElement.GetProperty("config");
        var peer = config.GetProperty("peers")[0];
        var addresses = config.GetProperty("interface").GetProperty("addresses");
        var endpoint = peer.GetProperty("endpoint").GetProperty("host").GetString() ?? "engage.cloudflareclient.com:2408";

        var host = endpoint;
        var port = 2408;
        var colon = endpoint.LastIndexOf(':');
        if (colon > 0 && int.TryParse(endpoint[(colon + 1)..], out var parsedPort))
        {
            host = endpoint[..colon];
            port = parsedPort;
        }

        var reserved = new int[3];
        if (config.TryGetProperty("client_id", out var clientId) &&
            !string.IsNullOrWhiteSpace(clientId.GetString()))
        {
            try
            {
                var bytes = Convert.FromBase64String(clientId.GetString()!);
                for (var i = 0; i < 3 && i < bytes.Length; i++)
                    reserved[i] = bytes[i];
            }
            catch (FormatException)
            {
            }
        }

        return new WarpProfileData
        {
            PrivateKey = Convert.ToBase64String(privateKey),
            PeerPublicKey = peer.GetProperty("public_key").GetString() ?? "",
            AddressV4 = addresses.GetProperty("v4").GetString() ?? "",
            AddressV6 = addresses.TryGetProperty("v6", out var v6) ? v6.GetString() ?? "" : "",
            EndpointHost = host,
            EndpointPort = port,
            Reserved = reserved
        };
    }
}
