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

    public static IReadOnlyList<Annotation> Redact(
        IEnumerable<RecognizedLine> lines,
        AnnotationStyle? style = null,
        Guid? groupId = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var appliedStyle = style ?? DefaultStyle;
        var group = groupId ?? Guid.NewGuid();
        var annotations = new List<Annotation>();
        var covered = new HashSet<CaptureRegion>();

        foreach (var line in lines)
        {
            foreach (var match in PiiDetector.Detect(line.Text))
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

        var padding = Math.Max(MinimumPadding, bounds.Height * PaddingRatio);
        bounds = new CaptureRegion(
            bounds.X - padding,
            bounds.Y - padding,
            bounds.Width + (padding * 2),
            bounds.Height + (padding * 2));
        return true;
    }
}
