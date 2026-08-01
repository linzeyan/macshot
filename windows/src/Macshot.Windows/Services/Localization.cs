using System.Reflection;
using Macshot.Windows.Core.Localization;
using Windows.System.UserProfile;

namespace Macshot.Windows.Services;

/// <summary>
/// The active language, and the lookup every user-facing string goes through.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>LanguageManager</c> and its <c>L(…)</c>, with the same forty languages
/// and the same resolution order. The translations are not re-authored here: the
/// <c>.strings</c> files under <c>macshot/*.lproj</c> are linked into this assembly as
/// embedded resources by the project file, so a language contributed to the Mac app
/// arrives in this one with no second pull request.
/// </para>
/// <para>
/// Static, and read from every thread that draws. It is written once at startup and
/// again only when the setting changes, so the field is swapped whole rather than
/// mutated — a reader either sees the old table or the new one, never a half-filled
/// dictionary.
/// </para>
/// </remarks>
public static class Localization
{
    private static volatile StringTable _strings = StringTable.Empty;

    /// <summary>The code actually in use — never "system".</summary>
    public static string Language { get; private set; } = AppLanguages.Fallback;

    /// <summary>
    /// Loads the language a setting of <paramref name="setting"/> resolves to.
    /// </summary>
    /// <remarks>
    /// English loads no file at all: the keys already are the English text, so the empty
    /// table answers every one of them correctly. That also means a build with no string
    /// files in it still shows a complete interface.
    /// </remarks>
    public static void Use(string? setting)
    {
        Language = AppLanguages.Resolve(setting, SystemLanguages());
        _strings = Language == AppLanguages.Fallback ? StringTable.Empty : Load(Language);
    }

    /// <summary>
    /// The translation of <paramref name="english"/>, or the English itself.
    /// </summary>
    /// <remarks>
    /// Named for what it does at the call site rather than for what it is. It appears
    /// several hundred times in this project and macshot spells it <c>L</c>, which is
    /// short enough to leave the string the thing being read.
    /// </remarks>
    public static string L(string english) => _strings.Get(english);

    /// <summary>
    /// <see cref="L"/> with the arguments filled in.
    /// </summary>
    /// <remarks>
    /// Composite formatting rather than interpolation, because a translator has to be
    /// able to move the values around: some languages put the count after the noun.
    /// </remarks>
    public static string L(string english, params object?[] arguments) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, L(english), arguments);

    private static IEnumerable<string> SystemLanguages()
    {
        try
        {
            return GlobalizationPreferences.Languages;
        }
        catch (Exception)
        {
            // Reachable when the app runs without a user profile to ask, which is rare
            // but not worth failing to start over: English is a working answer.
            return [];
        }
    }

    private static StringTable Load(string code)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream($"Macshot.Windows.Strings.{code}.strings");
            if (stream is null)
            {
                return StringTable.Empty;
            }

            using var reader = new StreamReader(stream);
            return StringTable.Parse(reader.ReadToEnd());
        }
        catch (Exception exception)
        {
            // A language that cannot be read shows English rather than stopping the app.
            DiagnosticLog.Write($"could not load the {code} strings: {exception.Message}");
            return StringTable.Empty;
        }
    }
}
