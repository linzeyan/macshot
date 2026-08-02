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
}
