using Xunit;

using System.Text.Json;
using Misearch.Core.Vpn;

namespace Misearch.CoreTests;

public sealed class WarpServiceTests
{
    [Fact]
    public void Client_id_is_read_from_the_root_of_the_registration_response()
    {
        using var doc = JsonDocument.Parse("""
            {
              "id": "abc",
              "client_id": "AQID",
              "config": { "peers": [], "interface": { "addresses": {} } }
            }
            """);

        var config = doc.RootElement.GetProperty("config");
        Assert.Equal("AQID", WarpService.TryGetClientId(config, doc.RootElement));
    }

    [Fact]
    public void Client_id_is_also_read_when_nested_under_config()
    {
        using var doc = JsonDocument.Parse("""
            { "config": { "client_id": "BAUG", "peers": [] } }
            """);

        var config = doc.RootElement.GetProperty("config");
        Assert.Equal("BAUG", WarpService.TryGetClientId(config, doc.RootElement));
    }

    [Theory]
    [InlineData("""{ "config": { "peers": [] } }""")]
    [InlineData("""{ "client_id": "", "config": {} }""")]
    [InlineData("""{ "client_id": 42, "config": {} }""")]
    public void A_missing_or_invalid_client_id_returns_null(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var config = doc.RootElement.GetProperty("config");

        Assert.Null(WarpService.TryGetClientId(config, doc.RootElement));
    }
}
