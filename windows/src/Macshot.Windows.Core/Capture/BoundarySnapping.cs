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

/// <summary>What an explicit auto-adjust made of the selection it was asked about.</summary>
/// <remarks>
/// Three answers rather than a bare "did it move", because macshot says a different thing
/// for each (<c>OverlayView.swift:7280-7285</c>) and the user cannot tell them apart from
/// the selection alone: an edge found and already under the selection means aim, and no
/// edge found at all means there was nothing there to aim at.
/// </remarks>
public enum SelectionFitOutcome
{
    /// <summary>
    /// The selection is too small to be worth refining, and nothing was even looked for.
    /// </summary>
    TooSmall,

    /// <summary>At least one edge moved onto a line in the picture.</summary>
    Adjusted,

    /// <summary>Lines were found, and the selection was already on them.</summary>
    AlreadyAligned,

    /// <summary>Nothing within reach of any of the four edges was a line.</summary>
    NothingNearby,
}

/// <summary>A selection after an explicit auto-adjust, and what that came to.</summary>
public readonly record struct SelectionFit(CaptureRegion Region, SelectionFitOutcome Outcome);

/// <summary>
/// Pulling a selection onto the edges already in the picture — macshot's boundary snap,
/// <c>OverlayView.swift:7082</c>.
/// </summary>
/// <remarks>
/// Three gestures reach a selection and each snaps differently. Dragging a grip moves one
/// or two edges, and only those may land on a line. Dragging the whole selection cannot
/// change its size, so the closer of its two edges wins the axis and the rest follows.
/// Dragging a new selection out moves one corner while the other stays where the press
/// landed. <see cref="Fit"/> is the fourth way in and the only one that is not a drag.
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
    /// The smallest side <see cref="Fit"/> will work on, and the smallest it will leave —
    /// macshot's <c>minimumSize</c>, in layout units.
    /// </summary>
    private const double FitMinimum = 4;

    /// <summary>
    /// How much of its own side <see cref="Fit"/> searches out from each edge, and the
    /// floor and ceiling that keeps in range — macshot's <c>0.30</c>, 48 and 160.
    /// </summary>
    /// <remarks>
    /// Far wider than <see cref="Radius"/> on purpose. A drag snap has to lose to the
    /// user's aim, because their hand is on the thing it is competing with; this is an
    /// explicit ask about a selection they have already let go of, and a rough rectangle
    /// thrown around a panel can sit a long way outside it.
    /// </remarks>
    private const double FitProportion = 0.30;

    private const double FitFloor = 48;

    private const double FitCeiling = 160;

    /// <summary>
    /// How much of each end of an edge is left out of the scoring — macshot's <c>0.15</c>.
    /// </summary>
    /// <remarks>
    /// A window's border stops short at its rounded corners, and a rough selection leaves
    /// uneven padding at the ends of every side. Scored over the whole span, either of
    /// those drops the support fraction under the bar and disqualifies an edge that is
    /// plainly continuous where it matters.
    /// </remarks>
    private const double FitSpanInset = 0.15;

    /// <summary>
    /// How far an edge has to have travelled before the result counts as a change, in
    /// layout units — macshot's <c>0.25</c>.
    /// </summary>
    private const double FitMoved = 0.25;

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
    /// Every edge of a finished selection taken out — or in — to the nearest real border
    /// in the picture, which is what the auto-adjust command asks for.
    /// </summary>
    /// <param name="scale">
    /// Frame pixels to the layout unit the constants above are written in, so the reach is
    /// the same distance on screen whatever the display is running at.
    /// </param>
    /// <remarks>
    /// <para>
    /// macshot's <c>autoAdjustSelection()</c>. It deliberately does not consult the
    /// boundary-snap setting and does not touch the drag-time radius: this is one explicit
    /// press, and someone who turned the drag snap off because it fought their aim has not
    /// asked for this to stop working too.
    /// </para>
    /// <para>
    /// <paramref name="index"/> is required rather than nullable, unlike every other entry
    /// point here. For a drag, no index means "leave the selection where the pointer put
    /// it" and there is nothing to say; for a command the user pressed, it means the answer
    /// is not ready yet, which is a different thing and the caller has to tell them so.
    /// </para>
    /// </remarks>
    public static SelectionFit Fit(CaptureRegion region, BoundarySnapIndex index, double scale)
    {
        ArgumentNullException.ThrowIfNull(index);

        var minimum = FitMinimum * scale;
        if (region.Width < minimum || region.Height < minimum)
        {
            return new SelectionFit(region, SelectionFitOutcome.TooSmall);
        }

        var across = Reach(region.Width, scale);
        var down = Reach(region.Height, scale);
        var insetX = region.Width * FitSpanInset;
        var insetY = region.Height * FitSpanInset;
        var spanTop = region.Y + insetY;
        var spanBottom = region.Bottom - insetY;
        var spanLeft = region.X + insetX;
        var spanRight = region.Right - insetX;

        var left = index.NearestVertical(region.X, spanTop, spanBottom, across);
        var right = index.NearestVertical(region.Right, spanTop, spanBottom, across);
        var top = index.NearestHorizontal(region.Y, spanLeft, spanRight, down);
        var bottom = index.NearestHorizontal(region.Bottom, spanLeft, spanRight, down);

        var toLeft = left?.Position ?? region.X;
        var toRight = right?.Position ?? region.Right;
        var toTop = top?.Position ?? region.Y;
        var toBottom = bottom?.Position ?? region.Bottom;

        // Each axis stands or falls on its own, and only if what it would leave is still a
        // selection: two edges that both found the same line would otherwise close it up.
        var fitted = region;
        if (toRight - toLeft >= minimum)
        {
            fitted = new CaptureRegion(toLeft, fitted.Y, toRight - toLeft, fitted.Height);
        }

        if (toBottom - toTop >= minimum)
        {
            fitted = new CaptureRegion(fitted.X, toTop, fitted.Width, toBottom - toTop);
        }

        var moved = FitMoved * scale;
        var changed = Math.Abs(fitted.X - region.X) > moved
            || Math.Abs(fitted.Right - region.Right) > moved
            || Math.Abs(fitted.Y - region.Y) > moved
            || Math.Abs(fitted.Bottom - region.Bottom) > moved;

        if (changed)
        {
            return new SelectionFit(fitted, SelectionFitOutcome.Adjusted);
        }

        var found = left is not null || right is not null || top is not null || bottom is not null;

        // The original rather than the fitted one: nothing moved far enough to be worth
        // reporting, so nothing should move at all.
        return new SelectionFit(
            region,
            found ? SelectionFitOutcome.AlreadyAligned : SelectionFitOutcome.NothingNearby);
    }

    /// <summary>How far out from one edge to look, given the side it belongs to.</summary>
    private static double Reach(double side, double scale) => Math.Min(
        FitCeiling * scale,
        Math.Max(FitFloor * scale, side * FitProportion));

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
