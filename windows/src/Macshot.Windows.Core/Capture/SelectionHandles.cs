namespace Macshot.Windows.Core.Capture;

/// <summary>Which grip of a selection is being dragged, or none.</summary>
public enum SelectionHandle
{
    None,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>
/// The eight grips around a selection: where they sit, which one a point grabs, and
/// what the selection becomes when one is dragged.
/// </summary>
/// <remarks>
/// <para>
/// Kept out of the overlay because every part of it is arithmetic a Windows machine
/// adds nothing to. The overlay draws the rectangles this returns and feeds pointer
/// positions back in.
/// </para>
/// <para>
/// A drag is resolved against the corner opposite the grip rather than by nudging one
/// edge, so dragging past the far side flips the selection instead of collapsing it —
/// which is what every other selection on the platform does, and what a user who
/// overshoots expects.
/// </para>
/// </remarks>
public static class SelectionHandles
{
    /// <summary>
    /// The side of a grip, in layout units. Large enough to grab without care, small
    /// enough that eight of them do not cover a small selection entirely.
    /// </summary>
    /// <remarks>
    /// Layout units rather than frame pixels, because it is a size a hand aims at: ten
    /// points is what macOS draws, and ten frame pixels would be two thirds of that on a
    /// 150% display and half of it on a 200% one. Everything here works in frame pixels,
    /// so each method takes the display's scale and multiplies this by it.
    /// </remarks>
    public const double Size = 10;

    /// <summary>
    /// Below this many grips-widths the edge grips would overlap the corners and leave no
    /// interior to drag the selection by, so only the corners are offered.
    /// </summary>
    private const double CornersOnlyBelow = 4;

    public static IReadOnlyList<SelectionHandle> All { get; } =
    [
        SelectionHandle.TopLeft,
        SelectionHandle.Top,
        SelectionHandle.TopRight,
        SelectionHandle.Right,
        SelectionHandle.BottomRight,
        SelectionHandle.Bottom,
        SelectionHandle.BottomLeft,
        SelectionHandle.Left,
    ];

    private static readonly SelectionHandle[] CornersOnly =
    [
        SelectionHandle.TopLeft,
        SelectionHandle.TopRight,
        SelectionHandle.BottomRight,
        SelectionHandle.BottomLeft,
    ];

    /// <summary>The grips worth drawing for a selection of this size, in draw order.</summary>
    /// <param name="scale">Frame pixels to the layout unit on the selection's display.</param>
    public static IReadOnlyList<SelectionHandle> For(CaptureRegion selection, double scale = 1)
    {
        var smallest = Size * scale * CornersOnlyBelow;
        return selection.Width < smallest || selection.Height < smallest
            ? CornersOnly
            : All;
    }

    /// <summary>The box to draw for one grip, centred on its point of the selection.</summary>
    /// <param name="scale">Frame pixels to the layout unit on the selection's display.</param>
    public static CaptureRegion RectangleOf(CaptureRegion selection, SelectionHandle handle, double scale = 1)
    {
        var (x, y) = AnchorOf(selection, handle);
        var side = Size * scale;
        return new CaptureRegion(x - (side / 2), y - (side / 2), side, side);
    }

    /// <summary>
    /// Which grip a point grabs, or <see cref="SelectionHandle.None"/>.
    /// </summary>
    /// <remarks>
    /// Corners are tested before edges. Their rectangles overlap at the ends of every
    /// edge, and a user aiming at a corner who lands one pixel inside the edge's box
    /// means the corner.
    /// </remarks>
    /// <param name="scale">Frame pixels to the layout unit on the selection's display.</param>
    /// <param name="tolerance">
    /// Slack around a grip, in layout units, so a point a hair outside one still grabs it.
    /// Scaled along with the grip, or a grip would be easier to hit on a 100% display than
    /// the same-looking grip on a 200% one.
    /// </param>
    public static SelectionHandle HitTest(
        CaptureRegion selection,
        CapturePoint point,
        double scale = 1,
        double tolerance = 2)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);

        var offered = For(selection, scale);
        foreach (var handle in offered)
        {
            if (IsCorner(handle) && Grabs(selection, handle, point, scale, tolerance * scale))
            {
                return handle;
            }
        }

        foreach (var handle in offered)
        {
            if (!IsCorner(handle) && Grabs(selection, handle, point, scale, tolerance * scale))
            {
                return handle;
            }
        }

