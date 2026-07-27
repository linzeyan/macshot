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
public static class FilenameTemplate
{
    public const string Default = "Macshot-{yyyy}{MM}{dd}-{HH}{mm}{ss}";

    /// <summary>Used when a template resolves to nothing usable, so a capture is never lost.</summary>
    private const string Fallback = "Macshot";

    private const int MaxLength = 120;

    private static readonly char[] InvalidCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static string Resolve(string? template, DateTimeOffset timestamp)
    {
        var expanded = Expand(string.IsNullOrWhiteSpace(template) ? Default : template, timestamp);
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
        Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(extension);

        var stem = Resolve(template, timestamp);
        var candidate = stem + extension;
        for (var attempt = 2; exists(candidate); attempt++)
        {
            candidate = $"{stem}-{attempt.ToString(CultureInfo.InvariantCulture)}{extension}";
        }

        return candidate;
    }

    private static string Expand(string template, DateTimeOffset timestamp)
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
            builder.Append(Substitute(token, timestamp) ?? template[open..(close + 1)]);
            index = close + 1;
        }

        return builder.ToString();
    }

    private static string? Substitute(string token, DateTimeOffset timestamp) => token switch
    {
        "yyyy" or "yy" or "MM" or "dd" or "HH" or "mm" or "ss" =>
            timestamp.ToString(token, CultureInfo.InvariantCulture),
        _ => null,
    };

    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(
                char.IsControl(character) || Array.IndexOf(InvalidCharacters, character) >= 0
                    ? '-'
                    : character);
        }

        // Windows silently drops trailing dots and spaces, so a name ending in one
        // would not round-trip: what came back would not be what was asked for.
        var trimmed = builder.ToString().Trim().TrimEnd('.', ' ');
        if (trimmed.Length > MaxLength)
        {
            trimmed = trimmed[..MaxLength].TrimEnd('.', ' ');
        }

        return trimmed.Length == 0 ? Fallback : trimmed;
    }
}
