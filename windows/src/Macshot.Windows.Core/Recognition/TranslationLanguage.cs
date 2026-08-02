namespace Macshot.Windows.Core.Recognition;

/// <summary>One language a recognized string can be translated into.</summary>
public sealed record TranslationLanguage(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// The languages offered for translation, and the arithmetic of choosing one.
/// </summary>
/// <remarks>
/// <para>
/// A fixed table rather than a list fetched from the service. The list is asked for
/// before there is any reason to have made a network call — the window opens with a
/// picker in it — and a picker that is empty until a request comes back, or empty
/// forever without a key, is worse than one that is occasionally a language behind.
/// </para>
/// <para>
/// Codes are the ISO-639-1 forms the Google v2 endpoint takes, which is also what the
/// macOS product stores in <c>translateTargetLang</c>, so the two stay interchangeable.
/// </para>
/// </remarks>
public static class TranslationLanguages
{
    /// <summary>What a fresh install translates into.</summary>
    public const string DefaultCode = "en";

    public static IReadOnlyList<TranslationLanguage> All { get; } =
    [
        new("ar", "Arabic"),
        new("bg", "Bulgarian"),
        new("cs", "Czech"),
        new("da", "Danish"),
        new("de", "German"),
        new("el", "Greek"),
        new("en", "English"),
        new("es", "Spanish"),
        new("fa", "Persian"),
        new("fi", "Finnish"),
        new("fr", "French"),
        new("he", "Hebrew"),
        new("hi", "Hindi"),
        new("hu", "Hungarian"),
        new("id", "Indonesian"),
        new("it", "Italian"),
        new("ja", "Japanese"),
        new("ko", "Korean"),
        new("ms", "Malay"),
        new("nl", "Dutch"),
        new("no", "Norwegian"),
        new("pl", "Polish"),
        new("pt", "Portuguese"),
        new("ro", "Romanian"),
        new("ru", "Russian"),
        new("sv", "Swedish"),
        new("th", "Thai"),
        new("tr", "Turkish"),
        new("uk", "Ukrainian"),
        new("vi", "Vietnamese"),

        // Spelled out rather than left as "zh". Simplified and traditional are not a
        // detail of the same choice for anyone who reads one of them.
        new("zh-CN", "Chinese (Simplified)"),
        new("zh-TW", "Chinese (Traditional)"),
    ];

    /// <summary>
    /// The stored code if it is one this offers, and English otherwise.
    /// </summary>
    /// <remarks>
    /// Case-insensitive, because <c>zh-tw</c> is what a hand-edited settings file is
    /// likely to say and refusing it would silently translate into the wrong language.
    /// </remarks>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return DefaultCode;
        }

        var trimmed = code.Trim();
        foreach (var language in All)
        {
            if (string.Equals(language.Code, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return language.Code;
            }
        }

        return DefaultCode;
    }

    /// <summary>
    /// Whether this is a language macshot offers, as opposed to one it would quietly
    /// replace with English.
    /// </summary>
    /// <remarks>
    /// For a caller that has somewhere better to fall back to than the default — a
    /// <c>macshot://ocr-translate?target=…</c> with a code nobody recognises should
    /// leave the language the user chose alone rather than reach past it to English.
    /// </remarks>
    public static bool IsKnown(string? code) =>
        !string.IsNullOrWhiteSpace(code)
        && All.Any(language => string.Equals(language.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The position of a code in <see cref="All"/>, for a picker to select.</summary>
    public static int IndexOf(string? code)
    {
        var normalized = Normalize(code);
        for (var index = 0; index < All.Count; index++)
        {
            if (All[index].Code == normalized)
            {
                return index;
            }
        }

        return 0;
    }
}
