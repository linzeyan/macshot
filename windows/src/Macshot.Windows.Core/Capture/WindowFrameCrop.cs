namespace Macshot.Windows.Core.Capture;

/// <summary>
/// Finds the visible window inside a frame captured from a window.
/// </summary>
/// <remarks>
/// <para>
/// A window capture item hands back the whole window as the window manager knows
/// it, which since Vista is larger than the window anyone can see: the compositor
/// keeps an invisible resize border outside the frame. Delivered unchanged, a
/// window capture therefore carries a transparent band on three sides that the
/// highlight the user clicked did not include.
/// </para>
/// <para>
/// The band is the difference between the two rectangles Windows reports for the
/// same window, so removing it is arithmetic and belongs here rather than next to
/// the interop that measures them.
/// </para>
/// </remarks>
public static class WindowFrameCrop
{
    /// <summary>
    /// The region of a captured window frame that is the window itself.
    /// </summary>
    /// <param name="windowRect">
    /// The window's outer rectangle, borders included — what the captured frame is
    /// assumed to cover.
    /// </param>
    /// <param name="visibleBounds">The window as drawn, in the same space.</param>
    /// <param name="frameWidth">Width of the captured frame, in pixels.</param>
    /// <param name="frameHeight">Height of the captured frame, in pixels.</param>
    /// <remarks>
    /// The whole frame is returned whenever the assumption cannot be checked out —
    /// either rectangle missing, or a frame that is not the size of the window
    /// rectangle after all. Cropping on a guess would cut into the window; keeping
    /// the frame only leaves the border that was already there.
    /// </remarks>
    public static CaptureRegion Resolve(
        CaptureRegion windowRect,
        CaptureRegion visibleBounds,
        int frameWidth,
        int frameHeight)
    {
        var whole = new CaptureRegion(0, 0, frameWidth, frameHeight);
        if (whole.IsEmpty || windowRect.IsEmpty || visibleBounds.IsEmpty)
        {
            return whole;
        }

        if ((int)Math.Round(windowRect.Width) != frameWidth
            || (int)Math.Round(windowRect.Height) != frameHeight)
        {
            return whole;
        }

        var inset = new CaptureRegion(
            visibleBounds.X - windowRect.X,
            visibleBounds.Y - windowRect.Y,
            visibleBounds.Width,
            visibleBounds.Height);

        var cropped = inset.Intersect(whole);
        return cropped.IsEmpty ? whole : cropped;
    }
}
