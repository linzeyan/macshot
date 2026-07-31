#if !OFFLINE
using Macshot.Windows.Core.Output;
using Macshot.Windows.Core.Recognition;

namespace Macshot.Windows.Services;

/// <summary>
/// Reads the text on show, translates it, and lays the translation over the words it
/// replaces. Answers the sentence to put in the hint line, whatever happened.
/// </summary>
/// <remarks>
/// <para>
/// Shared by the overlay and the editor, which run the same three steps and differ only
/// in where the sentence goes. Written twice it would be two chances to get the
/// line-matching wrong, and getting that wrong puts every translation over the wrong
/// words without anything looking amiss.
/// </para>
/// <para>
/// Behind <c>#if !OFFLINE</c> along with the translator it calls: the offline build
/// contains no such code at all, which is what the variant is for.
/// </para>
/// </remarks>
internal static class TranslationPlacement
{
    public static async Task<string> RunAsync(
        AnnotationCanvasView canvas,
        CaptureSettings settings,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(settings);

        var (asked, request) = TranslationOverlay.Ask(await canvas.RecognizeAsync());
        if (asked.Count == 0)
        {
            return "No text found to translate";
        }

        var outcome = await TranslationService.TranslateAsync(
            request,
            settings.TranslateTargetLanguage,
            settings.TranslateApiKey,
            cancellation);

        if (outcome.Text is not { } answer)
        {
            return outcome.Failure ?? "The translation failed.";
        }

        // A refusal rather than a best effort: the lines are matched by position, so an
        // answer that merged two of them or broke one in half would cover the wrong
        // words from that point down.
        if (TranslationOverlay.Pair(asked, answer) is not { } paired || paired.Count == 0)
        {
            return "The translation did not line up with the text, so nothing was changed";
        }

        var placed = await canvas.LayTranslationsOverAsync(paired);
        return $"Translated {placed} lines • Ctrl+Z to undo";
    }
}
#endif
