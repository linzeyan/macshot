namespace Macshot.Windows.Core.Capture;

/// <summary>
/// Where the recording panel sits relative to what is being recorded.
/// </summary>
/// <remarks>
/// It belongs to the region rather than to the screen, the way the size box does: a
/// panel in the corner of the desktop says a recording is running somewhere, and a panel
/// against the region says which one. Above it by preference, below when there is no room
/// above, and always inside the work area — a panel under the taskbar cannot be stopped.
/// </remarks>
public static class HudPlacement
{
    /// <summary>How far off the region's edge the panel sits.</summary>
    public const double Gap = 8;

    /// <summary>
    /// Places a panel of <paramref name="size"/> against <paramref name="region"/>,
    /// right-aligned to it as macshot's is: the left of a region is where the content
    /// being demonstrated usually starts.
    /// </summary>
    /// <param name="region">What is being recorded, in virtual-screen pixels.</param>
    /// <param name="workArea">What the panel must stay inside.</param>
    /// <param name="size">How big the panel is; its position is ignored.</param>
    public static CaptureRegion For(CaptureRegion region, CaptureRegion workArea, CaptureRegion size)
    {
        var above = region.Y - Gap - size.Height;
        var below = region.Bottom + Gap;

        // Below only when above will not fit at all. A region against the top of the
        // screen is the common case, and moving the panel down there beats putting it
        // half off the top.
        var top = above >= workArea.Y ? above : below;

        return new CaptureRegion(
            Math.Clamp(region.Right - size.Width, workArea.X, Math.Max(workArea.X, workArea.Right - size.Width)),
            Math.Clamp(top, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - size.Height)),
            size.Width,
            size.Height);
    }
}
