using System.Net;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Misearch.Core.Ai;

namespace Misearch.App;

public partial class MainWindow
{
    private const int MaxSelectionChars = 6000;
    private readonly OpenAiCompatibleClient _aiClient = new();
    private bool _aiBusy;

    /// <summary>Script that reports the current selection to the AI panel.</summary>
    private const string SelectionScript = """
        (() => {
          if (window.__misearchSelection) return;
          window.__misearchSelection = true;
          const send = () => {
            const text = String(window.getSelection ? window.getSelection().toString() : '').trim();
            if (window.chrome && window.chrome.webview)
              window.chrome.webview.postMessage({ type: 'selection', text: text });
          };
          document.addEventListener('mouseup', () => setTimeout(send, 10), true);
          document.addEventListener('keyup', () => setTimeout(send, 10), true);
        })();
        """;

    private AiSettingsResolver Ai => new(_settings);

    private void OnAiSelectionReported(TabItem tab, string text)
    {
        if (!ReferenceEquals(_activeTab, tab))
            return;
        _currentSelection = text.Length > MaxSelectionChars ? text[..MaxSelectionChars] : text;
        UpdateAiUi();
    }

    private string _currentSelection = "";

    private void AiButton_Click(object sender, RoutedEventArgs e) => OpenAiPanel();

    private void OpenAiPanel()
    {
        ShowPanel(PanelAi, "Asistente IA", "\uE99A");
        UpdateAiUi();
    }

    private void UpdateAiUi()
    {
        var resolver = Ai;
        var usable = resolver.IsUsable;

        // La traducción va con el modelo más capaz del proveedor, así que el panel lo dice.
        var translateModel = resolver.TranslationModel;
        AiProviderText.Text = translateModel == resolver.Model
            ? $"{resolver.Provider.Name} · {resolver.Model}"
            : $"{resolver.Provider.Name} · {resolver.Model} (traduce: {translateModel})";
        AiKeyHint.Text = usable
            ? "Listo. El texto de la página se envía al proveedor solo cuando pulsas un botón."
            : resolver.UnavailableReason();
        AiKeyHint.Foreground = usable
            ? (Brush)FindResource("MutedBrush")
            : (Brush)FindResource("AmberBrush");

        AiSummarizeButton.IsEnabled = usable && !_aiBusy;
        AiExplainButton.IsEnabled = usable && !_aiBusy && _currentSelection.Length > 0;
        AiExplainButton.Content = _currentSelection.Length > 0
            ? "Explicar la selección"
            : "Explicar la selección (nada seleccionado)";
        AiTranslateButton.IsEnabled = usable && !_aiBusy;
        AiTranslateButton.Content = _currentSelection.Length > 0
            ? "Traducir al español"
            : "Traducir la página al español";

        if (!_aiBusy && AiOutput.Document.Blocks.Count == 0)
            SetAiOutput(usable
                ? "Pulsa «Resumir esta página» o selecciona un texto y pulsa «Explicar la selección»."
                : resolver.UnavailableReason(), muted: true);
    }

    private void SetAiOutput(string text, bool muted = false)
    {
        AiOutput.Document.Blocks.Clear();
        AiOutput.Document.Blocks.Add(new Paragraph(new Run(text))
        {
            FontSize = 12.5,
            LineHeight = 19,
            Foreground = muted ? (Brush)FindResource("MutedBrush") : (Brush)FindResource("TextBrush")
        });
        AiOutput.ScrollToHome();
    }

    private async void AiSummarize_Click(object sender, RoutedEventArgs e) =>
        await RunAiAsync(AiTask.Summarize);

    private async void AiExplain_Click(object sender, RoutedEventArgs e) =>
        await RunAiAsync(AiTask.Explain);

    private async void AiTranslate_Click(object sender, RoutedEventArgs e) =>
        await RunAiAsync(AiTask.Translate);

