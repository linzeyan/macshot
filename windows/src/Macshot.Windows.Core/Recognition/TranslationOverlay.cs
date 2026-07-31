using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Recognition;

/// <summary>
/// One recognized line paired with what it says in another language, and the box the
/// translation has to fit.
/// </summary>
public sealed record TranslatedLine(string Text, CaptureRegion Bounds);

/// <summary>
/// Lays a translation over the text it replaces: what to send, how to match the answer
/// back to the lines it came from, and how big each replacement has to be drawn.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of the macOS <c>TranslationOverlay</c>, and it makes the same three
/// choices: the box is padded by one pixel, it is painted the average colour of what it
/// covers so it sits on the page rather than on top of it, and the type is set to
/// <see cref="HeightRatio"/> of the box so a translation that runs longer than the
/// original still occupies the line it belongs to.
/// </para>
/// <para>
/// What is here is everything that can be decided without a network or a font. Sending
/// the request and rasterizing the glyphs are the caller's, because one needs a key and
/// the other needs a display.
/// </para>
/// </remarks>
public static class TranslationOverlay
{
    /// <summary>
    /// How far the box overhangs the text it covers. One pixel, the same as macshot:
    /// enough to swallow an antialiased edge, little enough that consecutive lines do
    /// not overlap into each other.
    /// </summary>
    public const double Padding = 1;

    /// <summary>
    /// The type size as a fraction of the line's height. Below the box rather than
    /// filling it, because an OCR box is drawn round the glyphs' extremes — an ascender
    /// on one word and a descender on another — and type set to the full height would
    /// stand taller than the words it replaces.
    /// </summary>
    public const double HeightRatio = 0.65;

    /// <summary>
    /// Never smaller than this, whatever the line's height. A translation rendered at
    /// four pixels is not a translation, it is a smudge that hides the original.
    /// </summary>
    public const double MinimumFontSize = 8;

    /// <summary>
    /// What separates the lines in one request, and what the answer is split on.
    /// </summary>
    /// <remarks>
    /// One request rather than one per line. A page of recognized text is thirty or
    /// forty lines, and thirty round trips would take long enough that the user would
    /// assume it had failed — quite apart from what it costs against a metered key.
    /// </remarks>
    public const char LineSeparator = '\n';

    /// <summary>
    /// The lines worth sending, in reading order, and the text to send for them.
    /// </summary>
    /// <remarks>
    /// Blank lines are dropped rather than sent. OCR produces them from rules and
    /// borders, they translate to nothing, and each one left in shifts every line after
    /// it out of step with its answer.
    /// </remarks>
    public static (IReadOnlyList<RecognizedLine> Lines, string Request) Ask(IEnumerable<RecognizedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var worthSending = lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .ToArray();

        return (worthSending, string.Join(LineSeparator, worthSending.Select(line => line.Text)));
    }

    /// <summary>
    /// The answer matched back to the lines it was asked about, or null when it cannot
    /// be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null rather than a best effort. The lines are matched by position, so a service
    /// that merged two of them or broke one in half puts every later translation over
    /// the wrong words — and a translation over the wrong words is worse than no
    /// translation, because nothing about it looks wrong.
    /// </para>
    /// <para>
    /// The box comes back padded and the caller does not pad it again: two callers
    /// padding by the amount they each thought right is how the boxes started
    /// overlapping on macshot.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<TranslatedLine>? Pair(IReadOnlyList<RecognizedLine> asked, string? answer)
    {
        ArgumentNullException.ThrowIfNull(asked);

        if (asked.Count == 0 || string.IsNullOrEmpty(answer))
        {
            return null;
        }

        var translations = answer.Split(LineSeparator);
        if (translations.Length != asked.Count)
        {
            return null;
        }

        var paired = new List<TranslatedLine>(asked.Count);
        for (var index = 0; index < asked.Count; index++)
        {
            var translated = translations[index].Trim();

            // A line the service had nothing to say about is left showing its original,
            // which is more use than a blank box over it.
            if (translated.Length == 0)
            {
                continue;
            }

            paired.Add(new TranslatedLine(translated, BoxOf(asked[index])));
        }

        return paired;
    }

    /// <summary>The type size for a line of this height, in frame pixels.</summary>
    public static double FontSizeFor(double boxHeight) =>
        Math.Max(MinimumFontSize, boxHeight * HeightRatio);

    /// <summary>
    /// The padded box a line's translation is drawn into: the union of its words, which
    /// is narrower than the line's own bounds wherever OCR read a gap.
    /// </summary>
    private static CaptureRegion BoxOf(RecognizedLine line)
    {
        var bounds = default(CaptureRegion);
        foreach (var word in line.Words)
        {
            bounds = bounds.Union(word.Bounds);
        }

        return new CaptureRegion(
            bounds.X - Padding,
            bounds.Y - Padding,
            bounds.Width + (Padding * 2),
            bounds.Height + (Padding * 2));
    }
}
