using System.Globalization;
using System.Text;

namespace Macshot.Windows.Core.Output;

/// <summary>
/// Turns a user-supplied filename template into a safe file name.
/// </summary>
/// <remarks>
/// <para>
/// The invalid-character set is hard-coded to Windows' rather than taken from
/// <see cref="Path.GetInvalidFileNameChars"/>, because that method answers for the
/// machine running the code. On Linux it reports only NUL and '/', so a
/// developer-machine test run would accept names Windows rejects. macshot only
/// ever writes on Windows, so the Windows rules are the correct ones everywhere.
/// </para>
/// <para>
/// An unrecognised token is left verbatim instead of being dropped, so a typo in
/// the preferences shows up in the file name rather than silently disappearing.
/// </para>
/// </remarks>
/// <summary>
/// What a template can know about the capture beyond the time it was taken.
/// </summary>
/// <remarks>
/// Both are optional and both resolve to nothing when they are not supplied, which is
/// what macshot does — <c>FilenameFormatter.swift:17–18</c>. A template that asks for
/// the window title on a full-screen capture should produce a shorter name, not a
/// name with the word "null" in it.
/// </remarks>
/// <param name="WindowTitle">The title of the window captured, if one was.</param>
/// <param name="Index">Which of a run of captures this is, counting from 1.</param>
public readonly record struct FilenameContext(string? WindowTitle = null, int? Index = null);

public static class FilenameTemplate
{
    /// <summary>
    /// macshot's own default — <c>FilenameFormatter.swift:4</c>. It was
    /// <c>Macshot-{yyyy}{MM}{dd}-{HH}{mm}{ss}</c> here, which is a perfectly good name
    /// and the wrong one: the two apps are the same product, and a user moving between
    /// them should not find their screenshots renamed.
    /// </summary>
    public const string Default = "Screenshot {date} at {time}";

    /// <summary>
    /// What a recording is called, kept separate because macshot keeps it separate —
    /// <c>FilenameFormatter.swift:7</c>. One template for both would mean a folder
    /// where the videos and the screenshots cannot be told apart by name.
    /// </summary>
    public const string DefaultRecording = "Recording {date} at {time}";

    /// <summary>Used when a template resolves to nothing usable, so a capture is never lost.</summary>
    private const string Fallback = "Macshot";

    /// <summary>macshot's cap, and in macshot's unit — UTF-8 bytes, not characters.</summary>
    private const int MaxBytes = 200;

    /// <summary>How long a <c>{random}</c> run is. macshot's eight.</summary>
    private const int RandomLength = 8;

    private const string RandomAlphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    private static readonly char[] InvalidCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static string Resolve(string? template, DateTimeOffset timestamp, FilenameContext context = default)
    {
        var expanded = Expand(string.IsNullOrWhiteSpace(template) ? Default : template, timestamp, context);
        return Sanitize(expanded);
    }

    /// <summary>
    /// Resolves the template and, if that name is taken, suffixes it until it is
    /// free. <paramref name="exists"/> is injected so the whole collision rule is
    /// testable without a file system.
    /// </summary>
    public static string ResolveUnique(
        string? template,
        DateTimeOffset timestamp,
        string extension,
        Func<string, bool> exists,
        FilenameContext context = default)
    {
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(extension);

        var stem = Resolve(template, timestamp, context);
        var candidate = stem + extension;

        // "name (2).png", which is macshot's suffix (ImageSaveService.swift:212–225)
        // and the shell's on both platforms. macshot gives up at 999 and returns a name
        // that already exists, overwriting it; this does not, because a save that
        // silently replaces a capture is worse than a long loop nobody will reach.
        for (var attempt = 2; exists(candidate); attempt++)
        {
            candidate = $"{stem} ({attempt.ToString(CultureInfo.InvariantCulture)}){extension}";
        }

        return candidate;
    }

    private static string Expand(string template, DateTimeOffset timestamp, FilenameContext context)
    {
        var builder = new StringBuilder(template.Length + 16);
        var index = 0;
        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);
            if (open < 0)
            {
                builder.Append(template, index, template.Length - index);
                break;
            }

            var close = template.IndexOf('}', open + 1);
            if (close < 0)
            {
                builder.Append(template, index, template.Length - index);
                break;
            }

            builder.Append(template, index, open - index);
            var token = template.Substring(open + 1, close - open - 1);
            builder.Append(Substitute(token, timestamp, context) ?? template[open..(close + 1)]);
            index = close + 1;
        }

        return builder.ToString();
    }

    /// <summary>
    /// What one token stands for, or null when it stands for nothing and should be
    /// left in the name verbatim so a typo is visible.
    /// </summary>
    /// <remarks>
    /// The named tokens are macshot's — <c>FilenameFormatter.swift:13–19</c> — and are
    /// the ones a template is actually written in. The bare date parts below them are
    /// this port's own and are kept rather than replaced: they were the only tokens
    /// this understood, so removing them would leave a literal <c>{yyyy}</c> in the
    /// file name of anyone who had written a template already.
    /// </remarks>
    private static string? Substitute(string token, DateTimeOffset timestamp, FilenameContext context) => token switch
    {
        "date" => timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        "time" => timestamp.ToString("HH-mm-ss", CultureInfo.InvariantCulture),
        "timestamp" => timestamp.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture),
        "unix" => timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        "window" => context.WindowTitle?.Trim() ?? string.Empty,
        "index" => context.Index?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,

        // Fresh on every occurrence rather than once per name, so a template with two
        // of them produces two different runs — which is the only reason to write two.
        "random" => Random(),

        "yyyy" or "yy" or "MM" or "dd" or "HH" or "mm" or "ss" =>
            timestamp.ToString(token, CultureInfo.InvariantCulture),
        _ => null,
    };

    private static string Random()
    {
        return string.Create(RandomLength, 0, static (span, _) =>
        {
            for (var index = 0; index < span.Length; index++)
            {
                span[index] = RandomAlphabet[System.Random.Shared.Next(RandomAlphabet.Length)];
            }
        });
    }

    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            // Control characters are dropped rather than replaced, which is macshot's
            // rule (FilenameFormatter.swift:87). A window title with a stray newline in
            // it should not become a name with a dash where nothing was.
            if (char.IsControl(character))
            {
                continue;
            }

            builder.Append(Array.IndexOf(InvalidCharacters, character) >= 0 ? '-' : character);
        }

        // Windows silently drops trailing dots and spaces, so a name ending in one
        // would not round-trip: what came back would not be what was asked for.
        var trimmed = builder.ToString().Trim().TrimEnd('.', ' ');
        trimmed = CapBytes(trimmed).TrimEnd('.', ' ').Trim();

        return trimmed.Length == 0 ? Fallback : trimmed;
    }

    /// <summary>
    /// Cuts the name to <see cref="MaxBytes"/> UTF-8 bytes, never mid-character.
    /// </summary>
    /// <remarks>
    /// Bytes rather than characters, which is macshot's cap
    /// (<c>FilenameFormatter.swift:capToByteLength</c>) and the one that matches what a
    /// file system actually limits. Counting characters would let a title in Chinese or
    /// Japanese through at three times the length of the same title in English.
    /// </remarks>
    private static string CapBytes(string name)
    {
        if (Encoding.UTF8.GetByteCount(name) <= MaxBytes)
        {
            return name;
        }

        var bytes = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(name);
        var kept = new StringBuilder(name.Length);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            var cost = Encoding.UTF8.GetByteCount(element);
            if (bytes + cost > MaxBytes)
            {
                break;
            }

            bytes += cost;
            kept.Append(element);
        }

        return kept.ToString();
    }
}
