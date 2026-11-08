using System.Reflection;
using Misearch.Core.Models;

namespace Misearch.Core.Services;

public sealed class TrackerBlocklist
{
    private readonly HashSet<string> _domains = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public int Count
    {
        get { lock (_gate) return _domains.Count; }
    }

    public void Load()
    {
        lock (_gate)
        {
            _domains.Clear();
            var assembly = Assembly.GetExecutingAssembly();
            var name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("blocklist.txt", StringComparison.OrdinalIgnoreCase));
            if (name != null)
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var lines = new List<string>();
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                        lines.Add(line);
                    ReadDomains(lines);
                }
            }

            if (File.Exists(AppPaths.UserBlocklistFile))
                ReadDomains(File.ReadAllLines(AppPaths.UserBlocklistFile));
        }
    }

    public bool IsBlocked(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        host = host.TrimEnd('.');
        lock (_gate)
        {
            if (_domains.Contains(host))
                return true;
            var index = host.IndexOf('.');
            while (index > 0)
            {
                if (_domains.Contains(host[(index + 1)..]))
                    return true;
                index = host.IndexOf('.', index + 1);
            }
            return false;
        }
    }

    private void ReadDomains(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('!'))
                continue;
            if (line.StartsWith("0.0.0.0 ", StringComparison.Ordinal) ||
                line.StartsWith("127.0.0.1 ", StringComparison.Ordinal))
                line = line.Split(' ', 2)[1].Trim();
            if (line.Contains(' '))
                continue;
            _domains.Add(line);
        }
    }
}
