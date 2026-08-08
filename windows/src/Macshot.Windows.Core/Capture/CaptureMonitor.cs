namespace Macshot.Windows.Core.Capture;

/// <summary>
/// One display, described in virtual-screen physical pixels.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Bounds"/> uses the Windows virtual-screen coordinate system, whose
/// origin is the primary display's top-left corner. A display placed left of or
/// above the primary therefore has negative coordinates. Converting those to the
/// zero-based pixel offsets of a captured frame is <see cref="MonitorLayout"/>'s
/// job, not this type's.
/// </para>
/// <para>
/// <see cref="Scale"/> is that display's DPI scaling (1.0 at 96 DPI, 1.5 at 150%).
/// It is per display, which is why pointer input can only be mapped to pixels
/// through the specific monitor the pointer is on. See
/// <c>docs/windows-port/architecture.md</c>, decision D6.
/// </para>
/// </remarks>
public sealed record CaptureMonitor(string DeviceName, CaptureRegion Bounds, double Scale, bool IsPrimary = false)
{
    private readonly CaptureRegion? _workArea;

    /// <summary>
    /// The part of the display not covered by the taskbar, in virtual space. It is
    /// an opt-in property rather than a constructor parameter so the existing call
    /// sites, which only care about geometry, keep working; when it is not supplied
    /// it falls back to <see cref="Bounds"/>, which is correct for a full-screen
    /// overlay and only slightly wrong for a floating panel.
    /// </summary>
    public CaptureRegion WorkArea
    {
        get => _workArea ?? Bounds;
        init => _workArea = value;
    }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DeviceName, nameof(DeviceName));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Scale, nameof(Scale));

        if (Bounds.IsEmpty)
        {
            throw new ArgumentException($"Display '{DeviceName}' reported empty bounds.", nameof(Bounds));
        }
    }

    /// <summary>The display's width in WinUI layout units.</summary>
    public double DipWidth => Bounds.Width / Scale;

    /// <summary>The display's height in WinUI layout units.</summary>
    public double DipHeight => Bounds.Height / Scale;

    public bool Contains(CapturePoint virtualPoint) => Bounds.Contains(virtualPoint.X, virtualPoint.Y);

    /// <summary>Maps a pointer position inside a window covering this display to virtual-screen pixels.</summary>
    /// <remarks>
    /// Landed on a whole pixel, because that is what the answer means: the caller is
    /// asking which pixel the pointer is over, and there is no half of one. WinUI reports
    /// the position in layout units, so at any scale that is not a whole number the
    /// multiplication comes back a hair off — 300 physical pixels at 175% arrives as
    /// 299.99999999999994. Left as it was, that dust reached the delivered image: a
    /// region the size box called 1000 × 500 was cropped outwards to 1002 × 501, because
    /// the crop rounds a fractional rectangle away from its middle to avoid losing a
    /// column the user chose.
    /// </remarks>
    public CapturePoint PointerToVirtual(double dipX, double dipY)
    {
        return new CapturePoint(Whole(Bounds.X + dipX * Scale), Whole(Bounds.Y + dipY * Scale));
    }

    /// <summary>Maps virtual-screen pixels back to a pointer position, for placing overlay chrome.</summary>
    public CapturePoint VirtualToPointer(CapturePoint virtualPoint)
    {
        return new CapturePoint((virtualPoint.X - Bounds.X) / Scale, (virtualPoint.Y - Bounds.Y) / Scale);
    }

    /// <summary>
    /// A virtual-space region as an offset into this display's own pixels, clipped to it.
    /// </summary>
    /// <remarks>
    /// This is the space a display's capture item is in: the compositor hands over that
    /// display and nothing else, so a crop of it starts at the display's top-left corner
    /// rather than at the virtual desktop's. Clipped rather than merely shifted, because
    /// the caller is cropping a buffer and a rectangle that overhangs the display would
    /// index past the end of it.
    /// </remarks>
    public CaptureRegion VirtualToLocal(CaptureRegion virtualRegion)
    {
        var clipped = virtualRegion.Intersect(Bounds);
        return clipped.IsEmpty
            ? default
            : new CaptureRegion(clipped.X - Bounds.X, clipped.Y - Bounds.Y, clipped.Width, clipped.Height);
    }

    /// <summary>
    /// The pixel a coordinate falls on, halves going the same way whichever side of the
    /// primary display the coordinate is — which <c>Math.Round</c> would not do, since
    /// away-from-zero sends a half up on the right of the desktop and down on the left.
    /// </summary>
    private static double Whole(double coordinate) => Math.Floor(coordinate + 0.5);
}
