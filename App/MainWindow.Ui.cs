using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Misearch.Core.Models;
using Misearch.Core.Services;
using Microsoft.Web.WebView2.Core;

namespace Misearch.App;

public partial class MainWindow
{
    private void ShowPanel(UIElement content, string title, string icon)
    {
        PanelHome.Visibility = Visibility.Collapsed;
        PanelSettings.Visibility = Visibility.Collapsed;
        PanelVpn.Visibility = Visibility.Collapsed;
        PanelHistory.Visibility = Visibility.Collapsed;
        PanelBookmarks.Visibility = Visibility.Collapsed;
        PanelDownloads.Visibility = Visibility.Collapsed;

        content.Visibility = Visibility.Visible;
        PanelTitleText.Text = title;
        PanelIcon.Text = icon;
        SidePanel.Visibility = Visibility.Visible;

        // El WebView2 es una superficie nativa que siempre se dibuja por encima de WPF.
        // Reducimos el área del navegador para que el panel lateral quede visible y clicable.
        ViewsHost.Margin = new Thickness(0, 0, SidePanel.Width + 20, 0);
    }

    private void ClosePanel()
    {
        SidePanel.Visibility = Visibility.Collapsed;
        ViewsHost.Margin = new Thickness(0);
    }

    private void PanelClose_Click(object sender, RoutedEventArgs e) => ClosePanel();

    private void Menu_Click(object sender, RoutedEventArgs e) =>
        ShowPanel(PanelHome, "Menú", "\uE712");

    private void Shield_Click(object sender, RoutedEventArgs e) =>
        ShowPanel(PanelSettings, "Privacidad", "\uEA18");

    private void VpnPill_Click(object sender, RoutedEventArgs e) => OpenVpnPanel();

    private void OpenVpn_Click(object sender, RoutedEventArgs e) => OpenVpnPanel();

    private void OpenVpnPanel() => ShowPanel(PanelVpn, "VPN", "\uEA18");

    private void OpenSettings_Click(object sender, RoutedEventArgs e) =>
        ShowPanel(PanelSettings, "Ajustes", "\uE713");

    private void OpenHistory_Click(object sender, RoutedEventArgs e)
    {
        RefreshHistoryPanel();
        ShowPanel(PanelHistory, "Historial", "\uE81C");
        HistorySearchBox.Focus();
    }

    private void OpenBookmarks_Click(object sender, RoutedEventArgs e)
    {
        RefreshBookmarksPanel();
        ShowPanel(PanelBookmarks, "Marcadores", "\uE734");
    }

    private void OpenDownloads_Click(object sender, RoutedEventArgs e)
    {
        UpdateDownloadsPanel();
        ShowPanel(PanelDownloads, "Descargas", "\uE896");
    }

    private void NewTab_Click(object sender, RoutedEventArgs e) => _ = CreateTabAsync(NewTabUrl);

    private void ShowToast(string message, bool error = false)
    {
        ToastText.Text = message;
        ToastText.Foreground = error ? (Brush)FindResource("RedBrush") : (Brush)FindResource("TextBrush");
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void ApplySettingsToUi()
    {
        _initializingSettings = true;

        BlockTrackersCheck.IsChecked = _settings.BlockTrackers;
        PrivateModeCheck.IsChecked = _settings.PrivateMode;
        ClearExitCheck.IsChecked = _settings.ClearOnExit;
        HttpsFirstCheck.IsChecked = _settings.HttpsFirst;

        _initializingSettings = false;

        PrivateHint.Text = "El modo privado y el borrado al salir se aplican al reiniciar Misearch.";
        EngineHint.Text = $"Las búsquedas se envían a {SearchRouter.EngineName(_settings.SearchEngine)}.";
        UpdateEngineChips();
    }

    private void UpdateEngineChips()
    {
        foreach (var child in EngineChips.Children)
        {
            if (child is not Button button || button.Tag is not string tag)
                continue;
            var isOn = Enum.TryParse<SearchEngine>(tag, out var engine) && engine == _settings.SearchEngine;
            button.Background = isOn ? new SolidColorBrush(Color.FromRgb(30, 58, 95)) : Brushes.Transparent;
            button.BorderBrush = isOn ? new SolidColorBrush(Color.FromRgb(47, 111, 184)) : (Brush)FindResource("BorderBrush2");
            button.Foreground = isOn ? new SolidColorBrush(Color.FromRgb(191, 224, 255)) : (Brush)FindResource("MutedBrush");
        }
    }

    private void Engine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse<SearchEngine>(tag, out var engine))
            return;

