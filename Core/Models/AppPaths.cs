using System.Text.Json;

namespace Misearch.Core.Models
{
    public static class AppPaths
    {
        public static string RoamingRoot { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Misearch");

        public static string LocalRoot { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Misearch");

        public static string BrowserData => Path.Combine(LocalRoot, "browser");
        public static string CoreDir => Path.Combine(LocalRoot, "core");
        public static string VpnDir => Path.Combine(LocalRoot, "vpn");
        public static string SettingsFile => Path.Combine(RoamingRoot, "settings.json");
        public static string HistoryFile => Path.Combine(RoamingRoot, "history.json");
        public static string BookmarksFile => Path.Combine(RoamingRoot, "bookmarks.json");
        public static string UserBlocklistFile => Path.Combine(RoamingRoot, "blocklist.txt");

        public static string DefaultDownloadFolder
        {
            get
            {
                var downloads = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                return Directory.Exists(downloads) ? Path.Combine(downloads, "Misearch") : downloads;
            }
        }

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(RoamingRoot);
            Directory.CreateDirectory(LocalRoot);
            Directory.CreateDirectory(BrowserData);
            Directory.CreateDirectory(CoreDir);
            Directory.CreateDirectory(VpnDir);
        }
    }
}
