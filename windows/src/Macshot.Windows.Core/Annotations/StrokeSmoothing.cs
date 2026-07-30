using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// Rounds off a freehand stroke, so a line drawn with a mouse looks drawn rather than
/// digitised.
/// </summary>
/// <remarks>
/// <para>
/// Chaikin's corner cutting, the same algorithm the macOS product uses. Each pass replaces
/// every corner with two points a quarter and three quarters of the way along the segments
/// that meet there, which pulls the path towards the curve the hand was making. Two passes
/// is enough to lose the staircase a pointer sampled at screen resolution leaves behind,
/// and few enough that the stroke still goes where it was drawn.
/// </para>
/// <para>
/// Applied when the stroke is finished rather than as it is drawn: smoothing the live
/// path would move points the user is still adding to, so the ink would appear to lag
/// behind the pointer.
/// </para>
/// </remarks>
public static class StrokeSmoothing
{
    /// <summary>How many corner-cutting passes a smoothed stroke gets.</summary>
    public const int Passes = 2;

    /// <summary>
    /// A stroke with its corners cut. The two ends are kept exactly where they were: a
    /// stroke that started somewhere other than where the pointer went down would be
    /// smoothing away the one thing the user aimed.
    /// </summary>
    public static IReadOnlyList<CapturePoint> Smooth(IReadOnlyList<CapturePoint> points, int passes = Passes)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfNegative(passes);

        // Two points are a straight line and one is a dot; there is no corner to cut, and
        // a pass would leave both ends short of where they were.
        if (points.Count < 3 || passes == 0)
        {
            return points;
        }

        var current = points;
        for (var pass = 0; pass < passes; pass++)
        {
            current = CutCorners(current);
        }

        return current;
    }

    private static IReadOnlyList<CapturePoint> CutCorners(IReadOnlyList<CapturePoint> points)
    {
        var cut = new List<CapturePoint>(((points.Count - 1) * 2) + 2) { points[0] };

        for (var index = 0; index < points.Count - 1; index++)
        {
            var from = points[index];
            var to = points[index + 1];

            cut.Add(new CapturePoint(
                (from.X * 0.75) + (to.X * 0.25),
                (from.Y * 0.75) + (to.Y * 0.25)));
            cut.Add(new CapturePoint(
                (from.X * 0.25) + (to.X * 0.75),
                (from.Y * 0.25) + (to.Y * 0.75)));
        }

        cut.Add(points[^1]);
        return cut;
    }
}
