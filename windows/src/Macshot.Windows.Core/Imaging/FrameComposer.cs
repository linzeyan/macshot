using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// Assembles one virtual-desktop frame out of per-display captures.
/// </summary>
/// <remarks>
/// <para>
/// <c>BitBlt</c> hands back the whole virtual screen in a single call, but
/// <c>Windows.Graphics.Capture</c> works one <c>GraphicsCaptureItem</c> at a time —
/// one per display. Everything downstream, from the per-monitor overlay crop to the
/// annotation coordinates, is written against a single frame whose origin is the
/// virtual desktop's top-left, so the displays are put back together here rather
/// than that assumption being unpicked everywhere else. See
/// <c>docs/windows-port/architecture.md</c>, decisions D5 and D6.
/// </para>
/// <para>
/// The buffer starts opaque black. Display layouts are not rectangular — an L of
/// three monitors leaves a corner no display covers — and that corner has to be a
/// defined colour rather than whatever the allocation happened to contain.
/// </para>
/// </remarks>
public sealed class FrameComposer
{
    private readonly MonitorLayout _layout;
    private readonly byte[] _pixels;

    public FrameComposer(MonitorLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        _layout = layout;
        Width = (int)Math.Round(layout.VirtualBounds.Width);
        Height = (int)Math.Round(layout.VirtualBounds.Height);
        if (Width <= 0 || Height <= 0)
        {
            throw new ArgumentException("The display layout has no area to capture.", nameof(layout));
        }

        _pixels = new byte[checked(Width * Height * 4)];
        for (var index = 3; index < _pixels.Length; index += 4)
        {
            _pixels[index] = byte.MaxValue;
        }
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The virtual-screen origin the composed frame starts at.</summary>
    public int VirtualX => (int)Math.Round(_layout.VirtualBounds.X);

    public int VirtualY => (int)Math.Round(_layout.VirtualBounds.Y);

    /// <summary>
    /// Copies one display's captured pixels into the position its bounds give it.
    /// </summary>
    /// <remarks>
    /// A capture whose size disagrees with what Windows reported for the display is
    /// copied as far as the two overlap instead of being refused. The sizes can
    /// differ by a pixel from rounding on a scaled display, and losing the whole
    /// screenshot over that would be a worse answer than a one-pixel edge left at the
    /// background colour.
    /// </remarks>
    public void Draw(CaptureMonitor monitor, int sourceWidth, int sourceHeight, ReadOnlySpan<byte> bgraPixels)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);

        if (bgraPixels.Length != checked(sourceWidth * sourceHeight * 4))
        {
            throw new ArgumentException("The pixel buffer does not match the source dimensions.", nameof(bgraPixels));
        }

        var target = _layout.FrameRegionOf(monitor);
        var left = (int)Math.Round(target.X);
        var top = (int)Math.Round(target.Y);
        var columns = Math.Min(sourceWidth, Width - left);
        var rows = Math.Min(sourceHeight, Height - top);
        if (columns <= 0 || rows <= 0)
        {
            return;
        }

        for (var row = 0; row < rows; row++)
        {
            var from = row * sourceWidth * 4;
            var to = (((top + row) * Width) + left) * 4;
            bgraPixels.Slice(from, columns * 4).CopyTo(_pixels.AsSpan(to));
        }
    }

    /// <summary>The composed frame, top-down BGRA.</summary>
    public byte[] ToImage() => _pixels;
}
