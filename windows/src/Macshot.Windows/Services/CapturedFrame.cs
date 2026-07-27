using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;

namespace Macshot.Windows.Services;

public sealed class CapturedFrame
{
    public CapturedFrame(int virtualX, int virtualY, int width, int height, byte[] bgraPixels)
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
    }

    public int VirtualX { get; }

    public int VirtualY { get; }

    public int Width { get; }

    public int Height { get; }

    public byte[] BgraPixels { get; }

    public SoftwareBitmap ToSoftwareBitmap()
    {
        var buffer = CryptographicBuffer.CreateFromByteArray(BgraPixels);
        return SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            Width,
            Height,
            BitmapAlphaMode.Ignore);
    }
}
