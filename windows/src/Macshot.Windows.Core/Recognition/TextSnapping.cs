using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Recognition;

/// <summary>
/// Puts a highlighter stroke on the line of text it was drawn across. macshot's smart
/// marker — <c>MarkerToolHandler.snapToTextLines</c>.
/// </summary>
/// <remarks>
/// Highlighting a line of text by hand means holding a straight line at a constant height
/// and stopping at the right word, which is the one thing a mouse is worst at. The stroke
/// keeps the horizontal span the hand drew — where it starts and stops is what the user
/// meant — and gives up only its height and its thickness, which is what they could not
/// aim at anyway.
/// </remarks>
public static class TextSnapping
{
    /// <summary>
    /// How far above or below a line's box the stroke can pass and still be taken as
    /// meaning that line. A hand aiming at a line of text lands just off it as often as on
    /// it, and a strict test would leave the option looking broken half the time.
    /// </summary>
    private const double VerticalReach = 8;

    /// <summary>
    /// How much of the line the stroke has to cross before it counts. Below this it is a
    /// tick beside a word rather than a highlight of the line, and moving it would be
    /// taking the mark away from what the user drew.
    /// </summary>
    private const double MinimumOverlap = 10;

    /// <summary>
    /// Padding added to the text's own height, so the highlight clears ascenders and
    /// antialiased edges instead of cutting through them.
    /// </summary>
    private const double HeightPadding = 4;

    /// <summary>
    /// Where the highlight sits within the line's box, as a fraction from its top. Below
    /// the middle, because an OCR box includes the room descenders need and the text a
    /// reader sees is therefore above the box's geometric centre.
    /// </summary>
    private const double TextCentre = 0.55;

    /// <summary>
    /// <paramref name="stroke"/> laid across the line of text it crossed, or unchanged
    /// when it crossed none.
    /// </summary>
    /// <remarks>
    /// Unchanged rather than dropped when nothing matches: the user drew a highlight and
    /// is owed one, and a stroke that vanished because the OCR engine did not find text
    /// would read as the tool having failed.
    /// </remarks>
    public static Annotation SnapToText(Annotation stroke, IEnumerable<RecognizedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        ArgumentNullException.ThrowIfNull(lines);

        var drawn = stroke.BoundingRect;

        // The height the stroke was drawn at, not its box: a stroke dragged a little off
        // the horizontal has a tall box, and its ends are what say where it was aimed.
        var height = (stroke.Start.Y + stroke.End.Y) / 2;

        CaptureRegion best = default;
        var bestOverlap = MinimumOverlap;

        foreach (var line in lines)
        {
            var box = line.Bounds;
            if (height < box.Y - VerticalReach || height > box.Bottom + VerticalReach)
            {
                continue;
            }

            var overlap = Math.Min(drawn.Right, box.Right) - Math.Max(drawn.X, box.X);
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                best = box;
            }
        }

        if (best.IsEmpty)
        {
            return stroke;
        }

        var middle = best.Y + (best.Height * TextCentre);

        return stroke with
        {
            Start = new CapturePoint(drawn.X, middle),
            End = new CapturePoint(drawn.Right, middle),

            // Emptied along with the ends, because a marker is drawn from its samples when
            // it has them: leaving the hand-drawn path behind would draw the old stroke
            // under new ends and show the snap as having done nothing.
            Points = [],
            Pressures = [],
            Style = stroke.Style with { StrokeWidth = best.Height + HeightPadding },
        };
    }
}
