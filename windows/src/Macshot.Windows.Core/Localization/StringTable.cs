using System.Text;

namespace Macshot.Windows.Core.Localization;

/// <summary>
/// One language's strings, read from macshot's own <c>Localizable.strings</c> format.
/// </summary>
/// <remarks>
/// <para>
/// The format is Apple's rather than a .resw, and that is the whole point: macshot has
/// forty translated files and a contributor workflow written into the header of each
/// one. Re-authoring them as Windows resources would fork the translations, and the
/// next language would then have to be contributed twice.
/// </para>
/// <para>
/// Keys are the English text, which is macshot's rule (<c>en.lproj</c> header: "Keys use
/// the English text"). It has a property worth more than tidiness: a key with no
/// translation falls back to itself, so a missing entry shows English rather than a
/// blank label or a raw identifier. Nothing in this port can go blank for want of a
/// string.
/// </para>
/// </remarks>
public sealed class StringTable
{
    /// <summary>A table with nothing in it, which answers every key with itself.</summary>
    public static StringTable Empty { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    private readonly IReadOnlyDictionary<string, string> _strings;

    private StringTable(IReadOnlyDictionary<string, string> strings) => _strings = strings;

    public int Count => _strings.Count;

    /// <summary>
    /// Reads a <c>.strings</c> file: <c>"key" = "value";</c>, with <c>/* … */</c> and
    /// <c>//</c> comments between entries.
    /// </summary>
    /// <remarks>
    /// Never throws. A translation file is a contribution from someone who does not
    /// build the app, and one malformed line in one language must not stop macshot from
    /// starting — the entries around it are kept and the bad one is skipped, which is
    /// also what the Apple loader does.
    /// </remarks>
    public static StringTable Parse(string? contents)
    {
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(contents))
        {
            return new StringTable(strings);
        }

        var index = 0;
        while (index < contents.Length)
        {
            SkipTrivia(contents, ref index);
            if (index >= contents.Length || contents[index] != '"')
            {
                // Anything that is not the start of an entry: step over it rather than
                // giving up on the rest of the file.
                index++;
                continue;
            }

            if (!TryReadQuoted(contents, ref index, out var key))
            {
                break;
            }

            SkipTrivia(contents, ref index);
            if (index >= contents.Length || contents[index] != '=')
            {
                continue;
            }

            index++;
            SkipTrivia(contents, ref index);
            if (index >= contents.Length || contents[index] != '"')
            {
                continue;
            }

            if (!TryReadQuoted(contents, ref index, out var value))
            {
                break;
            }

            // Last one wins, as with any key-value file read top to bottom.
            strings[key] = value;

            SkipTrivia(contents, ref index);
            if (index < contents.Length && contents[index] == ';')
            {
                index++;
            }
        }

        return new StringTable(strings);
    }

    /// <summary>
    /// The translation of <paramref name="key"/>, or the key itself.
    /// </summary>
    /// <remarks>
    /// An empty translation is treated as no translation. A contributor who leaves a
    /// value blank means "not done yet", and English is a better answer than nothing.
    /// </remarks>
    public string Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return _strings.TryGetValue(key, out var value) && value.Length > 0 ? value : key;
    }

    private static void SkipTrivia(string text, ref int index)
    {
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                index++;
            }
            else if (text[index] == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                var end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? text.Length : end + 2;
            }
            else if (text[index] == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                var end = text.IndexOf('\n', index + 2);
                index = end < 0 ? text.Length : end + 1;
            }
            else
            {
                return;
            }
        }
    }

    /// <summary>
    /// Reads a quoted run starting at the opening quote, resolving the escapes the
    /// format uses. False means the file ended inside the quotes.
    /// </summary>
    private static bool TryReadQuoted(string text, ref int index, out string value)
    {
        var builder = new StringBuilder();
        index++;

        while (index < text.Length)
        {
            var character = text[index];
            if (character == '"')
            {
                index++;
                value = builder.ToString();
                return true;
            }

            if (character == '\\' && index + 1 < text.Length)
            {
                index++;
                builder.Append(text[index] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '0' => '\0',

                    // Anything else escaped is itself: \" and \\ are the common two, and
                    // an unknown escape is likelier a literal backslash than a mistake
                    // worth losing the string over.
                    var other => other,
                });
                index++;
                continue;
            }

            builder.Append(character);
            index++;
        }

        value = string.Empty;
        return false;
    }
}
