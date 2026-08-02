using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Capture;

/// <summary>
/// A selection after the lines in the picture have had their say, and the lines to draw
/// where it landed on one.
/// </summary>
public readonly record struct BoundarySnap(CaptureRegion Region, double? GuideX, double? GuideY)
{
    /// <summary>The selection untouched, which is what "nothing was near" looks like.</summary>
    public static BoundarySnap Of(CaptureRegion region) => new(region, null, null);
}

/// <summary>
/// Pulling a selection onto the edges already in the picture — macshot's boundary snap,
/// <c>OverlayView.swift:7082</c>.
/// </summary>
/// <remarks>
/// Three gestures reach a selection and each snaps differently. Dragging a grip moves one
/// or two edges, and only those may land on a line. Dragging the whole selection cannot
/// change its size, so the closer of its two edges wins the axis and the rest follows.
/// Dragging a new selection out moves one corner while the other stays where the press
/// landed.
/// </remarks>
public static class BoundarySnapping
{
    /// <summary>
    /// How near a line has to be before an edge is taken onto it, in layout units —
    /// macshot's <c>boundarySnapRadiusPoints</c>. Multiply by the display's scale for
    /// frame pixels.
    /// </summary>
    /// <remarks>
    /// Four rather than the five marks line up within: this one moves what the user is
    /// pointing at onto something they can see, so it has to lose to their aim as soon as
    /// they are visibly not aiming at it.
    /// </remarks>
    public const double Radius = 4;

    /// <summary>The thinnest a snap may leave a selection, so it cannot close it up.</summary>
    private const double Minimum = 1;

    /// <summary>
    /// The selection a grip drag lands on, with the dragged edges pulled onto any lines
    /// beneath them.
    /// </summary>
    public static BoundarySnap Resize(
        CaptureRegion region,
        SelectionHandle handle,
        BoundarySnapIndex? index,
        double radius)
    {
        if (index is null || handle == SelectionHandle.None)
        {
            return BoundarySnap.Of(region);
        }

        var left = region.X;
        var top = region.Y;
        var right = region.Right;
        var bottom = region.Bottom;
        double? guideX = null;
        double? guideY = null;

        if (handle is (SelectionHandle.Left or SelectionHandle.TopLeft or SelectionHandle.BottomLeft)
            && index.NearestVertical(left, top, bottom, radius) is { } onLeft
            && onLeft.Position <= right - Minimum)
        {
            left = onLeft.Position;
            guideX = onLeft.Position;
        }

        if (handle is (SelectionHandle.Right or SelectionHandle.TopRight or SelectionHandle.BottomRight)
            && index.NearestVertical(right, top, bottom, radius) is { } onRight
            && onRight.Position >= left + Minimum)
        {
            right = onRight.Position;
            guideX = onRight.Position;
        }

        if (handle is (SelectionHandle.Top or SelectionHandle.TopLeft or SelectionHandle.TopRight)
            && index.NearestHorizontal(top, left, right, radius) is { } onTop
            && onTop.Position <= bottom - Minimum)
        {
            top = onTop.Position;
            guideY = onTop.Position;
        }

        if (handle is (SelectionHandle.Bottom or SelectionHandle.BottomLeft or SelectionHandle.BottomRight)
            && index.NearestHorizontal(bottom, left, right, radius) is { } onBottom
            && onBottom.Position >= top + Minimum)
        {
            bottom = onBottom.Position;
            guideY = onBottom.Position;
        }

        return new BoundarySnap(CaptureRegion.FromPoints(left, top, right, bottom), guideX, guideY);
    }

    /// <summary>
    /// The selection a move lands on: shifted so that whichever of its edges is nearest a
    /// line sits on it, and not resized.
    /// </summary>
    public static BoundarySnap Move(CaptureRegion region, BoundarySnapIndex? index, double radius)
    {
        if (index is null)
        {
            return BoundarySnap.Of(region);
        }

        var (dx, guideX) = Shift(
            index.NearestVertical(region.X, region.Y, region.Bottom, radius), region.X,
            index.NearestVertical(region.Right, region.Y, region.Bottom, radius), region.Right);

        var (dy, guideY) = Shift(
            index.NearestHorizontal(region.Y, region.X, region.Right, radius), region.Y,
            index.NearestHorizontal(region.Bottom, region.X, region.Right, radius), region.Bottom);

        return new BoundarySnap(
            SelectionHandles.Translate(region, dx, dy),
            guideX,
            guideY);
    }

    /// <summary>
    /// The selection being dragged out, with the corner under the pointer pulled onto any
    /// lines beneath it. <paramref name="anchor"/> is where the press landed and does not
    /// move.
    /// </summary>
    public static BoundarySnap Corner(
        CapturePoint anchor,
        CapturePoint moving,
        BoundarySnapIndex? index,
        double radius)
    {
        var region = CaptureRegion.FromPoints(anchor.X, anchor.Y, moving.X, moving.Y);

        if (index is null)
        {
            return BoundarySnap.Of(region);
        }

        var x = moving.X;
        var y = moving.Y;
        double? guideX = null;
        double? guideY = null;

        if (index.NearestVertical(x, region.Y, region.Bottom, radius) is { } vertical)
        {
            x = vertical.Position;
            guideX = vertical.Position;
        }

        if (index.NearestHorizontal(y, region.X, region.Right, radius) is { } horizontal)
        {
            y = horizontal.Position;
            guideY = horizontal.Position;
        }

        return new BoundarySnap(
            CaptureRegion.FromPoints(anchor.X, anchor.Y, x, y),
            guideX,
            guideY);
    }

    /// <summary>
    /// How far to move an axis so that the nearer of the two edges lands on its line.
    /// </summary>
    /// <remarks>
    /// The nearer, because both edges of a moved selection are candidates and only one
    /// shift is possible: taking the smaller one puts the selection on the line the user
    /// was closest to, which is the one they were aiming at.
    /// </remarks>
    private static (double Shift, double? Guide) Shift(
        BoundaryHit? first,
        double firstEdge,
        BoundaryHit? second,
        double secondEdge)
    {
        var shift = 0.0;
        double? guide = null;

        if (first is { } near)
        {
            shift = near.Position - firstEdge;
            guide = near.Position;
        }

        if (second is { } far && (guide is null || Math.Abs(far.Position - secondEdge) < Math.Abs(shift)))
        {
            shift = far.Position - secondEdge;
            guide = far.Position;
        }

        return (shift, guide);
    }
}