        return SelectionHandle.None;
    }

    /// <summary>
    /// The selection that results from dragging <paramref name="handle"/> to
    /// <paramref name="point"/>, normalized so it is never inside out.
    /// </summary>
    /// <param name="square">
    /// Constrains the result to a square, which is what Shift asks for. Taken from the
    /// larger of the two sides so the pointer stays inside the result rather than the
    /// selection shrinking away from it.
    /// </param>
    public static CaptureRegion Resize(
        CaptureRegion selection,
        SelectionHandle handle,
        CapturePoint point,
        bool square = false)
    {
        if (handle == SelectionHandle.None)
        {
            return selection;
        }

        var left = selection.X;
        var top = selection.Y;
        var right = selection.Right;
        var bottom = selection.Bottom;

        if (handle is SelectionHandle.TopLeft or SelectionHandle.Left or SelectionHandle.BottomLeft)
        {
            left = point.X;
        }

        if (handle is SelectionHandle.TopRight or SelectionHandle.Right or SelectionHandle.BottomRight)
        {
            right = point.X;
        }

        if (handle is SelectionHandle.TopLeft or SelectionHandle.Top or SelectionHandle.TopRight)
        {
            top = point.Y;
        }

        if (handle is SelectionHandle.BottomLeft or SelectionHandle.Bottom or SelectionHandle.BottomRight)
        {
            bottom = point.Y;
        }

        var resized = CaptureRegion.FromPoints(left, top, right, bottom);
        return square && IsCorner(handle) ? Squared(resized, selection, handle) : resized;
    }

    /// <summary>
    /// Moves the whole selection, which is the drag that starts inside it rather than
    /// on a grip.
    /// </summary>
    public static CaptureRegion Translate(CaptureRegion selection, double deltaX, double deltaY) =>
        new(selection.X + deltaX, selection.Y + deltaY, selection.Width, selection.Height);

    /// <summary>
    /// Keeps a selection inside <paramref name="bounds"/> without changing its size,
    /// which is what moving one towards an edge should do.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only for moving. Resizing into an edge has to stop the edge being dragged and
    /// leave the opposite one where the user put it, which is
    /// <see cref="CaptureRegion.Intersect"/> rather than this.
    /// </para>
    /// <para>
    /// Takes a region rather than a width and a height because the bounds are one
    /// display inside the virtual desktop, and every display but the first starts
    /// somewhere other than the origin.
    /// </para>
    /// </remarks>
    public static CaptureRegion ClampTo(CaptureRegion selection, CaptureRegion bounds)
    {
        var width = Math.Min(selection.Width, bounds.Width);
        var height = Math.Min(selection.Height, bounds.Height);
        var x = Math.Clamp(selection.X, bounds.X, Math.Max(bounds.X, bounds.Right - width));
        var y = Math.Clamp(selection.Y, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - height));
        return new CaptureRegion(x, y, width, height);
    }

    public static bool IsCorner(SelectionHandle handle) =>
        handle is SelectionHandle.TopLeft or SelectionHandle.TopRight
            or SelectionHandle.BottomRight or SelectionHandle.BottomLeft;

    private static bool Grabs(
        CaptureRegion selection,
        SelectionHandle handle,
        CapturePoint point,
        double scale,
        double tolerance)
    {
        var box = RectangleOf(selection, handle, scale);
        return point.X >= box.X - tolerance
            && point.X <= box.Right + tolerance
            && point.Y >= box.Y - tolerance
            && point.Y <= box.Bottom + tolerance;
    }

    private static (double X, double Y) AnchorOf(CaptureRegion selection, SelectionHandle handle)
    {
        var centerX = selection.X + selection.Width / 2;
        var centerY = selection.Y + selection.Height / 2;

        return handle switch
        {
            SelectionHandle.TopLeft => (selection.X, selection.Y),
            SelectionHandle.Top => (centerX, selection.Y),
            SelectionHandle.TopRight => (selection.Right, selection.Y),
            SelectionHandle.Right => (selection.Right, centerY),
            SelectionHandle.BottomRight => (selection.Right, selection.Bottom),
            SelectionHandle.Bottom => (centerX, selection.Bottom),
            SelectionHandle.BottomLeft => (selection.X, selection.Bottom),
            SelectionHandle.Left => (selection.X, centerY),
            _ => (centerX, centerY),
        };
    }

    /// <summary>
    /// Squares a resized selection about the corner that stayed put, which is the one
    /// opposite the grip being dragged.
    /// </summary>
    private static CaptureRegion Squared(CaptureRegion resized, CaptureRegion original, SelectionHandle handle)
    {
        var side = Math.Max(resized.Width, resized.Height);

        var anchorX = handle is SelectionHandle.TopLeft or SelectionHandle.BottomLeft
            ? original.Right
            : original.X;
        var anchorY = handle is SelectionHandle.TopLeft or SelectionHandle.TopRight
            ? original.Bottom
            : original.Y;

        var left = resized.Right <= anchorX ? anchorX - side : anchorX;
        var top = resized.Bottom <= anchorY ? anchorY - side : anchorY;
        return new CaptureRegion(left, top, side, side);
    }
}