    private async Task RunAiAsync(AiTask action)
    {
        if (_aiBusy)
            return;

        var core = _activeTab?.View.CoreWebView2;
        if (core == null)
        {
            ShowToast("No hay ninguna página abierta.", error: true);
            return;
        }

        var resolver = Ai;
        if (!resolver.IsUsable)
        {
            SetAiOutput(resolver.UnavailableReason(), muted: true);
            return;
        }

        _aiBusy = true;
        UpdateAiUi();
        SetAiOutput("Pensando…", muted: true);

        try
        {
            var title = _activeTab?.Title ?? "";
            string prompt;
            string system;
            int maxTokens;
            switch (action)
            {
                case AiTask.Summarize:
                    var text = await ReadPageTextAsync(core);
                    if (string.IsNullOrWhiteSpace(text))
                        throw new AiException("Esta página no tiene texto que resumir.");
                    prompt = AiPrompts.SummaryPrompt(title, _activeTab?.Url ?? "", text);
                    system = AiPrompts.SummarySystem;
                    maxTokens = 700;
                    break;

                case AiTask.Explain:
                    if (string.IsNullOrWhiteSpace(_currentSelection))
                        throw new AiException("Selecciona primero un texto en la página.");
                    prompt = AiPrompts.ExplainPrompt(_currentSelection, title);
                    system = AiPrompts.ExplainSystem;
                    maxTokens = 400;
                    break;

                default:
                    // A selection is translated on its own; without one, the whole page.
                    var source = _currentSelection;
                    if (string.IsNullOrWhiteSpace(source))
                    {
                        source = await ReadPageTextAsync(core);
                        if (string.IsNullOrWhiteSpace(source))
                            throw new AiException("Esta página no tiene texto que traducir.");
                    }
                    prompt = AiPrompts.TranslatePrompt(source);
                    system = AiPrompts.TranslateSystem;
                    maxTokens = 1200;
                    break;
            }

            var request = resolver.BuildRequest(new[]
            {
                AiMessage.System(system),
                AiMessage.User(prompt)
            }, maxTokens: maxTokens, task: action);

            var result = await _aiClient.CompleteAsync(request);
            SetAiOutput(result.Text);
        }
        catch (AiException ex)
        {
            SetAiOutput(ex.Message, muted: true);
            if (ex.StatusCode == HttpStatusCode.TooManyRequests)
                ShowToast("Límite del plan gratuito alcanzado.", error: true);
        }
        catch (Exception ex)
        {
            SetAiOutput("No se pudo usar la IA: " + ex.Message, muted: true);
        }
        finally
        {
            _aiBusy = false;
            UpdateAiUi();
        }
    }

    private static async Task<string> ReadPageTextAsync(Microsoft.Web.WebView2.Core.CoreWebView2 core)
    {
        const string script = """
            (() => {
              const root = document.querySelector('article') || document.querySelector('main') || document.body;
              return root ? String(root.innerText || '') : '';
            })();
            """;
        var json = await core.ExecuteScriptAsync(script);
        try
        {
            return JsonSerializer.Deserialize<string>(json) ?? "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private void CopyAiOutput_Click(object sender, RoutedEventArgs e)
    {
        var text = new TextRange(AiOutput.Document.ContentStart, AiOutput.Document.ContentEnd).Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;
        try
        {
            Clipboard.SetText(text);
            ShowToast("Respuesta copiada.");
        }
        catch (Exception ex)
        {
            ShowToast("No se pudo copiar: " + ex.Message, error: true);
        }
    }

    // ---------- Ajustes de IA ----------

    private void AiSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings)
            return;
        _settings.AiEnabled = AiEnabledCheck.IsChecked == true;
        _settingsStore.Save();
        UpdateAiUi();
    }

