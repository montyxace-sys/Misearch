using System.Text.Json;
using System.Text.Json.Serialization;
using Misearch.Core.Models;

namespace Misearch.Core.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettings Settings { get; private set; } = new();

    public AppSettings Load()
    {
        AppPaths.EnsureDirectories();
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                Settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            Settings = new AppSettings();
        }

        if (string.IsNullOrWhiteSpace(Settings.DownloadFolder))
            Settings.DownloadFolder = AppPaths.DefaultDownloadFolder;

        return Settings;
    }

    public void Save()
    {
        AppPaths.EnsureDirectories();
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(Settings, Options));
    }
}
