namespace Macshot.Windows.Core.Capture;

/// <summary>
/// The set of attached displays, and the conversions between the three coordinate
/// spaces the capture pipeline uses.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// <b>Pointer space</b> — WinUI layout units inside one overlay window. Scaled by
/// that window's display, so it is only meaningful together with a
/// <see cref="CaptureMonitor"/>.
/// </description></item>
/// <item><description>
/// <b>Virtual space</b> — physical pixels, origin at the primary display's
/// top-left. Can be negative.
/// </description></item>
/// <item><description>
/// <b>Frame space</b> — physical pixels, origin at <see cref="VirtualBounds"/>'s
/// top-left, so always non-negative. This is what indexes a captured buffer and
/// what every annotation stores.
/// </description></item>
/// </list>
/// Going from pointer straight to frame while ignoring which display the pointer
/// is on is the mixed-DPI bug this type exists to prevent.
/// </remarks>
public sealed class MonitorLayout
{
    public MonitorLayout(IEnumerable<CaptureMonitor> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        var attached = monitors.ToArray();
        if (attached.Length == 0)
        {
            throw new ArgumentException("At least one display is required.", nameof(monitors));
        }

        var bounds = default(CaptureRegion);
        foreach (var monitor in attached)
        {
            ArgumentNullException.ThrowIfNull(monitor);
            monitor.Validate();
            bounds = bounds.Union(monitor.Bounds);
        }

        Monitors = attached;
        VirtualBounds = bounds;
    }

    public IReadOnlyList<CaptureMonitor> Monitors { get; }

    /// <summary>The union of every display, in virtual space.</summary>
    public CaptureRegion VirtualBounds { get; }

    /// <summary>
    /// The primary display, falling back to the first attached one when Windows
    /// reports no primary flag, so callers never have to null check.
    /// </summary>
    public CaptureMonitor Primary =>
        Monitors.FirstOrDefault(monitor => monitor.IsPrimary) ?? Monitors[0];

    public CaptureMonitor? MonitorAt(CapturePoint virtualPoint) =>
        Monitors.FirstOrDefault(monitor => monitor.Contains(virtualPoint));

    public CapturePoint VirtualToFrame(CapturePoint virtualPoint) =>
        new(virtualPoint.X - VirtualBounds.X, virtualPoint.Y - VirtualBounds.Y);

    public CaptureRegion VirtualToFrame(CaptureRegion virtualRegion) =>
        new(
            virtualRegion.X - VirtualBounds.X,
            virtualRegion.Y - VirtualBounds.Y,
            virtualRegion.Width,
            virtualRegion.Height);

    /// <summary>Where a display's pixels live inside a full virtual-desktop capture.</summary>
    public CaptureRegion FrameRegionOf(CaptureMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return VirtualToFrame(monitor.Bounds);
    }

    /// <summary>
    /// Maps a pointer position inside <paramref name="monitor"/>'s overlay window
    /// to frame space. This is the only supported way to turn input into pixels.
    /// </summary>
    public CapturePoint PointerToFrame(CaptureMonitor monitor, double dipX, double dipY)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return VirtualToFrame(monitor.PointerToVirtual(dipX, dipY));
    }

    public CaptureRegion PointerToFrame(CaptureMonitor monitor, CaptureRegion dipRegion)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return VirtualToFrame(monitor.PointerToVirtual(dipRegion));
    }

    public CapturePoint FrameToVirtual(CapturePoint framePoint) =>
        new(framePoint.X + VirtualBounds.X, framePoint.Y + VirtualBounds.Y);

    /// <summary>
    /// The inverse of <see cref="VirtualToFrame(CaptureRegion)"/>: where a region an
    /// overlay chose sits on the desktop itself.
    /// </summary>
    /// <remarks>
    /// Needed by everything that aims something other than a screenshot at the region —
    /// a recording, a scroll capture — because those talk to a display or a window,
    /// neither of which knows where the virtual-desktop capture began.
    /// </remarks>
    public CaptureRegion FrameToVirtual(CaptureRegion frameRegion) =>
        new(
            frameRegion.X + VirtualBounds.X,
            frameRegion.Y + VirtualBounds.Y,
            frameRegion.Width,
            frameRegion.Height);

    /// <summary>
    /// The inverse of <see cref="PointerToFrame(CaptureMonitor, double, double)"/>,
    /// used to place an annotation stored in frame space back onto one display's
    /// overlay. Drawing has to go through the same per-display scale that input
    /// came through, or a mark lands away from where it was drawn on any display
    /// that is not at 100%.
    /// </summary>
    public CapturePoint FrameToPointer(CaptureMonitor monitor, CapturePoint framePoint)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return monitor.VirtualToPointer(FrameToVirtual(framePoint));
    }
}
