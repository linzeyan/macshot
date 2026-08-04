using Macshot.Windows.Core.Imaging;

using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;

namespace Macshot.Windows.Services;

public sealed class CapturedFrame
{
    public CapturedFrame(
        int virtualX,
        int virtualY,
        int width,
        int height,
        byte[] bgraPixels,
        bool hasAlpha = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentNullException.ThrowIfNull(bgraPixels);

        if (bgraPixels.Length != checked(width * height * 4))
        {
            throw new ArgumentException("The pixel buffer does not match the frame dimensions.", nameof(bgraPixels));
        }

        VirtualX = virtualX;
        VirtualY = virtualY;
        Width = width;
        Height = height;
        BgraPixels = bgraPixels;
        HasAlpha = hasAlpha;
    }

    public int VirtualX { get; }

    public int VirtualY { get; }

    public int Width { get; }

    public int Height { get; }

    public byte[] BgraPixels { get; }

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

    public SoftwareBitmap ToSoftwareBitmap()
    {
        var buffer = CryptographicBuffer.CreateFromByteArray(BgraPixels);
        return SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            Width,
            Height,
            AlphaMode);
    }

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

        var buffer = CryptographicBuffer.CreateFromByteArray(PremultipliedAlpha.From(BgraPixels));
        return SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            Width,
            Height,
            BitmapAlphaMode.Premultiplied);
    }
}
