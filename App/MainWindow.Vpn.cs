using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Misearch.Core.Models;
using Misearch.Core.Vpn;

namespace Misearch.App;

public partial class MainWindow
{
    private bool ProfileExists() =>
        !string.IsNullOrWhiteSpace(_settings.VpnProfilePath) &&
        File.Exists(_settings.VpnProfilePath) &&
        !string.IsNullOrWhiteSpace(_settings.VpnProfileKind);

    private string BuildVpnConfig(int port)
    {
        if (!ProfileExists())
            throw new InvalidOperationException("No hay un perfil VPN importado.");

        var text = File.ReadAllText(_settings.VpnProfilePath);
        var yaml = _settings.VpnProfileKind switch
        {
            "wireguard" => MihomoConfigBuilder.BuildWireGuard(WireGuardProfile.Parse(text), port),
            "clash" => MihomoConfigBuilder.BuildFromClash(text, port),
            _ => throw new InvalidOperationException("Tipo de perfil desconocido.")
        };
        return MihomoManager.WriteConfig(yaml);
    }

    private void UpdateVpnUi()
    {
        var installed = _mihomo.IsInstalled;
        var running = _mihomo.IsRunning;
        var vpnActive = _settings.VpnEnabled && running && _mihomo.Mode == "vpn";
        var vpnFailed = _settings.VpnEnabled && !running;

        if (vpnActive)
        {
            VpnStateText.Text = "VPN activa";
            VpnStateDot.Fill = (Brush)FindResource("GreenBrush");
            VpnDetailText.Text = $"Túnel activo. Todo el tráfico del navegador pasa por 127.0.0.1:{_proxyPort}.";
            VpnPillText.Text = "VPN activa";
            VpnDot.Fill = (Brush)FindResource("GreenBrush");
            StatusVpnText.Text = "VPN activa";
        }
        else if (vpnFailed)
        {
            VpnStateText.Text = "VPN caída";
            VpnStateDot.Fill = (Brush)FindResource("RedBrush");
            VpnDetailText.Text = "El motor no está en marcha y el navegador no tiene salida directa (kill switch). Pulsa Desconectar para volver a navegar.";
            VpnPillText.Text = "VPN caída";
            VpnDot.Fill = (Brush)FindResource("RedBrush");
            StatusVpnText.Text = "VPN caída · tráfico bloqueado";
        }
        else if (running)
        {
            VpnStateText.Text = "Sin VPN";
            VpnStateDot.Fill = (Brush)FindResource("AmberBrush");
            VpnDetailText.Text = installed
                ? "El motor está en modo directo. Activa la VPN para cifrar el tráfico y ocultar tu IP."
                : "Sin motor VPN. El navegador conecta directamente.";
            VpnPillText.Text = "Sin VPN";
            VpnDot.Fill = (Brush)FindResource("AmberBrush");
            StatusVpnText.Text = "Sin VPN";
        }
        else
        {
            VpnStateText.Text = installed ? "VPN detenida" : "VPN no configurada";
            VpnStateDot.Fill = (Brush)FindResource("MutedBrush");
            VpnDetailText.Text = installed
                ? "El motor está instalado pero no se pudo iniciar."
                : "Descarga el motor Mihomo (código abierto) para activar la VPN integrada.";
            VpnPillText.Text = "VPN";
            VpnDot.Fill = (Brush)FindResource("MutedBrush");
            StatusVpnText.Text = "VPN no disponible";
        }

        VpnProfileText.Text = ProfileExists()
            ? $"Perfil {( _settings.VpnProfileKind == "wireguard" ? "WireGuard" : "Clash/Mihomo")} · {Path.GetFileName(_settings.VpnProfilePath)}"
            : "No hay perfil importado. Puedes importar un .conf de WireGuard o un .yaml de Clash/Mihomo.";

        VpnActionButton.Content = vpnActive ? "Desconectar VPN" : "Activar VPN";
        VpnActionButton.IsEnabled = !_vpnBusy;
        DownloadEngineButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;

        if (installed)
        {
            var version = "";
            try
            {
                var path = Path.Combine(AppPaths.CoreDir, "version.txt");
                if (File.Exists(path))
                    version = " · " + File.ReadAllLines(path).FirstOrDefault();
            }
            catch (Exception)
            {
            }
            EngineText.Text = "Mihomo instalado" + version + ".";
        }
        else
        {
            EngineText.Text = "Mihomo no está instalado. Ocupa unos 20 MB y se descarga desde GitHub.";
        }
    }

    private async void VpnAction_Click(object sender, RoutedEventArgs e)
    {
        if (_vpnBusy)
            return;
        OpenVpnPanel();

        if (_settings.VpnEnabled)
            await DisableVpnAsync();
        else
            await EnableVpnAsync();
    }

    private async Task EnableVpnAsync()
    {
        if (!ProfileExists())
        {
            ShowToast("Importa primero un perfil WireGuard o Clash.", error: true);
            OpenVpnPanel();
            return;
        }

        _vpnBusy = true;
        UpdateVpnUi();
        try
        {
            if (!_mihomo.IsInstalled)
            {
                var ok = await DownloadEngineCoreAsync();
                if (!ok)
                    return;
            }

            var port = _proxyActive ? _proxyPort : FindFreePort();
            var configPath = BuildVpnConfig(port);
            await _mihomo.StartAsync(port, configPath, "vpn");

            if (!_proxyActive)
                await SwitchEnvironmentAsync(port);

            _settings.VpnEnabled = true;
            _settings.ProxyPort = port;
            _settingsStore.Save();
            ShowToast("VPN activa. Todo el tráfico del navegador va por el túnel.");
        }
        catch (Exception ex)
        {
            _settings.VpnEnabled = false;
            _settingsStore.Save();
            await RestoreDirectAsync();
            ShowToast("No se pudo activar la VPN: " + ex.Message, error: true);
        }
        finally
        {
            _vpnBusy = false;
            UpdateVpnUi();
        }
    }

