using Macshot.Windows.Core.Capture;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Point resolves to
// Macshot.Point and does not compile.
using Windows.Foundation;

namespace Macshot.Windows.Rendering;

/// <summary>
/// The mapping between the pixels an annotation is stored in and the layout units a
/// canvas is arranged in.
/// </summary>
/// <remarks>
/// This is the one thing the capture overlay and the editor window genuinely disagree
/// about. An overlay covers a display, so the mapping is that display's scale and its
/// offset into the virtual desktop; an editor holds one image laid out at its own pixel
/// size, so the mapping is nothing at all. Everything else about annotating — the tools,
/// the sprites, the preview, the toolbar — is the same in both, and this interface is
/// what lets it be written once.
/// </remarks>
public interface IFramePlacement
{
    /// <summary>Where a frame-space point sits on the canvas.</summary>
    Point ToLayout(CapturePoint framePoint);

    /// <summary>Which frame pixel a point on the canvas is over.</summary>
    CapturePoint ToFrame(Point layoutPoint);

    /// <summary>
    /// Frame pixels to the layout unit here, which is what a target sized for a hand —
    /// a grab handle, its catchment — has to be multiplied by before it can be measured
    /// against positions in the capture.
    /// </summary>
    double Scale { get; }
}

/// <summary>
/// One display's mapping: the scale and the virtual-desktop offset of the monitor an
/// overlay is covering.
/// </summary>
public sealed class MonitorFramePlacement(MonitorLayout layout, CaptureMonitor monitor) : IFramePlacement
{
    private readonly MonitorLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    private readonly CaptureMonitor _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));

    public Point ToLayout(CapturePoint framePoint)
    {
        var mapped = _layout.FrameToPointer(_monitor, framePoint);
        return new Point(mapped.X, mapped.Y);
    }

    public CapturePoint ToFrame(Point layoutPoint) =>
        _layout.PointerToFrame(_monitor, layoutPoint.X, layoutPoint.Y);

    public double Scale => _monitor.Scale;
}

/// <summary>
/// An image laid out at its size in points — its own scale (<c>CapturedFrame.Scale</c>),
/// not the window's — with nothing moving.
/// </summary>
/// <remarks>
/// <para>
/// macshot's editor shows an <c>NSImage</c> at its point size, so at 100% a capture from a
/// 2x display is one image pixel to a screen pixel and its handles and marks are the size
/// they were in the overlay. Laid out a pixel to a unit instead, the same capture opened
/// at 175% of its size on a 175% display.
/// </para>
/// <para>
/// This is not a zoom factor. Zooming an editor is done by the <c>ScrollViewer</c> around
/// the canvas, and WinUI reports a pointer position relative to the element it is asked
/// about with the zoom already divided out — so a point that arrives here is in canvas
/// units whatever the zoom is, and dividing by the zoom again would put every mark in the
/// wrong place at any zoom but 100%.
/// </para>
/// </remarks>
public sealed class ImageFramePlacement(double scale) : IFramePlacement
{
    public Point ToLayout(CapturePoint framePoint) => new(framePoint.X / scale, framePoint.Y / scale);

    public CapturePoint ToFrame(Point layoutPoint) => new(layoutPoint.X * scale, layoutPoint.Y * scale);

    public double Scale => scale;
}
