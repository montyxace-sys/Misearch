using Xunit;

using Misearch.Core.Ai;
using Misearch.Core.Models;

namespace Misearch.CoreTests;

public sealed class AiSettingsResolverTests
{
    private static AppSettings Enabled(AiProviderKind provider = AiProviderKind.Groq) => new()
    {
        AiEnabled = true,
        AiProvider = provider
    };

    [Fact]
    public void The_ai_is_off_by_default()
    {
        var resolver = new AiSettingsResolver(new AppSettings());

        Assert.False(resolver.IsUsable);
        Assert.Contains("desactivada", resolver.UnavailableReason());
    }

    [Fact]
    public void Enabling_it_without_a_key_is_not_enough_for_a_remote_provider()
    {
        var resolver = new AiSettingsResolver(Enabled());

        Assert.False(resolver.IsUsable);
        Assert.Contains("clave", resolver.UnavailableReason());
    }

    [Fact]
    public void A_stored_key_makes_the_provider_usable()
    {
        var settings = Enabled();
        settings.AiApiKeyProtected = AiKeyStore.Protect("gsk_123456");

        var resolver = new AiSettingsResolver(settings);

        Assert.True(resolver.IsUsable);
        Assert.Equal("gsk_123456", resolver.ApiKey);
        Assert.Equal("https://api.groq.com/openai/v1", resolver.BaseUrl);
        Assert.Equal("llama-3.1-8b-instant", resolver.Model);
    }

    [Fact]
    public void Ollama_needs_no_key_because_it_runs_locally()
    {
        var resolver = new AiSettingsResolver(Enabled(AiProviderKind.Ollama));

        Assert.False(resolver.NeedsKey);
        Assert.True(resolver.IsUsable);
        Assert.Equal("http://127.0.0.1:11434/v1", resolver.BaseUrl);
    }

    [Fact]
    public void A_custom_model_and_url_win_over_the_provider_defaults()
    {
        var settings = Enabled(AiProviderKind.OpenAiCompatible);
        settings.AiApiKeyProtected = AiKeyStore.Protect("clave");
        settings.AiModel = "mi-modelo";
        settings.AiBaseUrl = "http://127.0.0.1:8080/v1";

        var resolver = new AiSettingsResolver(settings);

        Assert.Equal("mi-modelo", resolver.Model);
        Assert.Equal("http://127.0.0.1:8080/v1", resolver.BaseUrl);
        Assert.True(resolver.IsUsable);
    }

    [Fact]
    public void Translations_use_the_stronger_groq_model_by_default()
    {
        var settings = Enabled();
        settings.AiApiKeyProtected = AiKeyStore.Protect("gsk_abc");

        var resolver = new AiSettingsResolver(settings);

        Assert.Equal("llama-3.1-8b-instant", resolver.Model);
        Assert.Equal("llama-3.3-70b-versatile", resolver.TranslationModel);
    }

    [Fact]
    public void A_model_picked_by_the_user_wins_for_translations_too()
    {
        var settings = Enabled();
        settings.AiApiKeyProtected = AiKeyStore.Protect("gsk_abc");
        settings.AiModel = "mi-modelo";

        var resolver = new AiSettingsResolver(settings);

        Assert.Equal("mi-modelo", resolver.TranslationModel);
    }

    [Fact]
    public void A_provider_without_a_stronger_model_translates_with_the_same_one()
    {
        var resolver = new AiSettingsResolver(Enabled(AiProviderKind.Ollama));

        Assert.Equal(resolver.Model, resolver.TranslationModel);
    }

    [Fact]
    public void The_request_is_built_from_the_settings()
    {
        var settings = Enabled();
        settings.AiApiKeyProtected = AiKeyStore.Protect("gsk_abc");
        settings.AiModel = "llama-3.3-70b-versatile";

        var request = new AiSettingsResolver(settings)
            .BuildRequest(new[] { AiMessage.User("hola") }, maxTokens: 64);

        Assert.Equal("https://api.groq.com/openai/v1", request.BaseUrl);
        Assert.Equal("llama-3.3-70b-versatile", request.Model);
        Assert.Equal("gsk_abc", request.ApiKey);
        Assert.Equal(64, request.MaxTokens);
        Assert.Single(request.Messages);
    }
}

public sealed class AiKeyStoreTests
{
    [Fact]
    public void A_key_survives_a_protect_unprotect_round_trip()
    {
        var stored = AiKeyStore.Protect("gsk_secreto_123");

        Assert.DoesNotContain("gsk_secreto_123", stored);
        Assert.Equal("gsk_secreto_123", AiKeyStore.Unprotect(stored));
    }

    [Fact]
    public void Protecting_the_same_key_twice_does_not_leave_it_in_clear_text()
    {
        var stored = AiKeyStore.Protect("sk-proj-abcdef");

        Assert.NotEqual("sk-proj-abcdef", stored);
        Assert.False(string.IsNullOrWhiteSpace(stored));
    }

    [Theory]
    [InlineData("")]
    [InlineData("basura que no es base64 ni dpapi")]
    [InlineData("dpapi:AAAA")]
    public void A_damaged_value_reads_as_no_key(string stored)
    {
        Assert.Equal("", AiKeyStore.Unprotect(stored));
    }

    [Fact]
    public void Masking_keeps_only_the_last_four_characters()
    {
        Assert.Equal("••••••cdef", AiKeyStore.Mask("abcdefghcdef"));
        Assert.Equal("••", AiKeyStore.Mask("ab"));
        Assert.Equal("", AiKeyStore.Mask(""));
    }
}
