using System.Text.Json;

namespace Macshot.Windows.Core.Recognition;

/// <summary>
/// What came back from a translation request: the text, or why there is none.
/// </summary>
public sealed record TranslationOutcome(string? Text, string? Failure)
{
    public bool Succeeded => Text is not null;

    public static TranslationOutcome Translated(string text) => new(text, null);

    public static TranslationOutcome Failed(string reason) => new(null, reason);
}

/// <summary>
/// Reads the response body of the endpoint macshot translates through.
/// </summary>
/// <remarks>
/// <para>
/// Kept in Core, away from the HTTP call, because the parsing is the part that can be
/// wrong in ways worth a test and the part that has to survive the service changing
/// its mind about error shapes. The request itself is compiled out of the offline
/// build; this is not, and does not need to be — it reaches nothing.
/// </para>
/// <para>
/// Nothing here throws. A translation is an extra offered on top of a capture the user
/// already has, so every way it can fail has to end as a sentence in the window rather
/// than as an exception over a finished capture.
/// </para>
/// </remarks>
public static class TranslationResponse
{
    /// <summary>
    /// Reads the body of the <c>translate_a/single</c> endpoint, which is what macshot
    /// uses — <c>TranslationService.swift:161, 208</c>. It is the only endpoint either
    /// product talks to: macshot has no API key setting, so neither does this.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shape is nested bare arrays with no names in it at all:
    /// <c>[[["translated","source",…],["more","source",…]],null,"en",…]</c>. The
    /// translation is every first element of the first array's entries, joined — the
    /// service splits a paragraph into sentences and hands each one back separately, so
    /// taking only the first would silently truncate anything longer than a line.
    /// </para>
    /// <para>
    /// An entry whose first element is not a string is skipped rather than refused. The
    /// endpoint is undocumented, and the trailing entries it adds carry alternatives and
    /// transliterations that have nothing to do with the text asked for.
    /// </para>
    /// </remarks>
    public static TranslationOutcome Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return TranslationOutcome.Failed("The translation service returned nothing.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Array
                || root.GetArrayLength() == 0
                || root[0].ValueKind != JsonValueKind.Array)
            {
                return TranslationOutcome.Failed("The translation service returned no translation.");
            }

            var builder = new System.Text.StringBuilder();
            foreach (var chunk in root[0].EnumerateArray())
            {
                if (chunk.ValueKind == JsonValueKind.Array
                    && chunk.GetArrayLength() > 0
                    && chunk[0].ValueKind == JsonValueKind.String)
                {
                    builder.Append(chunk[0].GetString());
                }
            }

            var text = builder.ToString();
            return text.Length == 0
                ? TranslationOutcome.Failed("The translation service returned no translation.")
                : TranslationOutcome.Translated(text);
        }
        catch (JsonException)
        {
            // The keyless endpoint answers a rate-limited caller with an HTML page, so
            // this is the ordinary way it says no rather than an exceptional one.
            return TranslationOutcome.Failed("The translation service returned something unreadable.");
        }
    }
}
