using System.Net;

namespace Misearch.Core.Ai;

/// <summary>What the assistant is asked to do; it decides which model is used.</summary>
public enum AiTask
{
    Summarize,
    Explain,
    Translate
}

public enum AiProviderKind
{
    Groq,
    OpenRouter,
    Ollama,
    OpenAiCompatible
}

public sealed record AiProviderInfo(
    AiProviderKind Kind,
    string Name,
    string BaseUrl,
    string DefaultModel,
    string PreferredModel = "");

public sealed class AiMessage
{
    public AiMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }

    public string Role { get; }
    public string Content { get; }

    public static AiMessage System(string content) => new("system", content);
    public static AiMessage User(string content) => new("user", content);
    public static AiMessage Assistant(string content) => new("assistant", content);
}

/// <summary>A request that failed for a reason the UI should explain to the user.</summary>
public sealed class AiException : Exception
{
    public AiException(string message, HttpStatusCode? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}

public sealed class AiRequest
{
    public string BaseUrl { get; init; } = "";
    public string Model { get; init; } = "";
    /// <summary>Used instead of <see cref="Model"/> for translations, when the provider has one.</summary>
    public string PreferredModel { get; init; } = "";
    public AiTask Task { get; init; } = AiTask.Summarize;
    public string ApiKey { get; init; } = "";
    public IReadOnlyList<AiMessage> Messages { get; init; } = Array.Empty<AiMessage>();
    public double Temperature { get; init; } = 0.3;
    public int MaxTokens { get; init; } = 800;
}

public sealed class AiResult
{
    public string Text { get; init; } = "";
    public string Model { get; init; } = "";
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
}

public interface IAiClient
{
    Task<AiResult> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default);
}