        _settings.SearchEngine = engine;
        _settingsStore.Save();
        UpdateEngineChips();
        EngineHint.Text = $"Las búsquedas se envían a {SearchRouter.EngineName(engine)}.";

        foreach (var tab in _tabs)
        {
            if (tab.Url.StartsWith(NewTabUrl, StringComparison.OrdinalIgnoreCase) && tab.View.CoreWebView2 != null)
                InjectNewTabEngine(tab);
        }
        ShowToast($"Buscador: {SearchRouter.EngineName(engine)}");
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings)
            return;

        _settings.BlockTrackers = BlockTrackersCheck.IsChecked == true;
        _settings.PrivateMode = PrivateModeCheck.IsChecked == true;
        _settings.ClearOnExit = ClearExitCheck.IsChecked == true;
        _settings.HttpsFirst = HttpsFirstCheck.IsChecked == true;
        _settingsStore.Save();
        UpdateStatusBar();
    }

    private async void ClearData_Click(object sender, RoutedEventArgs e) => await ClearDataAsync();

    private async Task ClearDataAsync()
    {
        if (MessageBox.Show(this,
                "Se borrarán el historial, los marcadores y los datos de navegación. ¿Continuar?",
                "Misearch", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _historyStore.ClearAll();
        try
        {
            var core = _activeTab?.View.CoreWebView2;
            if (core != null)
                await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
        }
        catch (Exception)
        {
        }

        RefreshHistoryPanel();
        RefreshBookmarksPanel();
        StarButton.Content = "\uE734";
        ShowToast("Datos de navegación borrados.");
    }

    private void OpenDownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = _settings.DownloadFolder;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void RefreshHistoryPanel(string? filter = null)
    {
        HistoryList.Items.Clear();
        var entries = _historyStore.SearchHistory(filter ?? "");

        foreach (var entry in entries)
        {
            var title = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(entry.Title) ? entry.Url : entry.Title,
                FontSize = 12.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var sub = new TextBlock
            {
                Text = $"{entry.Url} · {entry.VisitedAt.LocalDateTime:dd/MM/yyyy HH:mm}",
                Style = (Style)FindResource("MutedText"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var stack = new StackPanel();
            stack.Children.Add(title);
            stack.Children.Add(sub);

            HistoryList.Items.Add(new ListBoxItem { Tag = entry.Url, Content = stack });
        }
    }

    private void HistorySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (PanelHistory.Visibility == Visibility.Visible)
            RefreshHistoryPanel(HistorySearchBox.Text);
    }

    private void HistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryList.SelectedItem is ListBoxItem { Tag: string url })
        {
            NavigateActive(url);
            ClosePanel();
        }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _historyStore.ClearHistory();
        RefreshHistoryPanel();
        ShowToast("Historial borrado.");
    }

    private void RefreshBookmarksPanel()
    {
        BookmarksList.Items.Clear();
        var items = _historyStore.Bookmarks;
        BookmarksEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var bookmark in items)
        {
            var title = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(bookmark.Title) ? bookmark.Url : bookmark.Title,
                FontSize = 12.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var sub = new TextBlock
            {
                Text = bookmark.Url,
                Style = (Style)FindResource("MutedText"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var stack = new StackPanel();
            stack.Children.Add(title);
            stack.Children.Add(sub);

            BookmarksList.Items.Add(new ListBoxItem { Tag = bookmark.Url, Content = stack });
        }
    }

    private void BookmarksList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BookmarksList.SelectedItem is ListBoxItem { Tag: string url })
        {
            NavigateActive(url);
            ClosePanel();
        }
    }

    private void UpdateDownloadsPanel()
    {
        DownloadsEmpty.Visibility = DownloadsList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HandleDownloadStarting(TabItem tab, CoreWebView2DownloadStartingEventArgs e)
    {
        var folder = _settings.DownloadFolder;
        Directory.CreateDirectory(folder);

        var name = SanitizeFileName(GetDownloadName(e.DownloadOperation));
        var path = UniquePath(folder, name);

        e.ResultFilePath = path;
        e.Handled = true;

        var title = new TextBlock { Text = name, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
        var detail = new TextBlock
        {
            Text = "Iniciando…",
            Style = (Style)FindResource("MutedText"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(detail);

        var item = new ListBoxItem { Tag = path, Content = stack };
        DownloadsList.Items.Insert(0, item);
        DownloadsEmpty.Visibility = Visibility.Collapsed;

        var operation = e.DownloadOperation;
        void Refresh()
        {
            var total = (long)(operation.TotalBytesToReceive ?? 0);
            var state = operation.State switch
            {
                CoreWebView2DownloadState.InProgress =>
                    total > 0
                        ? $"{operation.BytesReceived * 100 / total} % · {FormatSize(operation.BytesReceived)}"
                        : FormatSize(operation.BytesReceived),
                CoreWebView2DownloadState.Completed => "Completado",
                CoreWebView2DownloadState.Interrupted => "Interrumpido",
                _ => ""
            };

            Dispatcher.BeginInvoke(() =>
            {
                detail.Text = state;
                if (operation.State == CoreWebView2DownloadState.Completed)
                {
                    title.Foreground = (Brush)FindResource("GreenBrush");
                    ShowToast($"Descargado: {name}");
                }
                else if (operation.State == CoreWebView2DownloadState.Interrupted)
                {
                    title.Foreground = (Brush)FindResource("RedBrush");
                }
            });
        }

        operation.BytesReceivedChanged += (_, _) => Refresh();
        operation.StateChanged += (_, _) => Refresh();
        DownloadsList.MouseDoubleClick -= DownloadsList_MouseDoubleClick;
        DownloadsList.MouseDoubleClick += DownloadsList_MouseDoubleClick;
        Refresh();
    }

    private void DownloadsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DownloadsList.SelectedItem is ListBoxItem { Tag: string path })
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
                OpenDownloadFolder_Click(sender, e);
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return $"{value:0.#} {units[index]}";
    }

    private static string GetDownloadName(CoreWebView2DownloadOperation operation)
    {
        var disposition = operation.ContentDisposition ?? "";
        var match = System.Text.RegularExpressions.Regex.Match(disposition,
            "filename\\*?=(?:UTF-8'')?\"?([^\";]+)\"?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success)
        {
            try { return Uri.UnescapeDataString(match.Groups[1].Value.Trim()); }
            catch (Exception) { return match.Groups[1].Value.Trim(); }
        }

        if (Uri.TryCreate(operation.Uri, UriKind.Absolute, out var uri))
        {
            var fromPath = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(fromPath))
                return fromPath;
            return uri.Host;
        }

        return "descarga";
    }

    private static string SanitizeFileName(string name)
    {
        var cleaned = string.IsNullOrWhiteSpace(name) ? "descarga" : name;
        foreach (var invalid in Path.GetInvalidFileNameChars())
            cleaned = cleaned.Replace(invalid, '_');
        return cleaned;
    }

    private static string UniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path))
            return path;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 1; i < 10000; i++)
        {
            var candidate = Path.Combine(folder, $"{name} ({i}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
        return path;
    }

    private void OpenMihomoSource_Click(object sender, MouseButtonEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://github.com/MetaCubeX/mihomo") { UseShellExecute = true });

    private void OpenMsoft_Click(object sender, MouseButtonEventArgs e) =>
        NavigateActive("https://msoft.com.es");

    private void OpenPdf_Click(object sender, RoutedEventArgs e) => OpenPdfDialog();

    private void Print_Click(object sender, RoutedEventArgs e) => PrintCurrent();

    private void PrintCurrent()
    {
        var core = _activeTab?.View.CoreWebView2;
        if (core == null)
        {
            ShowToast("No hay ninguna página para imprimir.", error: true);
            return;
        }
        try
        {
            core.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
        }
        catch (Exception ex)
        {
            ShowToast("No se pudo abrir la impresión: " + ex.Message, error: true);
        }
    }

    private void OpenPdfDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Abrir PDF",
            Filter = "Documentos PDF (*.pdf)|*.pdf|Todos los archivos (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
            OpenPdfFile(dialog.FileName);
    }

    private void OpenPdfFile(string path)
    {
        try
        {
            var url = new Uri(path).AbsoluteUri;
            if (_activeTab != null && _activeTab.Url.StartsWith(NewTabUrl, StringComparison.OrdinalIgnoreCase))
                NavigateActive(url);
            else
                _ = CreateTabAsync(url);
        }
        catch (Exception ex)
        {
            ShowToast("No se pudo abrir el PDF: " + ex.Message, error: true);
        }
    }
}