    private async Task DisableVpnAsync()
    {
        _vpnBusy = true;
        UpdateVpnUi();
        try
        {
            if (_mihomo.IsRunning)
            {
                var configPath = MihomoManager.WriteConfig(MihomoConfigBuilder.BuildDirect(_proxyPort));
                await _mihomo.StartAsync(_proxyPort, configPath, "direct");
            }
            _settings.VpnEnabled = false;
            _settingsStore.Save();
            ShowToast("VPN desactivada. La navegación vuelve a ser directa.");
        }
        catch (Exception ex)
        {
            ShowToast("No se pudo volver al modo directo: " + ex.Message, error: true);
        }
        finally
        {
            _vpnBusy = false;
            UpdateVpnUi();
        }
    }

    private async Task RestoreDirectAsync()
    {
        try
        {
            if (_mihomo.IsInstalled && _proxyActive)
            {
                var configPath = MihomoManager.WriteConfig(MihomoConfigBuilder.BuildDirect(_proxyPort));
                await _mihomo.StartAsync(_proxyPort, configPath, "direct");
            }
        }
        catch (Exception)
        {
        }
    }

    private async Task SwitchEnvironmentAsync(int port)
    {
        var urls = _tabs.Select(t => t.Url).ToList();

        foreach (var tab in _tabs.ToList())
        {
            ViewsHost.Children.Remove(tab.View);
            TabsPanel.Children.Remove(tab.Header);
            tab.View.Dispose();
        }
        _tabs.Clear();
        _activeTab = null;

        _proxyPort = port;
        _proxyActive = true;
        _environment = await CreateEnvironmentAsync(port);

        if (urls.Count == 0)
            urls.Add(NewTabUrl);
        foreach (var url in urls)
            await CreateTabAsync(url);
    }

    private async void DownloadEngine_Click(object sender, RoutedEventArgs e)
    {
        await DownloadEngineCoreAsync();
    }

    private async Task<bool> DownloadEngineCoreAsync()
    {
        DownloadEngineButton.IsEnabled = false;
        EngineProgressText.Text = "Preparando la descarga…";

        try
        {
            var progress = new Progress<(double Percent, string Message)>(p =>
                EngineProgressText.Text = $"{p.Message} {(int)Math.Round(p.Percent * 100)}%");

            await _mihomo.EnsureEngineAsync(progress);
            EngineProgressText.Text = "Motor instalado y listo.";
            ShowToast("Motor VPN instalado.");

            if (_settings.VpnEnabled)
                await EnableVpnAsync();
            return true;
        }
        catch (Exception ex)
        {
            EngineProgressText.Text = "Error: " + ex.Message;
            ShowToast("No se pudo descargar el motor: " + ex.Message, error: true);
            return false;
        }
        finally
        {
            DownloadEngineButton.IsEnabled = true;
            UpdateVpnUi();
        }
    }

    private async void ImportWireGuard_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importar perfil WireGuard",
            Filter = "WireGuard (*.conf)|*.conf|Todos los archivos (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var text = File.ReadAllText(dialog.FileName);
            _ = WireGuardProfile.Parse(text);

            AppPaths.EnsureDirectories();
            var target = Path.Combine(AppPaths.VpnDir, "profile.conf");
            File.Copy(dialog.FileName, target, overwrite: true);

            _settings.VpnProfilePath = target;
            _settings.VpnProfileKind = "wireguard";
            _settingsStore.Save();

            ShowToast("Perfil WireGuard importado.");
            await ApplyProfileIfConnected();
        }
        catch (Exception ex)
        {
            ShowToast("Perfil WireGuard no válido: " + ex.Message, error: true);
        }
        finally
        {
            UpdateVpnUi();
        }
    }

    private async void ImportClash_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importar perfil Clash / Mihomo",
            Filter = "Clash / Mihomo (*.yaml;*.yml)|*.yaml;*.yml|Todos los archivos (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var text = File.ReadAllText(dialog.FileName);
            _ = MihomoConfigBuilder.BuildFromClash(text, 7890);

            AppPaths.EnsureDirectories();
            var target = Path.Combine(AppPaths.VpnDir, "profile.yaml");
            File.Copy(dialog.FileName, target, overwrite: true);

            _settings.VpnProfilePath = target;
            _settings.VpnProfileKind = "clash";
            _settingsStore.Save();

            ShowToast("Perfil Clash importado.");
            await ApplyProfileIfConnected();
        }
        catch (Exception ex)
        {
            ShowToast("Perfil Clash no válido: " + ex.Message, error: true);
        }
        finally
        {
            UpdateVpnUi();
        }
    }

    private async Task ApplyProfileIfConnected()
    {
        if (!_settings.VpnEnabled)
            return;

        try
        {
            if (!_mihomo.IsInstalled)
                return;
            var configPath = BuildVpnConfig(_proxyPort);
            await _mihomo.StartAsync(_proxyPort, configPath, "vpn");
            ShowToast("Perfil aplicado a la VPN activa.");
        }
        catch (Exception ex)
        {
            _settings.VpnEnabled = false;
            _settingsStore.Save();
            await RestoreDirectAsync();
            ShowToast("El perfil nuevo no funciona; se desactivó la VPN: " + ex.Message, error: true);
        }
    }
}
