using System.Diagnostics;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using Misearch.Core.Models;

namespace Misearch.Core.Vpn;

public sealed class MihomoManager : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly object _logGate = new();
    private readonly StringBuilder _stderr = new();
    private Process? _process;

    public const string EngineName = "Mihomo";
    public string EnginePath => Path.Combine(AppPaths.CoreDir, "mihomo.exe");
    public bool IsInstalled => File.Exists(EnginePath);

    public int Port { get; private set; }
    public string Mode { get; private set; } = "direct";
    public bool IsRunning => _process is { HasExited: false };
    public string? LastError { get; private set; }

    public event EventHandler? StateChanged;

    public async Task StartAsync(int port, string configPath, string mode, CancellationToken cancellationToken = default)
    {
        Stop();

        if (!IsInstalled)
            throw new FileNotFoundException("El motor VPN no está descargado.");

        Port = port;
        Mode = mode;
        LastError = null;

        lock (_logGate)
            _stderr.Clear();

        var info = new ProcessStartInfo(EnginePath)
        {
            WorkingDirectory = AppPaths.CoreDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        info.ArgumentList.Add("-d");
        info.ArgumentList.Add(AppPaths.CoreDir);
        info.ArgumentList.Add("-f");
        info.ArgumentList.Add(configPath);

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (_logGate)
            {
                _stderr.AppendLine(e.Data);
                if (_stderr.Length > 8000)
                    _stderr.Remove(0, _stderr.Length - 8000);
            }
        };
        process.OutputDataReceived += (_, _) => { };
        process.Exited += (_, _) =>
        {
            LastError = process.ExitCode == 0
                ? "El motor VPN se detuvo."
                : $"El motor VPN se detuvo (código {process.ExitCode}).";
            StateChanged?.Invoke(this, EventArgs.Empty);
        };

        _process = process;
        process.Start();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new InvalidOperationException("El motor VPN no arrancó. " + Tail());
            if (CanConnect(port))
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
            await Task.Delay(150, cancellationToken);
        }

        Stop();
        throw new TimeoutException("El motor VPN no abrió el puerto local a tiempo. " + Tail());
    }

    public void Stop()
    {
        var process = _process;
        _process = null;
        if (process == null)
            return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch (Exception)
        {
        }
        process.Dispose();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Mirrors a profile and returns the path mihomo should load.</summary>
    public static string WriteConfig(string yaml)
    {
        AppPaths.EnsureDirectories();
        var path = Path.Combine(AppPaths.VpnDir, "config.yaml");
        File.WriteAllText(path, yaml);
        return path;
    }

    public async Task<string> EnsureEngineAsync(IProgress<(double Percent, string Message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsInstalled)
            return EnginePath;

        Directory.CreateDirectory(AppPaths.CoreDir);
        progress?.Report((0, "Buscando la última versión del motor…"));

        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/MetaCubeX/mihomo/releases/latest");
        request.Headers.UserAgent.ParseAdd("Misearch/0.1");
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "unknown";

        string? downloadUrl = null;
        string? digest = null;
        long? assetId = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.StartsWith("mihomo-windows-amd64", StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                continue;

            if (name.Contains("compatible", StringComparison.OrdinalIgnoreCase))
            {
                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
                assetId = asset.TryGetProperty("id", out var i) ? i.GetInt64() : null;
                break;
            }
            if (downloadUrl == null)
            {
                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
                assetId = asset.TryGetProperty("id", out var i) ? i.GetInt64() : null;
            }
        }

        if (downloadUrl == null)
            throw new InvalidOperationException("No se encontró un binario de Windows para Mihomo.");

        var tempZip = Path.Combine(Path.GetTempPath(), $"mihomo-{Guid.NewGuid():N}.zip");
        try
        {
            progress?.Report((0.05, "Descargando motor VPN (Mihomo)…"));
            await DownloadEngineAsync(downloadUrl, assetId, tempZip, progress, cancellationToken);

            if (!string.IsNullOrWhiteSpace(digest) && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                var expected = digest["sha256:".Length..];
                var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(tempZip)));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("La firma del motor VPN no coincide.");
            }

            progress?.Report((0.9, "Instalando motor VPN…"));
            using (var zip = ZipFile.OpenRead(tempZip))
            {
                var entry = zip.Entries.FirstOrDefault(e =>
                        e.Name.Equals("mihomo.exe", StringComparison.OrdinalIgnoreCase))
                    ?? zip.Entries.FirstOrDefault(e =>
                        e.Name.StartsWith("mihomo", StringComparison.OrdinalIgnoreCase) &&
                        e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("El ZIP no contiene el ejecutable de Mihomo.");
                entry.ExtractToFile(EnginePath, overwrite: true);
            }

            File.WriteAllText(Path.Combine(AppPaths.CoreDir, "version.txt"),
                $"{tag}{Environment.NewLine}https://github.com/MetaCubeX/mihomo{Environment.NewLine}");
            progress?.Report((1, $"Motor listo ({tag})."));
            StateChanged?.Invoke(this, EventArgs.Empty);
            return EnginePath;
        }
        finally
        {
            try { File.Delete(tempZip); } catch (Exception) { }
        }
    }

    private async Task DownloadEngineAsync(string downloadUrl, long? assetId, string targetPath,
        IProgress<(double Percent, string Message)>? progress, CancellationToken cancellationToken)
    {
        var sources = new List<(string Url, bool ViaApi)>();
        if (!string.IsNullOrWhiteSpace(downloadUrl))
            sources.Add((downloadUrl, false));
        if (assetId is { } id)
            sources.Add(($"https://api.github.com/repos/MetaCubeX/mihomo/releases/assets/{id}", true));

        Exception? lastError = null;
        for (var round = 0; round < 2; round++)
        {
            foreach (var (url, viaApi) in sources)
            {
                try
                {
                    await DownloadFromAsync(url, viaApi, targetPath, progress, cancellationToken);
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw;
                    lastError = ex;
                }
            }
            await Task.Delay(1000, cancellationToken);
        }

        throw lastError ?? new InvalidOperationException("No se pudo descargar el motor VPN.");
    }

    private async Task DownloadFromAsync(string url, bool viaApi, string targetPath,
        IProgress<(double Percent, string Message)>? progress, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Misearch/0.1");
        if (viaApi)
            request.Headers.Accept.ParseAdd("application/octet-stream");

        using var download = await _http.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!download.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"El servidor de descargas respondió {(int)download.StatusCode}.");

        var total = download.Content.Headers.ContentLength ?? 0;
        await using var source = await download.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(targetPath);
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;
            if (total > 0)
                progress?.Report((0.05 + 0.8 * copied / total, "Descargando motor VPN (Mihomo)…"));
        }
    }

    private static bool CanConnect(int port)
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync("127.0.0.1", port);
            return task.Wait(400) && client.Connected;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string Tail()
    {
        lock (_logGate)
        {
            var text = _stderr.ToString().Trim();
            return text.Length == 0 ? "" : "Detalle: " + text[^Math.Min(400, text.Length)..];
        }
    }

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }
}
