using System.Runtime.InteropServices.WindowsRuntime;

using Macshot.Windows.Core.Imaging;

using Windows.Graphics.Imaging;

namespace Macshot.Windows.Services;

public sealed class CapturedFrame
{
    public CapturedFrame(
        int virtualX,
        int virtualY,
        int width,
        int height,
        byte[] bgraPixels,
        double scale,
        bool hasAlpha = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentNullException.ThrowIfNull(bgraPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        if (bgraPixels.Length != checked(width * height * 4))
        {
            throw new ArgumentException("The pixel buffer does not match the frame dimensions.", nameof(bgraPixels));
        }

        VirtualX = virtualX;
        VirtualY = virtualY;
        Width = width;
        Height = height;
        BgraPixels = bgraPixels;
        Scale = scale;
        HasAlpha = hasAlpha;
    }

    /// <summary>
    /// No pixels at all, for a surface that has been closed and is only still in memory
    /// because the framework has not let go of it. See
    /// <c>CaptureOverlayView.ReleasePixels</c>.
    /// </summary>
    public static CapturedFrame Empty { get; } = new(0, 0, 0, 0, [], 1);

    public int VirtualX { get; }

    public int VirtualY { get; }

    public int Width { get; }

    public int Height { get; }

    public byte[] BgraPixels { get; }

    /// <summary>
    /// How many of these pixels make one point of the screen they were taken from: the
    /// display's scale for a capture, and 1 for a picture that never was one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What an <c>NSImage</c>'s size says on the Mac, where a capture taken at 2x is twice
    /// as many pixels as points and is shown, sized and marked up in the points.
    /// macshot's editor lays a capture out at that size, so it opens at the size it was on
    /// screen and a 3 on the row is as thick there as it was in the overlay. A bare buffer
    /// cannot say any of that, which is why this is here.
    /// </para>
    /// <para>
    /// Required rather than defaulted, so a frame made from another one has to say what it
    /// carries over: a default would let each of the two dozen places that build one drop
    /// it without anything noticing.
    /// </para>
    /// </remarks>
    public double Scale { get; }

    /// <summary>
    /// Whether the alpha byte of each pixel means anything.
    /// </summary>
    /// <remarks>
    /// False for every captured frame: BitBlt produces BGRX, where the fourth byte is
    /// undefined, and reading it would turn ordinary screenshots see-through at random.
    /// True only for the frames macshot itself has cut out — see
    /// <see cref="BackgroundRemover"/> — where the transparency is the whole point and
    /// dropping it would hand back the picture the button was pressed to change.
    /// </remarks>
    public bool HasAlpha { get; }

    /// <summary>
    /// The alpha the imaging stack should be told these pixels carry.
    /// </summary>
    /// <remarks>
    /// Straight rather than premultiplied: a cut-out keeps the subject's own colours and
    /// changes only the alpha beside them, so the colour bytes were never scaled by it.
    /// </remarks>
    public BitmapAlphaMode AlphaMode => HasAlpha ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore;

    /// <remarks>
    /// <c>AsBuffer</c> wraps the array; <c>CryptographicBuffer.CreateFromByteArray</c>,
    /// which used to be here, copies it. Between that copy and
    /// <c>CreateCopyFromBuffer</c>'s own, every surface that showed a capture paid two
    /// full screens instead of one — 12.9MB apiece at 2038x1588, spent at the moment a
    /// capture is already at its high-water mark.
    /// </remarks>
    /// <summary>The same pixels, said to be worth <paramref name="scale"/> to a point.</summary>
    /// <remarks>Shares the buffer: nothing about the pixels changes.</remarks>
    public CapturedFrame WithScale(double scale) =>
        new(VirtualX, VirtualY, Width, Height, BgraPixels, scale, HasAlpha);

    public SoftwareBitmap ToSoftwareBitmap() => SoftwareBitmap.CreateCopyFromBuffer(
        BgraPixels.AsBuffer(),
        BitmapPixelFormat.Bgra8,
        Width,
        Height,
        AlphaMode);

    /// <summary>
    /// The same pixels in the one form <see cref="SoftwareBitmapSource"/> accepts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every surface that shows a capture — the overlay's preview, the thumbnail panel, the
    /// pin window, the recognition window — hands its bitmap to a
    /// <c>SoftwareBitmapSource</c>, and that class takes premultiplied or no alpha and
    /// refuses straight. A cut-out frame carries straight alpha, so all four threw
    /// <c>ArgumentException</c> the moment one reached them.
    /// </para>
    /// <para>
    /// It went unnoticed because nothing ever produced such a frame on a machine anyone
    /// ran: background removal needed a Copilot+ PC and a packaged build, so
    /// <see cref="HasAlpha"/> was false everywhere. Making it reachable made this reachable.
    /// </para>
    /// <para>
    /// Converted here rather than at each of the four, and on the way out rather than in
    /// storage: what is saved and what is encoded must stay straight, because premultiplying
    /// is not reversible — a colour multiplied by a small alpha cannot be recovered.
    /// </para>
    /// </remarks>
    public SoftwareBitmap ToDisplayBitmap()
    {
        if (!HasAlpha)
        {
            return ToSoftwareBitmap();
        }

        return SoftwareBitmap.CreateCopyFromBuffer(
            PremultipliedAlpha.From(BgraPixels).AsBuffer(),
            BitmapPixelFormat.Bgra8,
            Width,
            Height,
            BitmapAlphaMode.Premultiplied);
    }
}
