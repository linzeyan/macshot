namespace Macshot.Windows.Core.Localization;

/// <summary>One language macshot can be shown in.</summary>
/// <param name="Code">The code the string file is named for.</param>
/// <param name="Name">Its name in that language, which is how the list is read.</param>
public readonly record struct AppLanguage(string Code, string Name);

/// <summary>
/// The languages macshot offers, and how a setting of "system" is resolved.
/// </summary>
/// <remarks>
/// The list and the resolution rule are both macshot's
/// (<c>Services/LanguageManager.swift:12–91</c>), in macshot's order, with each name
/// written in its own language — a list of endonyms is the one a reader looking for
/// their language can actually scan.
/// </remarks>
public static class AppLanguages
{
    /// <summary>The setting value meaning "whatever the system is set to".</summary>
    public const string System = "system";

    public const string Fallback = "en";

    /// <summary>Every language, "System Default" first, exactly as macshot lists them.</summary>
    public static IReadOnlyList<AppLanguage> All { get; } =
    [
        new(System, "System Default"),
        new("ar", "العربية"),
        new("bg", "Български"),
        new("bn", "বাংলা"),
        new("ca", "Català"),
        new("cs", "Čeština"),
        new("da", "Dansk"),
        new("de", "Deutsch"),
        new("el", "Ελληνικά"),
        new("en", "English"),
        new("es", "Español"),
        new("fa", "فارسی"),
        new("fi", "Suomi"),
        new("fil", "Filipino"),
        new("fr", "Français"),
        new("he", "עברית"),
        new("hi", "हिन्दी"),
        new("hr", "Hrvatski"),
        new("hu", "Magyar"),
        new("id", "Bahasa Indonesia"),
        new("it", "Italiano"),
        new("ja", "日本語"),
        new("ko", "한국어"),
        new("ms", "Bahasa Melayu"),
        new("nb", "Norsk bokmål"),
        new("nl", "Nederlands"),
        new("pl", "Polski"),
        new("pt", "Português"),
        new("pt-BR", "Português (Brasil)"),
        new("ro", "Română"),
        new("ru", "Русский"),
        new("sk", "Slovenčina"),
        new("sr", "Српски"),
        new("sv", "Svenska"),
        new("ta", "தமிழ்"),
        new("th", "ไทย"),
        new("tr", "Türkçe"),
        new("uk", "Українська"),
        new("vi", "Tiếng Việt"),
        new("zh-Hans", "简体中文"),
        new("zh-Hant", "繁體中文"),
    ];

    /// <summary>The codes a string file can exist for — everything but "system".</summary>
    public static IReadOnlyList<string> Codes { get; } =
        [.. All.Where(language => language.Code != System).Select(language => language.Code)];

    /// <summary>
    /// Turns a setting and the user's preferred languages into the one code to load.
    /// Never answers <see cref="System"/>.
    /// </summary>
    /// <param name="setting">
    /// What the settings file holds. Null, empty or <see cref="System"/> means follow
    /// <paramref name="preferred"/>.
    /// </param>
    /// <param name="preferred">
    /// The user's languages, best first — on Windows,
    /// <c>GlobalizationPreferences.Languages</c>.
    /// </param>
    /// <remarks>
    /// macshot's rule, in its order: the full code first, so <c>zh-Hant</c> is not
    /// answered with <c>zh-Hans</c>; then the language and script without the region, so
    /// <c>zh-Hant-TW</c> still finds <c>zh-Hant</c>; then the bare language, so
    /// <c>de-AT</c> finds <c>de</c>. English if none of them match.
    /// </remarks>
    public static string Resolve(string? setting, IEnumerable<string>? preferred)
    {
        var chosen = setting?.Trim();
        if (!string.IsNullOrEmpty(chosen) && chosen != System)
        {
            // A code the build does not carry is not honoured: it would show English
            // anyway, and this way the resolution is the same everywhere.
            return Codes.Contains(chosen, StringComparer.OrdinalIgnoreCase)
                ? Codes.First(code => string.Equals(code, chosen, StringComparison.OrdinalIgnoreCase))
                : Fallback;
        }

        foreach (var language in preferred ?? [])
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                continue;
            }

            // Windows reports BCP-47 with hyphens; macOS can produce underscores. Both
            // are normalized so the same rule reads both.
            var normalized = language.Trim().Replace('_', '-');
            var parts = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            string?[] candidates =
            [
                normalized,
                parts.Length >= 2 ? $"{parts[0]}-{parts[1]}" : null,
                parts[0],
            ];

            foreach (var candidate in candidates)
            {
                if (candidate is null)
                {
                    continue;
                }

                var match = Codes.FirstOrDefault(
                    code => string.Equals(code, candidate, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    return match;
                }
            }
        }

        return Fallback;
    }
}
