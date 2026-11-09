using Xunit;

using Misearch.Core.Models;
using Misearch.Core.Services;

namespace Misearch.CoreTests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;

    public SettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "misearch-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { }
    }

    private SettingsStore NewStore() => new(() => _file);

    [Fact]
    public void Settings_mutated_after_load_are_written_by_save()
    {
        // Regression: the window used to keep its own copy of AppSettings, so the engine,
        // the zoom and the switches were lost on exit because Save() serialised the
        // store's untouched instance.
        var store = NewStore();
        var settings = store.Load();
        store.Settings = settings;

        settings.SearchEngine = SearchEngine.Brave;
        settings.Zoom = 1.4;
        settings.BlockTrackers = false;
        store.Save();

        var reloaded = NewStore().Load();
        Assert.Equal(SearchEngine.Brave, reloaded.SearchEngine);
        Assert.Equal(1.4, reloaded.Zoom);
        Assert.False(reloaded.BlockTrackers);
    }

    [Fact]
    public void Save_uses_the_instance_assigned_to_the_store()
    {
        var store = NewStore();
        store.Load();
        store.Settings = new AppSettings { SearchEngine = SearchEngine.Startpage, HttpsFirst = false };
        store.Save();

        var reloaded = NewStore().Load();
        Assert.Equal(SearchEngine.Startpage, reloaded.SearchEngine);
        Assert.False(reloaded.HttpsFirst);
    }

    [Fact]
    public void Load_fills_the_download_folder_when_it_is_missing()
    {
        File.WriteAllText(_file, "{ \"SearchEngine\": \"Bing\", \"DownloadFolder\": \"\" }");

        var settings = NewStore().Load();

        Assert.Equal(SearchEngine.Bing, settings.SearchEngine);
        Assert.False(string.IsNullOrWhiteSpace(settings.DownloadFolder));
    }

    [Fact]
    public void Load_falls_back_to_defaults_on_corrupt_json()
    {
        File.WriteAllText(_file, "{ esto no es json");

        var settings = NewStore().Load();

        Assert.Equal(SearchEngine.DuckDuckGo, settings.SearchEngine);
        Assert.True(settings.BlockTrackers);
    }
}
