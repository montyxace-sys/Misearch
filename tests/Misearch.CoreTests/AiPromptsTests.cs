using Xunit;

using Misearch.Core.Ai;

namespace Misearch.CoreTests;

public sealed class AiPromptsTests
{
    [Fact]
    public void Long_pages_are_trimmed_on_a_word_boundary()
    {
        var text = string.Join(" ", Enumerable.Repeat("palabra", 5000));

        var trimmed = AiPrompts.TrimPageText(text, 200);

        Assert.True(trimmed.Length <= 220);
        Assert.EndsWith("[…]", trimmed);
        Assert.DoesNotContain("palabr[…]", trimmed);
    }

    [Fact]
    public void Whitespace_from_scraped_pages_is_collapsed()
    {
        var trimmed = AiPrompts.TrimPageText("  hola   mundo \n\n\t otra   línea  ");

        Assert.Equal("hola mundo otra línea", trimmed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void Empty_text_stays_empty(string text)
    {
        Assert.Equal("", AiPrompts.TrimPageText(text));
    }

    [Fact]
    public void The_summary_prompt_carries_the_title_the_url_and_the_text()
    {
        var prompt = AiPrompts.SummaryPrompt("Título de prueba", "https://ejemplo.com/a", "contenido de la página");

        Assert.Contains("Título de prueba", prompt);
        Assert.Contains("https://ejemplo.com/a", prompt);
        Assert.Contains("contenido de la página", prompt);
        Assert.Contains("Aviso:", prompt);
    }

    [Fact]
    public void The_translate_prompt_asks_for_the_text_only()
    {
        var prompt = AiPrompts.TranslatePrompt("Hello, world");

        Assert.Contains("Hello, world", prompt);
        Assert.Contains("español", prompt);
        Assert.Contains("solo la traducción", prompt);
    }

    [Fact]
    public void A_long_selection_is_trimmed_before_translating()
    {
        var text = string.Join(" ", Enumerable.Repeat("palabra", 4000));

        var prompt = AiPrompts.TranslatePrompt(text);

        // 6 000 characters of text plus the instructions around it, never the whole 32 000.
        Assert.True(prompt.Length < AiPrompts.MaxTranslateChars + 400, $"prompt de {prompt.Length} caracteres");
        Assert.Contains("[…]", prompt);
    }

    [Fact]
    public void The_explain_prompt_carries_the_selection()
    {
        var prompt = AiPrompts.ExplainPrompt("texto seleccionado por el usuario", "Una página");

        Assert.Contains("texto seleccionado por el usuario", prompt);
        Assert.Contains("Una página", prompt);
    }
}