    private void AiProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse<AiProviderKind>(tag, out var kind))
            return;

        _settings.AiProvider = kind;
        _settings.AiModel = "";
        _settings.AiBaseUrl = "";
        _settingsStore.Save();
        ApplyAiSettingsToUi();
        UpdateAiUi();
    }

    private void AiSaveKey_Click(object sender, RoutedEventArgs e)
    {
        var key = AiKeyBox.Password.Trim();
        if (key.Length == 0)
        {
            _settings.AiApiKeyProtected = "";
            _settings.AiEnabled = false;
        }
        else
        {
            _settings.AiApiKeyProtected = AiKeyStore.Protect(key);
            _settings.AiEnabled = true;
        }

        AiKeyBox.Clear();
        _settingsStore.Save();
        ApplyAiSettingsToUi();
        UpdateAiUi();
        ShowToast(key.Length == 0 ? "Clave de IA borrada." : "Clave de IA guardada (cifrada).");
    }

    private void AiTest_Click(object sender, RoutedEventArgs e) => _ = TestAiAsync();

    private async Task TestAiAsync()
    {
        var resolver = Ai;
        if (!resolver.IsUsable)
        {
            SetAiOutput(resolver.UnavailableReason(), muted: true);
            return;
        }

        AiTestButton.IsEnabled = false;
        SetAiOutput("Probando la conexión…", muted: true);
        try
        {
            var request = resolver.BuildRequest(new[]
            {
                AiMessage.User("Responde solo con la palabra: listo")
            }, maxTokens: 16);
            var result = await _aiClient.CompleteAsync(request);
            SetAiOutput($"Conexión correcta con {resolver.Provider.Name} ({resolver.Model}). " +
                        $"Respuesta de prueba: «{result.Text}».", muted: false);
        }
        catch (AiException ex)
        {
            SetAiOutput(ex.Message, muted: true);
        }
        catch (Exception ex)
        {
            SetAiOutput("No se pudo conectar: " + ex.Message, muted: true);
        }
        finally
        {
            AiTestButton.IsEnabled = true;
        }
    }

    private void ApplyAiSettingsToUi()
    {
        var wasInitializing = _initializingSettings;
        _initializingSettings = true;

        AiEnabledCheck.IsChecked = _settings.AiEnabled;

        foreach (var child in AiProviderChips.Children)
        {
            if (child is not Button button || button.Tag is not string tag ||
                !Enum.TryParse<AiProviderKind>(tag, out var kind))
                continue;
            var isOn = kind == _settings.AiProvider;
            button.Background = isOn
                ? new SolidColorBrush(Color.FromRgb(30, 58, 95))
                : Brushes.Transparent;
            button.BorderBrush = isOn
                ? new SolidColorBrush(Color.FromRgb(47, 111, 184))
                : (Brush)FindResource("BorderBrush2");
            button.Foreground = isOn
                ? new SolidColorBrush(Color.FromRgb(191, 224, 255))
                : (Brush)FindResource("MutedBrush");
        }

        var resolver = Ai;
        AiModelBox.Text = _settings.AiModel;
        AiBaseUrlBox.Text = _settings.AiBaseUrl;
        AiModelPlaceholder.Visibility = string.IsNullOrEmpty(AiModelBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        var stored = resolver.ApiKey;
        AiKeyStateText.Text = string.IsNullOrEmpty(stored)
            ? (resolver.NeedsKey
                ? $"Sin clave. {resolver.Provider.Name} necesita una: se consigue gratis en su web."
                : "Sin clave. Ollama corre en tu equipo y no necesita ninguna.")
            : "Clave guardada y cifrada con DPAPI: " + AiKeyStore.Mask(stored);

        _initializingSettings = wasInitializing;
    }

    private void AiModel_LostFocus(object sender, RoutedEventArgs e)
    {
        var model = AiModelBox.Text.Trim();
        if (model == _settings.AiModel)
            return;
        _settings.AiModel = model;
        _settingsStore.Save();
        UpdateAiUi();
    }

    private void AiBaseUrl_LostFocus(object sender, RoutedEventArgs e)
    {
        var url = AiBaseUrlBox.Text.Trim();
        if (url == _settings.AiBaseUrl)
            return;
        _settings.AiBaseUrl = url;
        _settingsStore.Save();
        UpdateAiUi();
    }
}
