using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Misearch.Core.Models;
using Misearch.Core.Services;
using Misearch.Core.Vpn;

namespace Misearch.App;

public partial class MainWindow : Window
{
    private const string NewTabUrl = "https://misearch.app/newtab.html";
    private const string VirtualHost = "misearch.app";

    private readonly SettingsStore _settingsStore = new();
    private readonly HistoryStore _historyStore = new();
    private readonly TrackerBlocklist _blocklist = new();
    private readonly MihomoManager _mihomo = new();
    private readonly DispatcherTimer _toastTimer = new();

    private readonly List<TabItem> _tabs = new();
    private readonly Dictionary<string, string> _httpsAttempts = new(StringComparer.OrdinalIgnoreCase);

    // Always the very instance SettingsStore holds: mutating a copy meant every change
    // (engine, zoom, switches) was silently dropped when the app was closed.
    private AppSettings _settings = new();
    private CoreWebView2Environment? _environment;
    private TabItem? _activeTab;
    private bool _proxyActive;
    private int _proxyPort;
    private long _sessionBlocked;
    private bool _initializingSettings;
    private bool _vpnBusy;

    private sealed class TabItem
    {
        public WebView2 View { get; init; } = null!;
        public Border Header { get; set; } = null!;
        public TextBlock TitleText { get; set; } = null!;
        public TextBlock TabIcon { get; set; } = null!;
        public string Title { get; set; } = "Nueva pestaña";
        public string Url { get; set; } = NewTabUrl;
        public bool Loading { get; set; }
    }

