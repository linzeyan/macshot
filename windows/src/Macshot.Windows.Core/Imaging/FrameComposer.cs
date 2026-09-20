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
/// Where a canvas has to be allocated it starts opaque black, because display
/// layouts are not rectangular — an L of three monitors leaves a corner no display
/// covers, and that corner has to be a defined colour rather than whatever the
/// allocation happened to contain. One display leaves no such corner, and
/// <see cref="Draw"/> allocates nothing at all there.
/// </para>
/// </remarks>
public sealed class FrameComposer
{
    private readonly MonitorLayout _layout;
    private readonly int _length;

    /// <summary>
    /// Null until something needs it, because the commonest layout never does: one
    /// display's capture <em>is</em> the whole canvas, and <see cref="Draw"/> takes that
    /// buffer as its own rather than allocating a second screen to copy it into.
    /// </summary>
    private byte[]? _pixels;

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

        // Sized here rather than where the buffer is made, so a layout too large to
        // hold is refused while a capture is still being planned.
        _length = checked(Width * Height * 4);
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
    /// <para>
    /// A capture whose size disagrees with what Windows reported for the display is
    /// copied as far as the two overlap instead of being refused. The sizes can
    /// differ by a pixel from rounding on a scaled display, and losing the whole
    /// screenshot over that would be a worse answer than a one-pixel edge left at the
    /// background colour.
    /// </para>
    /// <para>
    /// A draw that covers the whole canvas — one display, which is most machines —
    /// keeps <paramref name="bgraPixels"/> instead of copying it, so the composer
    /// becomes free rather than costing a second full screen at the peak of a
    /// capture: 12.9MB at 2038x1588, plus the alpha pre-fill's pass over the same
    /// 12.9MB. The copy it replaces overwrote every byte of the canvas anyway, which
    /// is what makes the two identical. The buffer becomes this composer's, so a
    /// caller must not go on writing into one it has handed over.
    /// </para>
    /// </remarks>
    public void Draw(CaptureMonitor monitor, int sourceWidth, int sourceHeight, byte[] bgraPixels)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(bgraPixels);
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

        if (_pixels is null && left == 0 && top == 0 && sourceWidth == Width && sourceHeight == Height)
        {
            _pixels = bgraPixels;
            return;
        }

        var canvas = Canvas();
        for (var row = 0; row < rows; row++)
        {
            var from = row * sourceWidth * 4;
            var to = (((top + row) * Width) + left) * 4;
            bgraPixels.AsSpan(from, columns * 4).CopyTo(canvas.AsSpan(to));
        }
    }

    /// <summary>The composed frame, top-down BGRA.</summary>
    public byte[] ToImage() => Canvas();

    /// <summary>
    /// The buffer to compose into, made opaque black on the way out. Display layouts
    /// are not rectangular — an L of three monitors leaves a corner no display covers
    /// — and that corner has to be a defined colour rather than whatever the
    /// allocation happened to contain.
    /// </summary>
    private byte[] Canvas()
    {
        if (_pixels is { } existing)
        {
            return existing;
        }

        var canvas = new byte[_length];
        for (var index = 3; index < canvas.Length; index += 4)
        {
            canvas[index] = byte.MaxValue;
        }

        return _pixels = canvas;
    }
}
