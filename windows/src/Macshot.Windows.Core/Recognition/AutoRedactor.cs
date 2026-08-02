using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Recognition;

/// <summary>
/// Turns recognized text into censored regions over anything that looks like a
/// secret. The counterpart of the macOS <c>AutoRedactor</c>.
/// </summary>
/// <remarks>
/// It produces ordinary annotations rather than a special redaction object, so the
/// result is undoable, movable, and drawn by the one draw path like everything
/// else. They share a <see cref="Annotation.GroupId"/> so a caller can treat one
/// run as a single action.
/// </remarks>
public static class AutoRedactor
{
    /// <summary>
    /// Solid, opaque black rather than the current drawing colour or the current censor
    /// mode. A redaction is not a highlight: inheriting a translucent marker colour would
    /// produce boxes that still show what is underneath them, and a blurred one would
    /// leave short strings readable. A caller that has a toolbar to ask passes its own
    /// style and gets the mode the user picked, which is what macshot does.
    /// </summary>
    public static AnnotationStyle DefaultStyle { get; } =
        new(new AnnotationColor(0, 0, 0), 1, CensorMode: CensorMode.Solid);

    /// <summary>
    /// Extra margin as a fraction of the box height. OCR boxes hug the glyphs, and
    /// an exact box leaves ascenders and antialiased edges showing.
    /// </summary>
    private const double PaddingRatio = 0.15;

    private const double MinimumPadding = 2;

    /// <param name="kinds">
    /// Which sorts of secret to cover, or null for every sort this build can spot. Passed
    /// through to the detector rather than filtered afterwards: a box discarded after the
    /// fact would already have claimed its region and suppressed the overlapping match
    /// behind it, so an unwanted kind could hide a wanted one.
    /// </param>
    public static IReadOnlyList<Annotation> Redact(
        IEnumerable<RecognizedLine> lines,
        AnnotationStyle? style = null,
        Guid? groupId = null,
        IReadOnlySet<PiiKind>? kinds = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var appliedStyle = style ?? DefaultStyle;
        var group = groupId ?? Guid.NewGuid();
        var annotations = new List<Annotation>();
        var covered = new HashSet<CaptureRegion>();

        foreach (var line in lines)
        {
            foreach (var match in PiiDetector.Detect(line.Text, kinds))
            {
                if (!TryCover(line, match, out var bounds))
                {
                    continue;
                }

                // Two patterns can match the same words — a bearer token that is also
                // a long digit run, say — and stacking identical boxes would only add
                // undo steps.
                if (!covered.Add(bounds))
                {
                    continue;
                }

                annotations.Add(Annotation.Create(
                    AnnotationTool.Censor,
                    new CapturePoint(bounds.X, bounds.Y),
                    new CapturePoint(bounds.Right, bounds.Bottom),
                    appliedStyle) with
                {
                    GroupId = group,
                });
            }
        }

        return annotations;
    }

    /// <summary>
    /// Covers every line of text inside <paramref name="within"/>, rather than only the
    /// lines that look like secrets. macshot's "Text Only" censor scope.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Whole lines rather than the whole region, because that is the difference the option
    /// exists for: blacking out a panel loses the layout that says what was there, and
    /// blacking out the words on it keeps the shape of the thing while making it
    /// unreadable. The gaps between lines are also what makes a redaction obviously a
    /// redaction rather than a crop.
    /// </para>
    /// <para>
    /// A line only partly inside the region is left alone. A box that covered half a
    /// sentence would be a redaction with the rest of the sentence beside it, which is not
    /// a redaction — and the region the user dragged is their statement about how far they
    /// meant to go.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Annotation> RedactAllText(
        IEnumerable<RecognizedLine> lines,
        CaptureRegion within,
        AnnotationStyle? style = null,
        Guid? groupId = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var appliedStyle = style ?? DefaultStyle;
        var group = groupId ?? Guid.NewGuid();

        return
        [
            .. lines
                .Select(line => Padded(line.Bounds))
                .Where(bounds => !bounds.IsEmpty && Encloses(within, bounds))
                .Select(bounds => Annotation.Create(
                    AnnotationTool.Censor,
                    new CapturePoint(bounds.X, bounds.Y),
                    new CapturePoint(bounds.Right, bounds.Bottom),
                    appliedStyle) with
                {
                    GroupId = group,
                }),
        ];
    }

    /// <summary>
    /// Whether <paramref name="inner"/> sits inside <paramref name="outer"/>, allowing the
    /// padding a box was just grown by — otherwise a line the user dragged exactly around
    /// would be rejected by the margin added to cover its own ascenders.
    /// </summary>
    private static bool Encloses(CaptureRegion outer, CaptureRegion inner)
    {
        var slack = Math.Max(MinimumPadding, inner.Height * PaddingRatio);
        return inner.X >= outer.X - slack
            && inner.Right <= outer.Right + slack
            && inner.Y >= outer.Y - slack
            && inner.Bottom <= outer.Bottom + slack;
    }

    private static CaptureRegion Padded(CaptureRegion bounds)
    {
        if (bounds.IsEmpty)
        {
            return bounds;
        }

        var padding = Math.Max(MinimumPadding, bounds.Height * PaddingRatio);
        return new CaptureRegion(
            bounds.X - padding,
            bounds.Y - padding,
            bounds.Width + (padding * 2),
            bounds.Height + (padding * 2));
    }

    private static bool TryCover(RecognizedLine line, PiiMatch match, out CaptureRegion bounds)
    {
        bounds = default;
        foreach (var word in line.WordsOverlapping(match.Start, match.Length))
        {
            bounds = bounds.Union(word.Bounds);
        }

        if (bounds.IsEmpty)
        {
            return false;
        }

        bounds = Padded(bounds);
        return true;
    }
}
