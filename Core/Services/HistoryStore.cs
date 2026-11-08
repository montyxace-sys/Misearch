using System.Text.Json;
using Misearch.Core.Models;

namespace Misearch.Core.Services;

public sealed class HistoryEntry
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset VisitedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class Bookmark
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class HistoryStore
{
    private const int MaxHistory = 2000;
    private readonly object _gate = new();
    private readonly List<HistoryEntry> _history = new();
    private readonly List<Bookmark> _bookmarks = new();

    public IReadOnlyList<HistoryEntry> History
    {
        get { lock (_gate) return _history.ToList(); }
    }

    public IReadOnlyList<Bookmark> Bookmarks
    {
        get { lock (_gate) return _bookmarks.ToList(); }
    }

    public void Load()
    {
        AppPaths.EnsureDirectories();
        lock (_gate)
        {
            _history.Clear();
            _bookmarks.Clear();
            try
            {
                if (File.Exists(AppPaths.HistoryFile))
                    _history.AddRange(JsonSerializer.Deserialize<List<HistoryEntry>>(
                        File.ReadAllText(AppPaths.HistoryFile)) ?? new List<HistoryEntry>());
            }
            catch (Exception)
            {
                // A corrupt history must never stop the browser from starting.
            }

            try
            {
                if (File.Exists(AppPaths.BookmarksFile))
                    _bookmarks.AddRange(JsonSerializer.Deserialize<List<Bookmark>>(
                        File.ReadAllText(AppPaths.BookmarksFile)) ?? new List<Bookmark>());
            }
            catch (Exception)
            {
            }
        }
    }

    public void AddVisit(string url, string title)
    {
        if (url.StartsWith("https://misearch.app/", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            return;

        lock (_gate)
        {
            var existing = _history.FirstOrDefault(h => h.Url == url);
            if (existing != null)
            {
                existing.Title = string.IsNullOrWhiteSpace(title) ? existing.Title : title;
                existing.VisitedAt = DateTimeOffset.Now;
                _history.Remove(existing);
            }
            _history.Add(new HistoryEntry { Url = url, Title = title, VisitedAt = DateTimeOffset.Now });
            if (_history.Count > MaxHistory)
                _history.RemoveRange(0, _history.Count - MaxHistory);
            SaveHistory();
        }
    }

    public IReadOnlyList<HistoryEntry> SearchHistory(string term)
    {
        lock (_gate)
        {
            var items = string.IsNullOrWhiteSpace(term)
                ? _history.ToList()
                : _history.Where(h =>
                    h.Url.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    h.Title.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
            return items.OrderByDescending(h => h.VisitedAt).Take(200).ToList();
        }
    }

    public bool IsBookmarked(string url)
    {
        lock (_gate)
            return _bookmarks.Any(b => b.Url == url);
    }

    public void ToggleBookmark(string url, string title)
    {
        lock (_gate)
        {
            var existing = _bookmarks.FirstOrDefault(b => b.Url == url);
            if (existing != null)
                _bookmarks.Remove(existing);
            else
                _bookmarks.Insert(0, new Bookmark { Url = url, Title = title });
            SaveBookmarks();
        }
    }

    public void RemoveBookmark(string url)
    {
        lock (_gate)
        {
            _bookmarks.RemoveAll(b => b.Url == url);
            SaveBookmarks();
        }
    }

    public void ClearHistory()
    {
        lock (_gate)
        {
            _history.Clear();
            SaveHistory();
        }
    }

    public void ClearAll()
    {
        lock (_gate)
        {
            _history.Clear();
            _bookmarks.Clear();
            SaveHistory();
            SaveBookmarks();
        }
    }

    private void SaveHistory() =>
        File.WriteAllText(AppPaths.HistoryFile, JsonSerializer.Serialize(_history));

    private void SaveBookmarks() =>
        File.WriteAllText(AppPaths.BookmarksFile, JsonSerializer.Serialize(_bookmarks));
}
