using System.Net;
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
/// Reads the Google Translate v2 response body.
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

            // The service's own message first: it says things worth passing on verbatim,
            // like an invalid key or an unsupported target language, and a generic
            // failure would send the user looking in the wrong place.
            if (root.TryGetProperty("error", out var error))
            {
                return TranslationOutcome.Failed(
                    error.TryGetProperty("message", out var message) && message.GetString() is { } text
                        ? text
                        : "The translation service refused the request.");
            }

            if (!root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("translations", out var translations)
                || translations.ValueKind != JsonValueKind.Array
                || translations.GetArrayLength() == 0
                || !translations[0].TryGetProperty("translatedText", out var translated)
                || translated.GetString() is not { } result)
            {
                return TranslationOutcome.Failed("The translation service returned no translation.");
            }

            // HTML-decoded even though the request asks for plain text: the v2 endpoint
            // still escapes quotes and ampersands, so a line with an apostrophe in it
            // comes back carrying &#39; and would be pasted that way.
            return TranslationOutcome.Translated(WebUtility.HtmlDecode(result));
        }
        catch (JsonException)
        {
            // An HTML error page from a proxy, most often. Quoting it into the window
            // would fill the text box with markup, so it is named rather than shown.
            return TranslationOutcome.Failed("The translation service returned something unreadable.");
        }
    }
}
