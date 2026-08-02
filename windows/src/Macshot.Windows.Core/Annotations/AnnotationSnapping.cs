using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// How far a mark being moved was nudged to line up, and where to draw the line saying
/// why.
/// </summary>
/// <param name="Dx">What to add to the mark's position, zero when nothing lined up.</param>
/// <param name="GuideX">
/// The vertical line the mark landed on, in frame coordinates, or null when the move did
/// not line up that way. What the guide is <em>for</em>: without it a mark that jumped a
/// few pixels looks like a mark that missed.
/// </param>
public readonly record struct SnapResult(double Dx, double Dy, double? GuideX, double? GuideY)
{
    public static SnapResult None { get; }
}

/// <summary>
/// Lining a mark up with the marks already on the capture, and with the capture itself.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>snapRectDelta</c> and <c>collectSnapTargets</c>
/// (<c>OverlayView.swift:3760</c>), with macshot's targets: every other mark's edges and
/// centre, and the region's own. Three arrows meant to start level are three arrows the
/// eye reads as crooked when one is two pixels out, and nothing about dragging with a
/// mouse makes two pixels reliable.
/// </para>
/// <para>
/// Edges and centres both, because either can be what "lined up" means: two boxes above
/// one another share an edge, and a label under a box shares its centre.
/// </para>
/// </remarks>
public static class AnnotationSnapping
{
    /// <summary>
    /// How near counts as lined up, in frame pixels — macshot's <c>snapThreshold</c>.
    /// </summary>
    /// <remarks>
    /// Small on purpose. A wide threshold makes marks that were meant to stand slightly
    /// apart jump together, and the user is left fighting the feature to place something
    /// six pixels from something else.
    /// </remarks>
    public const double Threshold = 5;

    /// <summary>
    /// The nudge that lines <paramref name="moved"/> up with anything near it.
    /// </summary>
    /// <param name="region">
    /// The capture itself, whose edges and centre are targets too — a mark centred in the
    /// picture is as deliberate as one centred under another mark.
    /// </param>
    /// <param name="others">
    /// Everything already on the capture. The mark being moved must not be in here, or it
    /// would line up with where it used to be and never move at all.
    /// </param>
    public static SnapResult ForMove(
        CaptureRegion moved,
        CaptureRegion region,
        IEnumerable<Annotation> others)
    {
        ArgumentNullException.ThrowIfNull(others);

        var xs = new List<double> { region.X, region.X + (region.Width / 2), region.Right };
        var ys = new List<double> { region.Y, region.Y + (region.Height / 2), region.Bottom };

        foreach (var other in others)
        {
            var bounds = other.BoundingRect;

            // A mark with no extent in either direction is a point, and a point is not
            // an edge anything can be lined up against.
            if (bounds.Width <= 0 && bounds.Height <= 0)
            {
                continue;
            }

            xs.Add(bounds.X);
            xs.Add(bounds.X + (bounds.Width / 2));
            xs.Add(bounds.Right);
            ys.Add(bounds.Y);
            ys.Add(bounds.Y + (bounds.Height / 2));
            ys.Add(bounds.Bottom);
        }

        var (dx, guideX) = Nearest([moved.X, moved.X + (moved.Width / 2), moved.Right], xs);
        var (dy, guideY) = Nearest([moved.Y, moved.Y + (moved.Height / 2), moved.Bottom], ys);
        return new SnapResult(dx, dy, guideX, guideY);
    }

    /// <summary>
    /// The smallest nudge that puts one of <paramref name="edges"/> on one of
    /// <paramref name="targets"/>, and the target it landed on.
    /// </summary>
    private static (double Delta, double? Guide) Nearest(
        IReadOnlyList<double> edges,
        IReadOnlyList<double> targets)
    {
        // Beyond the threshold to start with, so "nothing was near enough" and "nothing
        // was nearer than the last one" are the same test.
        var best = Threshold + 1;
        double delta = 0;
        double? guide = null;

        foreach (var edge in edges)
        {
            foreach (var target in targets)
            {
                var distance = Math.Abs(edge - target);

                // Strictly nearer, so that of two targets the same distance away the
                // first wins — which makes the answer depend on the order the marks were
                // drawn in rather than on floating-point luck.
                if (distance < best)
                {
                    best = distance;
                    delta = target - edge;
                    guide = target;
                }
            }
        }

        return best > Threshold ? (0, null) : (delta, guide);
    }
}
