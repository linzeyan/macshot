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
    /// The side of a grip, in frame pixels. Large enough to grab without care, small
    /// enough that eight of them do not cover a small selection entirely.
    /// </summary>
    public const double Size = 10;

    /// <summary>
    /// Below this the edge grips would overlap the corners and leave no interior to
    /// drag the selection by, so only the corners are offered.
    /// </summary>
    private const double CornersOnlyBelow = Size * 4;

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
    public static IReadOnlyList<SelectionHandle> For(CaptureRegion selection)
    {
        return selection.Width < CornersOnlyBelow || selection.Height < CornersOnlyBelow
            ? CornersOnly
            : All;
    }

    /// <summary>The square to draw for one grip, centred on its point of the selection.</summary>
    public static CaptureRegion RectangleOf(CaptureRegion selection, SelectionHandle handle)
    {
        var (x, y) = AnchorOf(selection, handle);
        return new CaptureRegion(x - Size / 2, y - Size / 2, Size, Size);
    }

    /// <summary>
    /// Which grip a point grabs, or <see cref="SelectionHandle.None"/>.
    /// </summary>
    /// <remarks>
    /// Corners are tested before edges. Their rectangles overlap at the ends of every
    /// edge, and a user aiming at a corner who lands one pixel inside the edge's box
    /// means the corner.
    /// </remarks>
    public static SelectionHandle HitTest(CaptureRegion selection, CapturePoint point, double tolerance = 2)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);

        var offered = For(selection);
        foreach (var handle in offered)
        {
            if (IsCorner(handle) && Grabs(selection, handle, point, tolerance))
            {
                return handle;
            }
        }

        foreach (var handle in offered)
        {
            if (!IsCorner(handle) && Grabs(selection, handle, point, tolerance))
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
    /// Keeps a selection inside the frame without changing its size, which is what
    /// dragging one towards an edge should do.
    /// </summary>
    public static CaptureRegion ClampTo(CaptureRegion selection, int width, int height)
    {
        var clampedWidth = Math.Min(selection.Width, width);
        var clampedHeight = Math.Min(selection.Height, height);
        var x = Math.Clamp(selection.X, 0, Math.Max(0, width - clampedWidth));
        var y = Math.Clamp(selection.Y, 0, Math.Max(0, height - clampedHeight));
        return new CaptureRegion(x, y, clampedWidth, clampedHeight);
    }

    public static bool IsCorner(SelectionHandle handle) =>
        handle is SelectionHandle.TopLeft or SelectionHandle.TopRight
            or SelectionHandle.BottomRight or SelectionHandle.BottomLeft;

    private static bool Grabs(CaptureRegion selection, SelectionHandle handle, CapturePoint point, double tolerance)
    {
        var box = RectangleOf(selection, handle);
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
