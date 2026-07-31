#if !OFFLINE
using Macshot.Windows.Core.Recognition;

namespace Macshot.Windows.Services;

/// <summary>
/// Sends recognized text to Google Cloud Translation and reads the answer.
/// </summary>
/// <remarks>
/// <para>
/// The whole file is inside <c>#if !OFFLINE</c>, which is the point of the variant:
/// the offline build does not contain a disabled uploader or a translator behind a
/// flag, it contains no such code at all. This is the first thing to sit behind that
/// gate, so it is also what proves the gate works.
/// </para>
/// <para>
/// The v2 REST endpoint with the user's own key, rather than the client libraries: one
/// form-encoded POST needs no SDK, and an SDK would pull a credential stack into a
/// build variant whose purpose is to have no network stack in it.
/// </para>
/// <para>
/// With no key there is a second endpoint: the keyless one macshot uses —
/// <c>TranslationService.swift:161, 208</c>. It is undocumented and rate-limited, and
/// for a while this port refused to translate at all rather than depend on it. That was
/// the wrong trade for the same product on two platforms: it made translation a feature
/// that worked on a Mac and asked for a Google Cloud account on Windows. A key, when
/// there is one, is still preferred — it is the endpoint with a contract behind it.
/// </para>
/// </remarks>
internal static class TranslationService
{
    private const string Endpoint = "https://translation.googleapis.com/language/translate/v2";

    /// <summary>
    /// The keyless endpoint, which is what macshot translates through.
    /// </summary>
    private const string FreeEndpoint = "https://translate.googleapis.com/translate_a/single";

    /// <summary>
    /// Long enough for a slow link, short enough that a hung request does not leave the
    /// window saying "Translating..." until macshot is quit.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// One client for the process. A new <see cref="HttpClient"/> per request leaves its
    /// socket in TIME_WAIT, which is the standard way to run a machine out of ports.
    /// </summary>
    private static readonly HttpClient Client = new() { Timeout = Timeout };

    /// <summary>
    /// Translates <paramref name="text"/>, answering either the translation or the
    /// reason there is none. Never throws.
    /// </summary>
    /// <remarks>
    /// Every failure comes back as a sentence rather than an exception. This runs from
    /// a button on a window holding text the user already has, so there is nothing a
    /// throw could usefully unwind — and the one thing that must not happen is losing
    /// the recognized text to a network fault.
    /// </remarks>
    public static async Task<TranslationOutcome> TranslateAsync(
        string text,
        string targetLanguage,
        string apiKey,
        CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return TranslationOutcome.Failed("There is no text to translate.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return await TranslateWithoutKeyAsync(text, targetLanguage, cancellation);
        }

        try
        {
            // The key goes in the query string because that is where this endpoint
            // takes it; the text goes in the body because a recognized page of text is
            // far past what a URL will hold.
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["q"] = text,
                ["target"] = TranslationLanguages.Normalize(targetLanguage),

                // Plain text rather than HTML, so the service does not try to preserve
                // markup that OCR never produced.
                ["format"] = "text",
            });

            using var response = await Client.PostAsync(
                $"{Endpoint}?key={Uri.EscapeDataString(apiKey)}",
                content,
                cancellation);

            // Read regardless of the status code: a refusal carries the reason in the
            // same JSON shape as a success, and it is a better message than "400".
            return TranslationResponse.Read(await response.Content.ReadAsStringAsync(cancellation));
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation, which is
            // indistinguishable from the user closing the window unless the token is
            // checked. Only the timeout is worth a message.
            return TranslationOutcome.Failed("The translation service did not answer in time.");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            return TranslationOutcome.Failed($"Could not reach the translation service: {exception.Message}");
        }
    }

    /// <summary>
    /// Translates through the keyless endpoint, so that translation works out of the box
    /// as it does on macOS.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A GET with the text in the query string, which is what the endpoint takes and what
    /// bounds this: a page of recognized text can outgrow a URL. Longer text is refused
    /// here with the sentence that says how to lift the limit, rather than sent and
    /// truncated by something in the middle.
    /// </para>
    /// <para>
    /// A browser's user agent, as macshot sends. Without one the endpoint answers some
    /// callers with a redirect to a consent page instead of a translation.
    /// </para>
    /// </remarks>
    private static async Task<TranslationOutcome> TranslateWithoutKeyAsync(
        string text,
        string targetLanguage,
        CancellationToken cancellation)
    {
        // Comfortably inside what every part of the path will carry, and past anything
        // an overlay's worth of recognized text comes to.
        const int MaxTextLength = 1_500;

        if (text.Length > MaxTextLength)
        {
            return TranslationOutcome.Failed(
                "That is more text than the free translation service takes at once. "
                    + "Add a Google Cloud Translation API key in Preferences to translate it.");
        }

        try
        {
            var query = $"{FreeEndpoint}?client=gtx&sl=auto&tl={Uri.EscapeDataString(TranslationLanguages.Normalize(targetLanguage))}"
                + $"&dt=t&q={Uri.EscapeDataString(text)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");

            using var response = await Client.SendAsync(request, cancellation);
            return TranslationResponse.ReadFree(await response.Content.ReadAsStringAsync(cancellation));
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return TranslationOutcome.Failed("The translation service did not answer in time.");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            return TranslationOutcome.Failed($"Could not reach the translation service: {exception.Message}");
        }
    }
}
#endif
