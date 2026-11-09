using System.Text;

namespace Misearch.Core.Ai;

/// <summary>
/// The prompts Misearch sends, and the text it is willing to send. Everything here is
/// pure so the trimming and the instructions can be tested without a provider.
/// </summary>
public static class AiPrompts
{
    /// <summary>Keeps a request well inside the free tiers (Groq's 8k tokens/min).</summary>
    public const int MaxPageChars = 12000;

    public const string SummarySystem =
        "Eres un asistente dentro del navegador Misearch. Respondes en español, en texto " +
        "plano sin markdown, con frases cortas. Resumes páginas web para su lector sin " +
        "inventar nada que no aparezca en el texto que recibes.";

    public const string TranslateSystem =
        "Eres un traductor dentro del navegador Misearch. Traduces al español el texto que " +
        "recibes, manteniendo el significado, el tono y el formato (listas, enlaces, " +
        "números). No explicas la traducción, no añades comentarios y no inventas nada: " +
        "devuelves solo el texto traducido.";

    /// <summary>Longest selection sent to the translator, well inside a free request.</summary>
    public const int MaxTranslateChars = 6000;

    public const string ExplainSystem =
        "Eres un asistente dentro del navegador Misearch. Respondes en español, en texto " +
        "plano y en pocas líneas, explicando lo que el usuario seleccionó con sus propias " +
        "palabras y sin inventar datos que no estén en la selección.";

    /// <summary>
    /// Trims page text to a size a free model can take: collapses the whitespace a scraped
    /// page is full of, cuts on a word boundary and says it was cut.
    /// </summary>
    public static string TrimPageText(string text, int maxChars = MaxPageChars)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var builder = new StringBuilder(Math.Min(text.Length, maxChars + 32));
        var lastWasSpace = false;
        foreach (var ch in text)
        {
            var isSpace = char.IsWhiteSpace(ch);
            if (isSpace && lastWasSpace)
                continue;
            builder.Append(isSpace ? ' ' : ch);
            lastWasSpace = isSpace;
        }

        var clean = builder.ToString().Trim();
        if (clean.Length <= maxChars)
            return clean;

        var cut = clean.LastIndexOf(' ', maxChars);
        if (cut < maxChars / 2)
            cut = maxChars;
        return clean[..cut].TrimEnd() + " […]";
    }

    public static string SummaryPrompt(string pageTitle, string pageUrl, string text)
    {
        var trimmed = TrimPageText(text);
        return $"""
            Página: {pageTitle}
            Dirección: {pageUrl}

            Texto de la página:
            ---
            {trimmed}
            ---

            Responde exactamente en este formato:
            1. Una línea con la idea principal (máximo 25 palabras).
            2. De 3 a 5 viñetas con los puntos clave, cada una de una sola línea.
            3. Una última línea que empiece por "Aviso:" si detectas algo sospechoso
               (pide datos personales, pagos, descargas, urgencia) o "Aviso: nada
               destacable" si no.
            """;
    }

    public static string TranslatePrompt(string text, string targetLanguage = "español")
    {
        var trimmed = TrimPageText(text, MaxTranslateChars);
        return $"""
            Traduce al {targetLanguage} el siguiente texto. Devuelve solo la traducción.

            ---
            {trimmed}
            ---
            """;
    }

    public static string ExplainPrompt(string selection, string pageTitle)
    {
        var trimmed = TrimPageText(selection, 4000);
        return $"""
            Contexto: la página se titula "{pageTitle}".

            Texto seleccionado:
            ---
            {trimmed}
            ---

            Explícalo en 3 o 4 líneas: qué significa, por qué importa y qué debería hacer
            el lector, si procede. Si el texto no tiene sentido por sí solo, dilo en una línea.
            """;
    }
}
