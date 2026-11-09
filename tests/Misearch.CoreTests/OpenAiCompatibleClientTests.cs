using Xunit;

using System.Net;
using System.Text.Json.Nodes;
using Misearch.Core.Ai;

namespace Misearch.CoreTests;

public sealed class OpenAiCompatibleClientTests
{
    private static AiRequest SampleRequest() => new()
    {
        BaseUrl = "https://api.groq.com/openai/v1",
        Model = "llama-3.1-8b-instant",
        ApiKey = "gsk_test",
        Messages = new[] { AiMessage.System("eres un asistente"), AiMessage.User("hola") },
        Temperature = 0.2,
        MaxTokens = 128
    };

    [Fact]
    public void The_body_has_the_model_the_messages_and_the_limits()
    {
        var body = JsonNode.Parse(OpenAiCompatibleClient.BuildBody(SampleRequest()))!;

        Assert.Equal("llama-3.1-8b-instant", body["model"]!.GetValue<string>());
        Assert.Equal(128, body["max_tokens"]!.GetValue<int>());
        Assert.Equal(0.2, body["temperature"]!.GetValue<double>());
        Assert.False(body["stream"]!.GetValue<bool>());

        var messages = body["messages"]!.AsArray();
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal("hola", messages[1]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void A_translation_uses_the_preferred_model()
    {
        var request = new AiRequest
        {
            BaseUrl = "https://api.groq.com/openai/v1",
            Model = "llama-3.1-8b-instant",
            PreferredModel = "llama-3.3-70b-versatile",
            Task = AiTask.Translate,
            Messages = new[] { AiMessage.User("hola") }
        };

        var body = JsonNode.Parse(OpenAiCompatibleClient.BuildBody(request))!;

        Assert.Equal("llama-3.3-70b-versatile", body["model"]!.GetValue<string>());
    }

    [Fact]
    public void Other_tasks_keep_the_model_the_user_picked()
    {
        var request = new AiRequest
        {
            BaseUrl = "https://api.groq.com/openai/v1",
            Model = "llama-3.1-8b-instant",
            PreferredModel = "llama-3.3-70b-versatile",
            Task = AiTask.Summarize,
            Messages = new[] { AiMessage.User("hola") }
        };

        var body = JsonNode.Parse(OpenAiCompatibleClient.BuildBody(request))!;

        Assert.Equal("llama-3.1-8b-instant", body["model"]!.GetValue<string>());
    }

    [Fact]
    public void A_provider_without_a_preferred_model_falls_back_to_the_chosen_one()
    {
        var request = new AiRequest
        {
            BaseUrl = "http://127.0.0.1:11434/v1",
            Model = "llama3.1",
            Task = AiTask.Translate,
            Messages = new[] { AiMessage.User("hola") }
        };

        Assert.Equal("llama3.1", OpenAiCompatibleClient.EffectiveModel(request));
    }

    [Fact]
    public void The_reply_text_is_read_from_the_first_choice()
    {
        var result = OpenAiCompatibleClient.ParseResponse("""
            {
              "model": "llama-3.1-8b-instant",
              "choices": [ { "message": { "role": "assistant", "content": "  Hola.  " } } ],
              "usage": { "prompt_tokens": 12, "completion_tokens": 3 }
            }
            """);

        Assert.Equal("Hola.", result.Text);
        Assert.Equal("llama-3.1-8b-instant", result.Model);
        Assert.Equal(12, result.PromptTokens);
        Assert.Equal(3, result.CompletionTokens);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{"message":{"content":""}}]}""")]
    public void An_unusable_reply_raises_a_friendly_error(string json)
    {
        var error = Assert.Throws<AiException>(() => OpenAiCompatibleClient.ParseResponse(json));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void An_error_object_in_the_body_is_surfaced()
    {
        var error = Assert.Throws<AiException>(() =>
            OpenAiCompatibleClient.ParseResponse("""{"error":{"message":"model not found"}}"""));

        Assert.Contains("model not found", error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "clave")]
    [InlineData(HttpStatusCode.TooManyRequests, "límite")]
    [InlineData(HttpStatusCode.NotFound, "modelo")]
    [InlineData(HttpStatusCode.InternalServerError, "no responde")]
    public void Http_errors_are_explained_in_spanish(HttpStatusCode status, string expected)
    {
        var text = OpenAiCompatibleClient.DescribeStatus(status, "");
        Assert.Contains(expected, text);
    }

    [Fact]
    public void A_provider_error_body_is_appended_to_the_message()
    {
        var text = OpenAiCompatibleClient.DescribeStatus(HttpStatusCode.BadRequest,
            """{"error":{"message":"temperature must be <= 2"}}""");

        Assert.Contains("temperature must be <= 2", text);
    }

    [Fact]
    public void Every_provider_has_a_name_and_a_default_model()
    {
        foreach (var provider in OpenAiCompatibleClient.Providers)
        {
            Assert.False(string.IsNullOrWhiteSpace(provider.Name));

            // The catch-all "otro" entry is the one a user fills in by hand.
            if (provider.Kind == AiProviderKind.OpenAiCompatible)
            {
                Assert.True(string.IsNullOrEmpty(provider.BaseUrl));
                continue;
            }

            Assert.False(string.IsNullOrWhiteSpace(provider.DefaultModel));
            Assert.StartsWith("http", provider.BaseUrl);
        }

        Assert.Equal("https://api.groq.com/openai/v1",
            OpenAiCompatibleClient.Info(AiProviderKind.Groq).BaseUrl);
    }
}
