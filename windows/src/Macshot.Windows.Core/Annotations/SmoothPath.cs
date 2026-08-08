using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// The curve a line, arrow or ruler runs along once it has been given intermediate
/// anchors: a Catmull-Rom spline through every point of the chain, flattened to a
/// polyline.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>smoothPath(through:)</c> (<c>Annotation.swift:798</c>), converted to cubic
/// Béziers the same way: each span's two controls sit a sixth of the way along the chord
/// between the points on either side of it. A spline rather than a jointed polyline
/// because that is what macOS draws — the anchors are waypoints the mark passes smoothly
/// through, not corners it turns at, and an elbowed version would read as a different
/// tool.
/// </para>
/// <para>
/// Flattened rather than kept as curves, because everything downstream already works on
/// polylines: the stroker, the hit test, the reading a ruler reports, the taper down the
/// side of a banner arrow. macOS samples the same curve in three separate places for those
/// three answers (<c>:566</c>, <c>:822</c>, <c>:798</c>); one flattening shared between
/// them is what keeps the path that is drawn, the path that can be grabbed and the path
/// that is measured from being three slightly different curves.
/// </para>
/// </remarks>
public static class SmoothPath
{
    /// <summary>
    /// How many straight pieces each span of the curve is flattened into.
    /// </summary>
    /// <remarks>
    /// Per span rather than macshot's per path (<c>anchors × 15</c> for its hit test,
    /// <c>× 20</c> for its length — 15 to 22 pieces a span either way), so a chain of ten
    /// anchors is as smooth everywhere as a chain of three rather than merely as smooth in
    /// total.
    /// </remarks>
    public const int SegmentsPerSpan = 16;

    /// <summary>
    /// <paramref name="anchors"/> joined into one polyline: the chain itself when there is
    /// nothing to curve, and the spline through it otherwise.
    /// </summary>
    /// <remarks>
    /// Two points are already a straight line and one is a point, which is why they come
    /// back untouched — a spline fitted to them would only round off ends nobody bent.
    /// </remarks>
    public static CapturePoint[] Through(IReadOnlyList<CapturePoint> anchors)
    {
        ArgumentNullException.ThrowIfNull(anchors);

        if (anchors.Count < 3)
        {
            return [.. anchors];
        }

        var flattened = new List<CapturePoint>(((anchors.Count - 1) * SegmentsPerSpan) + 1) { anchors[0] };
        for (var span = 0; span < anchors.Count - 1; span++)
        {
            // The ends of the chain have no neighbour beyond them, so they stand in for
            // their own: that is what makes the curve leave the first anchor and arrive at
            // the last along the chord rather than swinging past it.
            var before = span > 0 ? anchors[span - 1] : anchors[span];
            var from = anchors[span];
            var to = anchors[span + 1];
            var after = span + 2 < anchors.Count ? anchors[span + 2] : anchors[span + 1];

            var firstControl = new CapturePoint(
                from.X + ((to.X - before.X) / 6),
                from.Y + ((to.Y - before.Y) / 6));
            var secondControl = new CapturePoint(
                to.X - ((after.X - from.X) / 6),
                to.Y - ((after.Y - from.Y) / 6));

            for (var step = 1; step <= SegmentsPerSpan; step++)
            {
                flattened.Add(CubicAt(from, firstControl, secondControl, to, (double)step / SegmentsPerSpan));
            }
        }

        return [.. flattened];
    }

    /// <summary>
    /// How long the curve through <paramref name="anchors"/> actually is, which is longer
    /// than the straight line between its ends by however much it wanders.
    /// </summary>
    public static double Length(IReadOnlyList<CapturePoint> anchors)
    {
        var path = Through(anchors);
        var total = 0.0;
        for (var step = 1; step < path.Length; step++)
        {
            var deltaX = path[step].X - path[step - 1].X;
            var deltaY = path[step].Y - path[step - 1].Y;
            total += Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        }

        return total;
    }

    private static CapturePoint CubicAt(
        CapturePoint from,
        CapturePoint firstControl,
        CapturePoint secondControl,
        CapturePoint to,
        double t)
    {
        var inverse = 1 - t;
        var a = inverse * inverse * inverse;
        var b = 3 * inverse * inverse * t;
        var c = 3 * inverse * t * t;
        var d = t * t * t;

        return new CapturePoint(
            (a * from.X) + (b * firstControl.X) + (c * secondControl.X) + (d * to.X),
            (a * from.Y) + (b * firstControl.Y) + (c * secondControl.Y) + (d * to.Y));
    }
}
