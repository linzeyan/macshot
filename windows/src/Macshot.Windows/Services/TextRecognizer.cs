using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Recognition;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace Macshot.Windows.Services;

/// <summary>
/// Reads the text out of a capture, the Windows counterpart of the macOS
/// <c>VisionOCR</c> wrapper.
/// </summary>
/// <remarks>
/// <c>Windows.Media.Ocr</c> is part of the OS, so this needs no model download and
/// no network. It recognizes the languages the user has installed, which is why the
/// engine is created from the user profile rather than pinned to English.
/// </remarks>
public static class TextRecognizer
{
    /// <summary>
    /// Whether the machine can recognize text at all. A Windows install with no OCR
    /// language pack has no engine, and the caller should say so rather than return
    /// an empty result that reads as "no text found".
    /// </summary>
    public static bool IsAvailable => TryCreateEngine() is not null;

    /// <summary>
    /// Recognizes the text in a frame and reports it in frame space.
    /// </summary>
    /// <param name="frame">The pixels to read, normally the cropped selection.</param>
    /// <param name="originX">Frame-space X the bitmap's left edge sits at.</param>
    /// <param name="originY">Frame-space Y the bitmap's top edge sits at.</param>
    /// <remarks>
    /// The engine reports boxes relative to the bitmap it was given, and every
    /// annotation is stored against the whole virtual desktop, so the origin has to
    /// be added here. Doing it anywhere else would mean redaction boxes that are
    /// only correct when the selection starts at the desktop's top-left corner.
    /// </remarks>
    public static async Task<IReadOnlyList<RecognizedLine>> RecognizeAsync(
        CapturedFrame frame,
        double originX,
        double originY)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var engine = TryCreateEngine()
            ?? throw new InvalidOperationException(
                "Windows has no OCR language installed. Add a language pack in Settings to recognize text.");

        if (frame.Width > OcrEngine.MaxImageDimension || frame.Height > OcrEngine.MaxImageDimension)
        {
            throw new InvalidOperationException(
                $"The selection is larger than the {OcrEngine.MaxImageDimension} pixel limit Windows OCR accepts.");
        }

        using var bitmap = frame.ToSoftwareBitmap();
        var result = await engine.RecognizeAsync(bitmap);

        var lines = new List<RecognizedLine>();
        foreach (var line in result.Lines)
        {
            var words = line.Words
                .Select(word => new RecognizedWord(
                    word.Text,
                    new CaptureRegion(
                        originX + word.BoundingRect.X,
                        originY + word.BoundingRect.Y,
                        word.BoundingRect.Width,
                        word.BoundingRect.Height)))
                .ToArray();

            // A line the engine reports with no words carries no position, so there
            // is nothing to redact and nothing to show.
            if (words.Length > 0)
            {
                lines.Add(new RecognizedLine(words));
            }
        }

        return lines;
    }

    /// <summary>Joins recognized lines back into readable text.</summary>
    public static string ToText(IEnumerable<RecognizedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return string.Join(Environment.NewLine, lines.Select(line => line.Text));
    }

    private static OcrEngine? TryCreateEngine()
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is not null)
        {
            return engine;
        }

        // Every profile language may be one without an OCR pack while English is
        // still present, since it ships with most installs. Worth one more try
        // before telling the user it cannot be done at all.
        return OcrEngine.TryCreateFromLanguage(new Language("en-US"));
    }
}
