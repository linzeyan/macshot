namespace Macshot.Windows.Core.Capture;

/// <summary>
/// Picks the window a pointer is over, out of a front-to-back list of window
/// rectangles, so a click can take that window instead of asking the user to drag
/// out its edges by hand.
/// </summary>
/// <remarks>
/// The list arrives already ordered because z-order is the one thing only the
/// window manager knows. Everything after that is arithmetic, which is why it
/// lives here and can be tested against overlapping rectangles rather than by
/// arranging real windows on a real desktop and looking at the result.
/// </remarks>
public static class WindowSnapper
{
    /// <summary>
    /// The smallest edge, in pixels, a window may present and still be offered.
    /// Slivers are passed over rather than snapped to: a window resting a few
    /// pixels inside the desktop is never what a click there meant.
    /// </summary>
    public const double MinimumEdge = 16;

    /// <summary>
    /// The frontmost window containing <paramref name="point"/>, clipped to
    /// <paramref name="bounds"/>, or <c>null</c> when the pointer is over none of
    /// them. Every rectangle has to be in the same space; the capture pipeline
    /// passes frame space, so what comes back is usable as a selection unchanged.
    /// </summary>
    /// <remarks>
    /// The answer names the window rather than only measuring it, because a click
    /// can now be served two ways: cropped out of the desktop screenshot, or taken
    /// from the window itself. Only the second needs the identity, and this is the
    /// last place that still knows it.
    /// </remarks>
    public static CaptureWindow? Snap(
        IReadOnlyList<CaptureWindow> windowsFrontToBack,
        CapturePoint point,
        CaptureRegion bounds)
    {
        ArgumentNullException.ThrowIfNull(windowsFrontToBack);

        foreach (var window in windowsFrontToBack)
        {
            if (!window.Bounds.Contains(point.X, point.Y))
            {
                continue;
            }

            // A window too small to be worth snapping to does not hide the ones
            // behind it: a sliver lying over a real window should let the click
            // through to the window the user can actually see.
            var visible = window.ClipTo(bounds);
            if (visible.Bounds.Width < MinimumEdge || visible.Bounds.Height < MinimumEdge)
            {
                continue;
            }

            return visible;
        }

        return null;
    }
}
