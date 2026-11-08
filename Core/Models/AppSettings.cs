using System.Text.Json.Serialization;

namespace Misearch.Core.Models;

public enum SearchEngine
{
    DuckDuckGo,
    Google,
    Bing,
    Startpage,
    Brave
}

public sealed class AppSettings
{
    public SearchEngine SearchEngine { get; set; } = SearchEngine.DuckDuckGo;
    public bool BlockTrackers { get; set; } = true;
    public bool PrivateMode { get; set; } = true;
    public bool ClearOnExit { get; set; } = true;
    public bool HttpsFirst { get; set; } = true;
    public bool VpnEnabled { get; set; }
    public string VpnProfilePath { get; set; } = "";
    public string VpnProfileKind { get; set; } = "";
    public int ProxyPort { get; set; }
    public string DownloadFolder { get; set; } = "";
    public double Zoom { get; set; } = 1.0;
}
