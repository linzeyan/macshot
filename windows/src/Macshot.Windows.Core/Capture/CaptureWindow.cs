namespace Macshot.Windows.Core.Capture;

/// <summary>
/// One on-screen window: where it is, and which one it is.
/// </summary>
/// <remarks>
/// Snapping used to answer with a rectangle alone, which is all that cropping a
/// window out of the desktop screenshot needs. Capturing the window itself has to
/// name it, and the name belongs to the platform — an <c>HWND</c> on Windows — so
/// it is carried as an opaque number rather than as a type Core would have to
/// reference the OS to describe.
/// </remarks>
/// <param name="Title">
/// What the window calls itself, for the <c>{window}</c> filename token. Null or empty
/// for a window with no title, which resolves to nothing in a name rather than to the
/// word "null" — macshot does the same, <c>FilenameFormatter.swift:17</c>.
/// </param>
public readonly record struct CaptureWindow(long Id, CaptureRegion Bounds, string? Title = null)
{
    /// <summary>
    /// The same window with its bounds clipped to <paramref name="bounds"/>. The
    /// identity survives the clip: which window was pointed at does not change
    /// because part of it hangs off the edge of the capture.
    /// </summary>
    public CaptureWindow ClipTo(CaptureRegion bounds) =>
        this with { Bounds = Bounds.Intersect(bounds) };
}
