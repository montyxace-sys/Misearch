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

    private readonly Func<string> _path;

    public SettingsStore() : this(() => AppPaths.SettingsFile)
    {
    }

    /// <summary>Lets tests point the store at a temporary file.</summary>
    public SettingsStore(Func<string> pathProvider)
    {
        _path = pathProvider;
    }

    /// <summary>
    /// The live settings object. <see cref="Save"/> serialises this instance, so callers
    /// must keep mutating the object returned by <see cref="Load"/> (or assign a new one
    /// here) instead of holding a copy of their own.
    /// </summary>
    public AppSettings Settings { get; set; } = new();

    private string Path => _path();

    public AppSettings Load()
    {
        AppPaths.EnsureDirectories();
        try
        {
            if (File.Exists(Path))
            {
                var json = File.ReadAllText(Path);
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
        File.WriteAllText(Path, JsonSerializer.Serialize(Settings, Options));
    }
}