    public MainWindow()
    {
        InitializeComponent();

        _toastTimer.Interval = TimeSpan.FromSeconds(3.5);
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast.Visibility = Visibility.Collapsed;
        };

        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
        PreviewKeyDown += OnWindowPreviewKeyDown;
        AddressBox.TextChanged += (_, _) => UpdateAddressPlaceholder();
    }

    private void LoadBrandImage()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "brand.png");
            if (File.Exists(path))
                BrandImage.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(path));
        }
        catch (Exception)
        {
        }
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        _settings = _settingsStore.Load();
        _settingsStore.Settings = _settings;
        _historyStore.Load();
        _blocklist.Load();
        LoadBrandImage();

        _mihomo.StateChanged += OnMihomoStateChanged;
        ApplySettingsToUi();
        AddNewTabButton();

        try
        {
            await InitializeEngineAsync();
            _environment = await CreateEnvironmentAsync(_proxyActive ? _proxyPort : 0);
            await CreateTabAsync(NewTabUrl);
        }
        catch (Exception ex)
        {
            ShowToast("No se pudo iniciar el navegador: " + ex.Message, error: true);
        }

        UpdateVpnUi();
        UpdateStatusBar();
        AddressBox.Focus();
    }

    private async Task InitializeEngineAsync()
    {
        if (!_mihomo.IsInstalled)
        {
            _proxyActive = false;
            return;
        }

        try
        {
            _proxyPort = FindFreePort();
            if (_settings.VpnEnabled && ProfileExists())
            {
                var configPath = BuildVpnConfig(_proxyPort);
                await _mihomo.StartAsync(_proxyPort, configPath, "vpn");
            }
            else
            {
                var configPath = MihomoManager.WriteConfig(MihomoConfigBuilder.BuildDirect(_proxyPort));
                await _mihomo.StartAsync(_proxyPort, configPath, "direct");
                if (_settings.VpnEnabled)
                {
                    _settings.VpnEnabled = false;
                    _settingsStore.Save();
                    ShowToast("No hay un perfil VPN válido. Se abrió sin VPN.", error: true);
                }
            }
            _proxyActive = true;
        }
        catch (Exception ex)
        {
            _proxyActive = false;
            _settings.VpnEnabled = false;
            _settingsStore.Save();
            ShowToast("Motor VPN no disponible: " + ex.Message, error: true);
        }
    }

    private static int FindFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task<CoreWebView2Environment> CreateEnvironmentAsync(int proxyPort)
    {
        var options = new CoreWebView2EnvironmentOptions();
        var args = new List<string>();
        if (proxyPort > 0)
            args.Add($"--proxy-server=\"http://127.0.0.1:{proxyPort}\"");
        var debugPort = Environment.GetEnvironmentVariable("MISEARCH_REMOTE_DEBUG");
        if (!string.IsNullOrEmpty(debugPort))
            args.Add($"--remote-debugging-port={debugPort}");
        if (args.Count > 0)
            options.AdditionalBrowserArguments = string.Join(" ", args);
        return await CoreWebView2Environment.CreateAsync(null, AppPaths.BrowserData, options);
    }

    private async Task<TabItem> CreateTabAsync(string url)
    {
        if (_environment == null)
            throw new InvalidOperationException("El navegador todavía no está listo.");

        var view = new WebView2
        {
            Visibility = Visibility.Collapsed,
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 7, 11, 20)
        };
        ViewsHost.Children.Add(view);

        var controllerOptions = _environment.CreateCoreWebView2ControllerOptions();
        controllerOptions.IsInPrivateModeEnabled = _settings.PrivateMode;

        await view.EnsureCoreWebView2Async(_environment, controllerOptions);
        var core = view.CoreWebView2;

        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = true;

        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        if (Directory.Exists(assets))
            core.SetVirtualHostNameToFolderMapping(VirtualHost, assets, CoreWebView2HostResourceAccessKind.DenyCors);

        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);

        var tab = new TabItem { View = view, Url = url };
        BuildTabHeader(tab);
        WireTabEvents(tab);
        _tabs.Add(tab);

        view.Source = new Uri(url);
        SelectTab(tab);
        return tab;
    }

    private void BuildTabHeader(TabItem tab)
    {
        var icon = new TextBlock
        {
            Text = "\uE774",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 10,
            Foreground = (Brush)FindResource("MutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var title = new TextBlock
        {
            Text = "Nueva pestaña",
            FontSize = 11.5,
            Width = 132,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var close = new Button
        {
            Content = "\uE711",
            Style = (Style)FindResource("IconButton"),
            Width = 20,
            Height = 20,
            FontSize = 8,
            Margin = new Thickness(4, 0, 0, 0)
        };
        close.Click += (_, args) =>
        {
            args.Handled = true;
            CloseTab(tab);
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(icon);
        content.Children.Add(title);
        content.Children.Add(close);

        var header = new Border
        {
            Height = 30,
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Padding = new Thickness(12, 0, 6, 0),
            Margin = new Thickness(0, 0, 4, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = content
        };
        header.MouseLeftButtonDown += (_, args) =>
        {
            SelectTab(tab);
            args.Handled = true;
        };

        tab.Header = header;
        tab.TitleText = title;
        tab.TabIcon = icon;
        TabsPanel.Children.Insert(Math.Max(0, TabsPanel.Children.Count - 1), header);
    }

    private void AddNewTabButton()
    {
        var button = new Button
        {
            Content = "\uE710",
            Style = (Style)FindResource("IconButton"),
            Width = 26,
            Height = 26,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Nueva pestaña (Ctrl+T)"
        };
        button.Click += (_, _) => _ = CreateTabAsync(NewTabUrl);
        TabsPanel.Children.Add(button);
    }

    private void WireTabEvents(TabItem tab)
    {
        var core = tab.View.CoreWebView2;

        core.NavigationStarting += (_, e) =>
        {
            tab.Url = e.Uri;
            tab.Loading = true;
            if (e.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                _settings.HttpsFirst &&
                e.IsUserInitiated &&
                !IsLocalOrPrivate(e.Uri))
            {
                e.Cancel = true;
                var https = "https://" + e.Uri[7..];
                _httpsAttempts[https] = e.Uri;
                core.Navigate(https);
                return;
            }
            if (ReferenceEquals(_activeTab, tab))
                UpdateAddressUi();
        };

        core.SourceChanged += (_, _) =>
        {
            tab.Url = core.Source;
            if (ReferenceEquals(_activeTab, tab))
                UpdateAddressUi();
        };

        core.NavigationCompleted += (_, e) =>
        {
            tab.Loading = false;
            if (!e.IsSuccess && _httpsAttempts.Remove(tab.Url, out var original))
            {
                ShowToast("Este sitio no ofrece HTTPS. Se abre sin cifrar.", error: true);
                core.Navigate(original);
                return;
            }

            if (e.IsSuccess)
            {
                var url = core.Source;
                if (!url.StartsWith(NewTabUrl, StringComparison.OrdinalIgnoreCase))
                    _historyStore.AddVisit(url, string.IsNullOrWhiteSpace(tab.Title) ? url : tab.Title);
                if (url.StartsWith(NewTabUrl, StringComparison.OrdinalIgnoreCase))
                    InjectNewTabEngine(tab);
            }

            if (ReferenceEquals(_activeTab, tab))
            {
                UpdateAddressUi();
                UpdateStatusBar();
            }
        };

        core.DocumentTitleChanged += (_, _) =>
        {
            tab.Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "Nueva pestaña" : core.DocumentTitle;
            tab.TitleText.Text = tab.Title;
            tab.Header.ToolTip = tab.Title;
            if (ReferenceEquals(_activeTab, tab))
                Title = tab.Title + " — Misearch";
        };

        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? _))
                _ = CreateTabAsync(e.Uri);
        };

        core.WebResourceRequested += (_, e) =>
        {
            if (!_settings.BlockTrackers)
                return;
            if (e.Request.Uri.StartsWith("https://" + VirtualHost, StringComparison.OrdinalIgnoreCase))
                return;
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri))
                return;
            if (!_blocklist.IsBlocked(uri.Host))
                return;

            e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            _sessionBlocked++;
            if (ReferenceEquals(_activeTab, tab))
                UpdateStatusBar();
        };

        core.PermissionRequested += (_, e) =>
        {
            var allow = e.PermissionKind == CoreWebView2PermissionKind.ClipboardRead;
            e.State = allow ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            if (!allow)
            {
                var kind = e.PermissionKind.ToString();
                Dispatcher.BeginInvoke(() => ShowToast($"Permiso bloqueado: {kind}", error: true));
            }
        };

        core.DownloadStarting += (_, e) => HandleDownloadStarting(tab, e);

        core.WebMessageReceived += (_, e) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "search" && root.TryGetProperty("q", out var q))
                {
                    var text = q.GetString() ?? "";
                    if (SearchRouter.TryResolve(text, _settings.SearchEngine, out var url))
                        core.Navigate(url);
                }
                else if (type == "navigate" && root.TryGetProperty("url", out var u))
                {
                    var target = u.GetString();
                    if (!string.IsNullOrWhiteSpace(target))
                        core.Navigate(target);
                }
                else if (type == "selection" && root.TryGetProperty("text", out var sel))
                {
                    OnAiSelectionReported(tab, sel.GetString() ?? "");
                }
                else if (type == "shortcut" && root.TryGetProperty("combo", out var c))
                {
                    HandleShortcut(tab, c.GetString() ?? "");
                }
            }
            catch (JsonException)
            {
            }
        };

        core.ProcessFailed += (_, e) =>
        {
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited)
                ShowToast("La página dejó de responder. Recarga para continuar.", error: true);
        };

        _ = core.AddScriptToExecuteOnDocumentCreatedAsync(ShortcutScript);
        _ = core.AddScriptToExecuteOnDocumentCreatedAsync(SelectionScript);
        tab.View.ZoomFactor = _settings.Zoom;
    }

    private void InjectNewTabEngine(TabItem tab)
    {
        var name = SearchRouter.EngineName(_settings.SearchEngine);
        _ = tab.View.CoreWebView2.ExecuteScriptAsync(
            $"if (window.misearchSetEngine) window.misearchSetEngine({JsonSerializer.Serialize(name)});");
    }

    private void SelectTab(TabItem tab)
    {
        _activeTab = tab;
        foreach (var item in _tabs)
        {
            item.View.Visibility = ReferenceEquals(item, tab) ? Visibility.Visible : Visibility.Collapsed;
            item.Header.Background = ReferenceEquals(item, tab)
                ? (Brush)FindResource("Surface2Brush")
                : Brushes.Transparent;
            item.TitleText.Foreground = ReferenceEquals(item, tab)
                ? (Brush)FindResource("TextBrush")
                : (Brush)FindResource("MutedBrush");
            item.TabIcon.Foreground = ReferenceEquals(item, tab)
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("MutedBrush");
        }

        Title = tab.Title + " — Misearch";
        UpdateAddressUi();
        UpdateStatusBar();
        if (tab.View.CoreWebView2 != null)
            tab.View.Focus();
    }

    private async void CloseTab(TabItem tab)
    {
        var index = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        TabsPanel.Children.Remove(tab.Header);
        ViewsHost.Children.Remove(tab.View);
        tab.View.Dispose();

        if (ReferenceEquals(_activeTab, tab))
        {
            _activeTab = null;
            if (_tabs.Count > 0)
                SelectTab(_tabs[Math.Clamp(index, 0, _tabs.Count - 1)]);
            else
                await CreateTabAsync(NewTabUrl);
        }
    }

    private void UpdateAddressUi()
    {
        var tab = _activeTab;
        if (tab == null)
            return;

        var url = tab.Url;
        if (url.StartsWith(NewTabUrl, StringComparison.OrdinalIgnoreCase))
        {
            AddressBox.Text = "";
            SecurityIcon.Text = "\uE72E";
            SecurityIcon.Foreground = (Brush)FindResource("GreenBrush");
        }
        else
        {
            if (!AddressBox.IsKeyboardFocusWithin)
                AddressBox.Text = url;
            UpdateSecurityIcon(url);
        }

        StarButton.Content = _historyStore.IsBookmarked(url) ? "\uE735" : "\uE734";
        UpdateAddressPlaceholder();

        if (tab.View.CoreWebView2 != null)
        {
            BackButton.IsEnabled = tab.View.CoreWebView2.CanGoBack;
            ForwardButton.IsEnabled = tab.View.CoreWebView2.CanGoForward;
        }
    }

    private void UpdateSecurityIcon(string url)
    {
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            SecurityIcon.Text = "\uE72E";
            SecurityIcon.Foreground = (Brush)FindResource("GreenBrush");
        }
        else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            SecurityIcon.Text = "\uE7BA";
            SecurityIcon.Foreground = (Brush)FindResource("AmberBrush");
        }
        else if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            SecurityIcon.Text = "\uE8A5";
            SecurityIcon.Foreground = (Brush)FindResource("AccentBrush");
        }
        else
        {
            SecurityIcon.Text = "\uE774";
            SecurityIcon.Foreground = (Brush)FindResource("MutedBrush");
        }
    }

    private void UpdateAddressPlaceholder()
    {
        AddressPlaceholder.Visibility = string.IsNullOrEmpty(AddressBox.Text) && !AddressBox.IsKeyboardFocusWithin
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateStatusBar()
    {
        StatusBlockedText.Text = _sessionBlocked == 1 ? "1 bloqueado" : $"{_sessionBlocked} bloqueados";

        var url = _activeTab?.Url ?? "";
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            StatusSecurityDot.Fill = (Brush)FindResource("GreenBrush");
            StatusSecurityText.Text = url.StartsWith("https://" + VirtualHost, StringComparison.OrdinalIgnoreCase)
                ? "Nueva pestaña"
                : "Conexión cifrada";
        }
        else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            StatusSecurityDot.Fill = (Brush)FindResource("AmberBrush");
            StatusSecurityText.Text = "Conexión sin cifrar";
        }
        else
        {
            StatusSecurityDot.Fill = (Brush)FindResource("MutedBrush");
            StatusSecurityText.Text = "Listo";
        }

        HomeProtectionText.Text = _settings.BlockTrackers
            ? $"Bloqueo activo con {_blocklist.Count} rastreadores en la lista. {_sessionBlocked} bloqueados en esta sesión."
            : "El bloqueo de rastreadores está desactivado en Ajustes.";
    }

    private void NavigateActive(string url)
    {
        var tab = _activeTab;
        if (tab?.View.CoreWebView2 == null)
            return;
        tab.View.CoreWebView2.Navigate(url);
    }

    private static bool IsLocalOrPrivate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!System.Net.IPAddress.TryParse(uri.Host, out var ip))
            return false;
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4)
            return false;
        return bytes[0] == 127 || bytes[0] == 10 ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31);
    }

    private void HandleShortcut(TabItem tab, string combo)
    {
        switch (combo)
        {
            case "ctrl+t": _ = CreateTabAsync(NewTabUrl); break;
            case "ctrl+w": CloseTab(tab); break;
            case "ctrl+l": FocusAddress(); break;
            case "ctrl+o": OpenPdfDialog(); break;
            case "ctrl+p": PrintCurrent(); break;
            case "ctrl+r": tab.View.CoreWebView2?.Reload(); break;
            case "ctrl+shift+delete": _ = ClearDataAsync(); break;
            case "ctrl++": ChangeZoom(0.1); break;
            case "ctrl+-": ChangeZoom(-0.1); break;
            case "ctrl+0": ChangeZoom(0, reset: true); break;
            case "alt+arrowleft": GoBack(tab); break;
            case "alt+arrowright": GoForward(tab); break;
        }
    }

    private const string ShortcutScript = """
        (() => {
          const known = new Set(['ctrl+t','ctrl+w','ctrl+l','ctrl+o','ctrl+p','ctrl+r','ctrl+shift+delete','ctrl++','ctrl+-','ctrl+0','alt+arrowleft','alt+arrowright']);
          window.addEventListener('keydown', (event) => {
            const parts = [];
            if (event.ctrlKey || event.metaKey) parts.push('ctrl');
            if (event.shiftKey) parts.push('shift');
            if (event.altKey) parts.push('alt');
            let key = (event.key || '').toLowerCase();
            if (key === '=') key = '+';
            if (key === 'add') key = '+';
            if (key === 'subtract') key = '-';
            parts.push(key);
            const combo = parts.join('+');
            if (!known.has(combo)) return;
            event.preventDefault();
            event.stopPropagation();
            if (window.chrome && window.chrome.webview)
              window.chrome.webview.postMessage({ type: 'shortcut', combo });
          }, true);
        })();
        """;

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.T) { _ = CreateTabAsync(NewTabUrl); e.Handled = true; }
            else if (e.Key == Key.L) { FocusAddress(); e.Handled = true; }
            else if (e.Key == Key.O) { OpenPdfDialog(); e.Handled = true; }
            else if (e.Key == Key.P) { PrintCurrent(); e.Handled = true; }
            else if (e.Key == Key.W) { if (_activeTab != null) CloseTab(_activeTab); e.Handled = true; }
            else if (e.Key == Key.R) { _activeTab?.View.CoreWebView2?.Reload(); e.Handled = true; }
            else if (e.Key is Key.OemPlus or Key.Add) { ChangeZoom(0.1); e.Handled = true; }
            else if (e.Key is Key.OemMinus or Key.Subtract) { ChangeZoom(-0.1); e.Handled = true; }
            else if (e.Key is Key.D0 or Key.NumPad0) { ChangeZoom(0, reset: true); e.Handled = true; }
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Delete)
        {
            _ = ClearDataAsync();
            e.Handled = true;
        }
    }

    private void ChangeZoom(double delta, bool reset = false)
    {
        _settings.Zoom = reset ? 1.0 : Math.Clamp(Math.Round(_settings.Zoom + delta, 1), 0.5, 2.0);
        foreach (var tab in _tabs)
            tab.View.ZoomFactor = _settings.Zoom;
        _settingsStore.Save();
        ShowToast($"Zoom {_settings.Zoom * 100:0} %");
    }

    private void FocusAddress()
    {
        Activate();
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    private void UpdateAddressFromTab()
    {
        if (_activeTab != null)
            UpdateAddressUi();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab != null) GoBack(_activeTab);
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab != null) GoForward(_activeTab);
    }

    private static void GoBack(TabItem tab)
    {
        if (tab.View.CoreWebView2?.CanGoBack == true)
            tab.View.CoreWebView2.GoBack();
    }

    private static void GoForward(TabItem tab)
    {
        if (tab.View.CoreWebView2?.CanGoForward == true)
            tab.View.CoreWebView2.GoForward();
    }

    private void Reload_Click(object sender, RoutedEventArgs e) =>
        _activeTab?.View.CoreWebView2?.Reload();

    private void Home_Click(object sender, RoutedEventArgs e) => _ = CreateTabAsync(NewTabUrl);

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        if (!SearchRouter.TryResolve(AddressBox.Text, _settings.SearchEngine, out var url))
            return;
        NavigateActive(url);
        Keyboard.ClearFocus();
        _activeTab?.View.Focus();
        e.Handled = true;
    }

    private void AddressBox_GotFocus(object sender, RoutedEventArgs e)
    {
        UpdateAddressPlaceholder();
        AddressBox.SelectAll();
    }

    private void AddressBox_LostFocus(object sender, RoutedEventArgs e)
    {
        UpdateAddressPlaceholder();
        UpdateAddressFromTab();
    }

    private void Star_Click(object sender, RoutedEventArgs e)
    {
        var tab = _activeTab;
        if (tab == null || tab.Url.StartsWith(NewTabUrl, StringComparison.OrdinalIgnoreCase))
            return;
        var title = string.IsNullOrWhiteSpace(tab.Title) ? tab.Url : tab.Title;
        _historyStore.ToggleBookmark(tab.Url, title);
        var saved = _historyStore.IsBookmarked(tab.Url);
        StarButton.Content = saved ? "\uE735" : "\uE734";
        ShowToast(saved ? "Añadido a marcadores" : "Eliminado de marcadores");
        RefreshBookmarksPanel();
    }

    private void OnMihomoStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateVpnUi();
            if (_mihomo.LastError != null && _settings.VpnEnabled && !_mihomo.IsRunning)
                ShowToast("VPN caída: la navegación queda bloqueada (kill switch).", error: true);
        });
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _settingsStore.Save();
        _mihomo.Stop();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        foreach (var tab in _tabs)
        {
            try { tab.View.Dispose(); } catch (Exception) { }
        }
        _tabs.Clear();

        if (_settings.ClearOnExit)
        {
            try
            {
                if (Directory.Exists(AppPaths.BrowserData))
                    Directory.Delete(AppPaths.BrowserData, recursive: true);
            }
            catch (Exception)
            {
            }
        }
    }
}
