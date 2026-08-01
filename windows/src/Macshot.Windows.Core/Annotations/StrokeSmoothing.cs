using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// Rounds off a freehand stroke, so a line drawn with a mouse looks drawn rather than
/// digitised.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PencilSmoothing.Smooth"/> is Chaikin's corner cutting, the algorithm the
/// macOS product uses. Each pass replaces every corner with two points a quarter and
/// three quarters of the way along the segments that meet there, which pulls the path
/// towards the curve the hand was making. Two passes is enough to lose the staircase a
/// pointer sampled at screen resolution leaves behind, and few enough that the stroke
/// still goes where it was drawn.
/// </para>
/// <para>
/// <see cref="PencilSmoothing.Refined"/> runs a moving average along the stroke first,
/// which is what removes the tremor corner cutting leaves in — cutting a corner between
/// two shaky samples gives a shorter shaky corner. The average trails, so it lags the
/// pointer; padding the end with copies of the last sample lets it arrive at the true
/// end of the stroke instead of stopping short of it.
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
    /// How many samples the refined mode averages over. macshot's eight — enough to lose
    /// a hand's tremor, short enough that a deliberate turn survives it.
    /// </summary>
    public const int Window = 8;

    /// <summary>
    /// A stroke as the chosen mode leaves it. The two ends are kept exactly where they
    /// were: a stroke that started somewhere other than where the pointer went down would
    /// be smoothing away the one thing the user aimed.
    /// </summary>
    public static IReadOnlyList<CapturePoint> Smooth(
        IReadOnlyList<CapturePoint> points,
        PencilSmoothing mode = PencilSmoothing.Smooth)
    {
        ArgumentNullException.ThrowIfNull(points);

        // Two points are a straight line and one is a dot; there is no corner to cut, and
        // a pass would leave both ends short of where they were.
        if (points.Count < 3 || mode == PencilSmoothing.None)
        {
            return points;
        }

        return CutCorners(
            mode == PencilSmoothing.Refined ? Average(Padded(points)) : points,
            Passes);
    }

    private static IReadOnlyList<CapturePoint> CutCorners(IReadOnlyList<CapturePoint> points, int passes)
    {
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

    /// <summary>The stroke with the last sample repeated, so a trailing average can catch up to it.</summary>
    private static IReadOnlyList<CapturePoint> Padded(IReadOnlyList<CapturePoint> points)
    {
        var padded = new List<CapturePoint>(points.Count + Window - 1);
        padded.AddRange(points);
        for (var pad = 0; pad < Window - 1; pad++)
        {
            padded.Add(points[^1]);
        }

        return padded;
    }

    /// <summary>
    /// Each point replaced by the mean of it and the samples before it. Trailing rather
    /// than centred, matching macshot, so that early points are averaged over what little
    /// there is and the stroke still starts where the pointer went down.
    /// </summary>
    private static IReadOnlyList<CapturePoint> Average(IReadOnlyList<CapturePoint> points)
    {
        var averaged = new List<CapturePoint>(points.Count);
        for (var index = 0; index < points.Count; index++)
        {
            var from = Math.Max(0, index - Window + 1);
            double x = 0;
            double y = 0;
            for (var at = from; at <= index; at++)
            {
                x += points[at].X;
                y += points[at].Y;
            }

            var count = index - from + 1;
            averaged.Add(new CapturePoint(x / count, y / count));
        }

        return averaged;
    }
}
