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
    public CapturePoint PointerToVirtual(double dipX, double dipY)
    {
        return new CapturePoint(Bounds.X + dipX * Scale, Bounds.Y + dipY * Scale);
    }

    /// <summary>Maps virtual-screen pixels back to a pointer position, for placing overlay chrome.</summary>
    public CapturePoint VirtualToPointer(CapturePoint virtualPoint)
    {
        return new CapturePoint((virtualPoint.X - Bounds.X) / Scale, (virtualPoint.Y - Bounds.Y) / Scale);
    }

    public CaptureRegion PointerToVirtual(CaptureRegion dipRegion)
    {
        return new CaptureRegion(
            Bounds.X + dipRegion.X * Scale,
            Bounds.Y + dipRegion.Y * Scale,
            dipRegion.Width * Scale,
            dipRegion.Height * Scale);
    }
}
