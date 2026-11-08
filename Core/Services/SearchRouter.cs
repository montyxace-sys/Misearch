using System.Net;
using Misearch.Core.Models;

namespace Misearch.Core.Services;

public sealed record SearchEngineInfo(SearchEngine Id, string Name, string QueryUrl);

public static class SearchRouter
{
    public static IReadOnlyList<SearchEngineInfo> Engines { get; } = new[]
    {
        new SearchEngineInfo(SearchEngine.DuckDuckGo, "DuckDuckGo", "https://duckduckgo.com/?q={0}"),
        new SearchEngineInfo(SearchEngine.Google, "Google", "https://www.google.com/search?q={0}"),
        new SearchEngineInfo(SearchEngine.Bing, "Bing", "https://www.bing.com/search?q={0}"),
        new SearchEngineInfo(SearchEngine.Startpage, "Startpage", "https://www.startpage.com/sp/search?query={0}"),
        new SearchEngineInfo(SearchEngine.Brave, "Brave", "https://search.brave.com/search?q={0}")
    };

    public static string BuildSearchUrl(SearchEngine engine, string query)
    {
        var template = Engines.First(e => e.Id == engine).QueryUrl;
        return string.Format(template, Uri.EscapeDataString(query));
    }

    public static bool TryResolve(string input, SearchEngine engine, out string url)
    {
        url = "";
        var text = (input ?? "").Trim();
        if (text.Length == 0)
            return false;

        if (text.Equals("misearch://newtab", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://misearch.app/newtab.html";
            return true;
        }

        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            url = text;
            return true;
        }

        var hostCandidate = text.Split('/', 2)[0];
        var looksLikeHost =
            !text.Any(char.IsWhiteSpace) &&
            (hostCandidate.Contains('.') || hostCandidate.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
             IPAddress.TryParse(hostCandidate.Split(':')[0], out _));

        if (looksLikeHost)
        {
            var scheme = IsLocalHost(hostCandidate) ? "http://" : "https://";
            url = scheme + text;
            return true;
        }

        url = BuildSearchUrl(engine, text);
        return true;
    }

    public static string EngineName(SearchEngine engine) =>
        Engines.First(e => e.Id == engine).Name;

    private static bool IsLocalHost(string host)
    {
        var name = host.Split(':')[0];
        if (name.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        if (IPAddress.TryParse(name, out var ip))
        {
            var bytes = ip.GetAddressBytes();
            if (bytes.Length != 4)
                return false;
            return bytes[0] == 127 || bytes[0] == 10 ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31);
        }
        return false;
    }
}
