using Xunit;

using Misearch.Core.Models;
using Misearch.Core.Services;

namespace Misearch.CoreTests;

public sealed class SearchRouterTests
{
    [Theory]
    [InlineData("ejemplo.com", "https://ejemplo.com")]
    [InlineData("sub.ejemplo.com/ruta?x=1", "https://sub.ejemplo.com/ruta?x=1")]
    [InlineData("localhost:3000", "http://localhost:3000")]
    [InlineData("192.168.1.10:8080", "http://192.168.1.10:8080")]
    [InlineData("10.0.0.5", "http://10.0.0.5")]
    [InlineData("https://ya.esquema.com", "https://ya.esquema.com")]
    [InlineData("misearch://newtab", "https://misearch.app/newtab.html")]
    public void Hosts_and_schemes_resolve_to_a_url(string input, string expected)
    {
        Assert.True(SearchRouter.TryResolve(input, SearchEngine.DuckDuckGo, out var url));
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData("c#")]
    [InlineData(".net")]
    [InlineData("seguridad en la red")]
    [InlineData("qué es un vpn")]
    [InlineData("v1.2")]
    public void Terms_that_only_look_like_hosts_go_to_the_search_engine(string input)
    {
        Assert.True(SearchRouter.TryResolve(input, SearchEngine.DuckDuckGo, out var url));
        Assert.StartsWith("https://duckduckgo.com/?q=", url);
        Assert.Contains(Uri.EscapeDataString(input), url);
    }

    [Fact]
    public void Empty_input_is_not_resolved()
    {
        Assert.False(SearchRouter.TryResolve("   ", SearchEngine.DuckDuckGo, out var url));
        Assert.Equal("", url);
    }

    [Fact]
    public void The_chosen_engine_is_used_for_searches()
    {
        Assert.True(SearchRouter.TryResolve("redes privadas", SearchEngine.Brave, out var url));
        Assert.StartsWith("https://search.brave.com/search?q=", url);
    }

    [Fact]
    public void Every_engine_builds_a_url_with_the_query_encoded()
    {
        foreach (var engine in Enum.GetValues<SearchEngine>())
        {
            var url = SearchRouter.BuildSearchUrl(engine, "a&b c");
            Assert.Contains("a%26b%20c", url);
            Assert.StartsWith("https://", url);
        }
    }

    [Theory]
    [InlineData("ejemplo.com", true)]
    [InlineData("sub.ejemplo.co.uk", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("c#", false)]
    [InlineData(".net", false)]
    [InlineData("node.js", true)]
    [InlineData("localhost", true)]
    [InlineData("localhost:3000", true)]
    [InlineData("texto.com.", true)]
    [InlineData("ejemplo..com", false)]
    [InlineData("", false)]
    public void Host_detection_rejects_terms_with_odd_characters(string candidate, bool expected)
    {
        Assert.Equal(expected, SearchRouter.IsResolvableHostName(candidate));
    }
}
