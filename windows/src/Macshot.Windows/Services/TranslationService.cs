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
/// </remarks>
internal static class TranslationService
{
    private const string Endpoint = "https://translation.googleapis.com/language/translate/v2";

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
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return TranslationOutcome.Failed(
                "Translation needs a Google Cloud Translation API key. Add one in Preferences.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return TranslationOutcome.Failed("There is no text to translate.");
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
}
#endif
