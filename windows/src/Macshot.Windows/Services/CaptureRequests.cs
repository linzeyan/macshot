using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Services;

/// <summary>
/// A scroll capture aimed at one window, and optionally at one part of it.
/// </summary>
/// <param name="Window">The window whose wheel is turned. Always needed: scrolling is
/// done by driving a real window, whatever part of it is being kept.</param>
/// <param name="Region">
/// The part of the desktop to stitch, in virtual space, or null for the whole window.
/// Virtual rather than frame space because the window capture reports its own position
/// the same way, and the overlay whose frame space it was chosen in is gone by the time
/// the capture runs.
/// </param>
public sealed record ScrollCaptureRequest(CaptureWindow Window, CaptureRegion? Region = null);

/// <summary>
/// A recording aimed at one display, and optionally at one part of it.
/// </summary>
/// <param name="Monitor">The display whose frames are encoded.</param>
/// <param name="Region">
/// The part of the desktop to keep, in virtual space, or null for the whole display.
/// </param>
public sealed record RecordingRequest(CaptureMonitor Monitor, CaptureRegion? Region = null);
