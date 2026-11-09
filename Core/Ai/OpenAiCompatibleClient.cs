using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Misearch.Core.Ai;

/// <summary>
/// Talks to any OpenAI-compatible chat-completions endpoint: Groq, OpenRouter, a local
/// Ollama (<c>/v1</c>) or any other server. The request and the response are built and
/// read here, so a new provider only means a new entry in <see cref="Providers"/>.
/// </summary>
public sealed class OpenAiCompatibleClient : IAiClient
{
    public static IReadOnlyList<AiProviderInfo> Providers { get; } = new[]
    {
        new AiProviderInfo(AiProviderKind.Groq, "Groq",
            "https://api.groq.com/openai/v1", "llama-3.1-8b-instant"),
        new AiProviderInfo(AiProviderKind.OpenRouter, "OpenRouter",
            "https://openrouter.ai/api/v1", "meta-llama/llama-3.1-8b-instruct:free"),
        new AiProviderInfo(AiProviderKind.Ollama, "Ollama (local)",
            "http://127.0.0.1:11434/v1", "llama3.1"),
        new AiProviderInfo(AiProviderKind.OpenAiCompatible, "Otro (compatible con OpenAI)",
            "", "")
    };

    public static AiProviderInfo Info(AiProviderKind kind) =>
        Providers.First(p => p.Kind == kind);

    private readonly HttpClient _http;

    public OpenAiCompatibleClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
    }

    /// <summary>
    /// Builds the JSON body sent to the endpoint. Kept separate so it can be asserted in
    /// tests without a network round trip.
    /// </summary>
    public static string BuildBody(AiRequest request)
    {
        var messages = new JsonArray();
        foreach (var message in request.Messages)
        {
            messages.Add(new JsonObject
            {
                ["role"] = message.Role,
                ["content"] = message.Content
            });
        }

        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens,
            ["stream"] = false
        };
        return body.ToJsonString();
    }

    /// <summary>Reads <c>choices[0].message.content</c> out of a chat-completions reply.</summary>
    public static AiResult ParseResponse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new AiException("El proveedor de IA devolvió una respuesta vacía.");

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            throw new AiException("El proveedor de IA devolvió algo que no es JSON.");
        }

        if (root?["error"] is JsonNode error)
        {
            var message = error["message"]?.GetValue<string>();
            throw new AiException(string.IsNullOrWhiteSpace(message)
                ? "El proveedor de IA devolvió un error."
                : message);
        }

        var choice = root?["choices"]?.AsArray().FirstOrDefault();
        var content = choice?["message"]?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(content))
            throw new AiException("La respuesta de la IA no traía texto.");

        return new AiResult
        {
            Text = content.Trim(),
            Model = root?["model"]?.GetValue<string>() ?? "",
            PromptTokens = root?["usage"]?["prompt_tokens"]?.GetValue<int>() ?? 0,
            CompletionTokens = root?["usage"]?["completion_tokens"]?.GetValue<int>() ?? 0
        };
    }

    /// <summary>Friendlier text for the HTTP errors these services return.</summary>
    public static string DescribeStatus(HttpStatusCode status, string body)
    {
        var detail = ExtractErrorMessage(body);
        return status switch
        {
            HttpStatusCode.Unauthorized =>
                "La clave de API no es válida o ha caducado. Revísala en Ajustes.",
            HttpStatusCode.TooManyRequests =>
                "Has agotado el límite gratuito del proveedor. Espera un minuto o cambia de modelo.",
            HttpStatusCode.NotFound =>
                "El modelo o la dirección del proveedor no existe." + Suffix(detail),
            HttpStatusCode.RequestEntityTooLarge =>
                "El texto enviado es demasiado largo para este modelo.",
            _ when (int)status >= 500 =>
                "El proveedor de IA no responde en este momento." + Suffix(detail),
            _ => $"El proveedor de IA respondió {(int)status}." + Suffix(detail)
        };
    }

    private static string Suffix(string detail) =>
        string.IsNullOrWhiteSpace(detail) ? "" : " " + detail;

    private static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "";
        try
        {
            var root = JsonNode.Parse(body);
            var message = root?["error"]?["message"]?.GetValue<string>()
                          ?? root?["message"]?.GetValue<string>();
            return message is null ? "" : Truncate(message, 160);
        }
        catch (Exception)
        {
            return Truncate(body, 160);
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    public async Task<AiResult> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BaseUrl))
            throw new AiException("Falta la dirección del proveedor de IA.");
        if (string.IsNullOrWhiteSpace(request.Model))
            throw new AiException("Falta el modelo de IA.");

        var url = request.BaseUrl.TrimEnd('/') + "/chat/completions";
        using var message = new HttpRequestMessage(HttpMethod.Post, url);
        message.Content = new StringContent(BuildBody(request), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiException("La IA tardó demasiado en responder.");
        }
        catch (HttpRequestException ex)
        {
            throw new AiException("No se pudo conectar con el proveedor de IA: " + ex.Message);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new AiException(DescribeStatus(response.StatusCode, body), response.StatusCode);
            return ParseResponse(body);
        }
    }
}
