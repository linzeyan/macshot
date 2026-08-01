#if !OFFLINE
using Macshot.Windows.Core.Recognition;

namespace Macshot.Windows.Services;

/// <summary>
/// Sends recognized text to Google Translate and reads the answer.
/// </summary>
/// <remarks>
/// <para>
/// The whole file is inside <c>#if !OFFLINE</c>, which is the point of the variant:
/// the offline build does not contain a disabled uploader or a translator behind a
/// flag, it contains no such code at all. This is the first thing to sit behind that
/// gate, so it is also what proves the gate works.
/// </para>
/// <para>
/// One endpoint, no API key — the same undocumented <c>translate_a/single</c> macshot
/// calls (<c>TranslationService.swift:201–222</c>). An earlier version of this file
/// offered a Cloud Translation key as the supported path and fell back to this one.
/// That was a difference this product does not have: macshot has no key setting
/// anywhere, so translation there works the moment it is pressed, and a Windows user
/// asked for a Google Cloud account for the same button would be using a different
/// product.
/// </para>
/// <para>
/// macshot's other provider is Apple's on-device translation, chosen in Settings when
/// the system offers it (macOS 15+). Windows has no equivalent, so the port shows no
/// provider control — which is also what macshot does on a Mac that cannot run Apple
/// translation: the section is not built at all.
/// </para>
/// </remarks>
internal static class TranslationService
{
    private const string Endpoint = "https://translate.googleapis.com/translate_a/single";

    /// <summary>
    /// macshot's own timeout — <c>TranslationService.swift:220</c>. Short, because the
    /// window says "Translating..." until this returns.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

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
    /// <para>
    /// Every failure comes back as a sentence rather than an exception. This runs from
    /// a button on a window holding text the user already has, so there is nothing a
    /// throw could usefully unwind — and the one thing that must not happen is losing
    /// the recognized text to a network fault.
    /// </para>
    /// <para>
    /// A GET with the text in the query string, which is what the endpoint takes, and a
    /// browser's user agent, which macshot also sends: without one the endpoint answers
    /// some callers with a redirect to a consent page instead of a translation. No
    /// length limit is imposed here — macshot imposes none either, and a limit invented
    /// on this side would refuse text that translates perfectly well on a Mac.
    /// </para>
    /// </remarks>
    public static async Task<TranslationOutcome> TranslateAsync(
        string text,
        string targetLanguage,
        CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return TranslationOutcome.Failed("There is no text to translate.");
        }

        try
        {
            var query = $"{Endpoint}?client=gtx&sl=auto"
                + $"&tl={Uri.EscapeDataString(TranslationLanguages.Normalize(targetLanguage))}"
                + $"&dt=t&q={Uri.EscapeDataString(text)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");

            using var response = await Client.SendAsync(request, cancellation);

            // Read regardless of the status code: this endpoint says no with a body, and
            // naming what it said is a better message than "429".
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
