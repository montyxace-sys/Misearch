using Misearch.Core.Models;

namespace Misearch.Core.Ai;

/// <summary>
/// Turns the saved settings into a ready-to-use AI client. The AI is off unless the user
/// turned it on and gave a key (Ollama, being local, needs no key).
/// </summary>
public sealed class AiSettingsResolver
{
    private readonly AppSettings _settings;

    public AiSettingsResolver(AppSettings settings)
    {
        _settings = settings;
    }

    public AiProviderInfo Provider => OpenAiCompatibleClient.Info(_settings.AiProvider);

    public string Model =>
        string.IsNullOrWhiteSpace(_settings.AiModel) ? Provider.DefaultModel : _settings.AiModel.Trim();

    public string ApiKey => AiKeyStore.Unprotect(_settings.AiApiKeyProtected);

    public string BaseUrl =>
        string.IsNullOrWhiteSpace(_settings.AiBaseUrl) ? Provider.BaseUrl : _settings.AiBaseUrl.Trim();

    public bool NeedsKey => _settings.AiProvider != AiProviderKind.Ollama;

    /// <summary>True when a request can actually be made right now.</summary>
    public bool IsUsable => _settings.AiEnabled && !string.IsNullOrWhiteSpace(BaseUrl) &&
                            !string.IsNullOrWhiteSpace(Model) &&
                            (!NeedsKey || !string.IsNullOrWhiteSpace(ApiKey));

    /// <summary>Why the AI cannot be used, ready to show in the UI.</summary>
    public string UnavailableReason()
    {
        if (!_settings.AiEnabled)
            return "La IA está desactivada. Actívala en Ajustes.";
        if (string.IsNullOrWhiteSpace(BaseUrl))
            return "Falta la dirección del proveedor de IA.";
        if (NeedsKey && string.IsNullOrWhiteSpace(ApiKey))
            return "Falta la clave de API. Añádela en Ajustes (Groq es gratis).";
        if (string.IsNullOrWhiteSpace(Model))
            return "Falta el modelo de IA.";
        return "";
    }

    public AiRequest BuildRequest(IReadOnlyList<AiMessage> messages, int maxTokens = 800) => new()
    {
        BaseUrl = BaseUrl,
        Model = Model,
        ApiKey = ApiKey,
        Messages = messages,
        MaxTokens = maxTokens
    };
}
