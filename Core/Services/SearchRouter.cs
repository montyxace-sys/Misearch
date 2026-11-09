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

    public static string BuildSearchUrl(SearchEngine engine, string query) =>
        BuildSearchUrl(engine, query, Engines);

    public static bool TryResolve(string input, SearchEngine engine, out string url) =>
        TryResolve(input, engine, Engines, out url);

    /// <summary>
    /// Resolves what the user typed into a URL. A term with no spaces that is not a host
    /// (for example <c>c#</c>, <c>node.js</c> or <c>.net</c>) is sent to the search engine
    /// instead of being turned into a broken <c>https://</c> address.
    /// </summary>
    public static bool TryResolve(string input, SearchEngine engine,
        IReadOnlyList<SearchEngineInfo> engines, out string url)
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
        var looksLikeHost = !text.Any(char.IsWhiteSpace) && IsResolvableHostName(hostCandidate);

        if (looksLikeHost)
        {
            var scheme = IsLocalHost(hostCandidate) ? "http://" : "https://";
            url = scheme + text;
            return true;
        }

        url = BuildSearchUrl(engine, text, engines);
        return true;
    }

    /// <summary>
    /// True when the text before the first slash has the shape of a host name: valid
    /// characters in every label and a last label that can be a TLD. "localhost" on its
    /// own has no dot, so it is the one bare name accepted; terms such as "c#", ".net" or
    /// "node.js" are rejected and end up in the search engine.
    /// </summary>
    public static bool IsResolvableHostName(string hostCandidate)
    {
        var name = hostCandidate.Split(':')[0].TrimEnd('.');
        if (name.Length == 0)
            return false;
        if (IPAddress.TryParse(name, out _))
            return true;
        if (name.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        // A leading dot (".net"), a dot with nothing behind it, or a last label that is too
        // short to be a TLD ("node.js") means the user is typing a term, not an address.
        if (name.StartsWith('.'))
            return false;

        var labels = name.Split('.');
        foreach (var label in labels)
        {
            if (label.Length == 0)
                return false;
            foreach (var ch in label)
            {
                var ok = char.IsLetterOrDigit(ch) || ch == '-' || ch == '_';
                if (!ok)
                    return false;
            }
        }

        // The last label must look like a TLD: letters only, at least two of them.
        var tld = labels[^1];
        return tld.Length >= 2 && tld.All(char.IsLetter);
    }

    public static string BuildSearchUrl(SearchEngine engine, string query,
        IReadOnlyList<SearchEngineInfo> engines)
    {
        var template = engines.First(e => e.Id == engine).QueryUrl;
        return string.Format(template, Uri.EscapeDataString(query));
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
