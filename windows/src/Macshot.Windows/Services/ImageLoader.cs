using System.Runtime.InteropServices.WindowsRuntime;

using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Macshot.Windows.Services;

/// <summary>
/// Reads an image file back into a frame, which is what reopening a past capture needs.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="ImageDelivery"/>. Every format macshot writes is one
/// Windows Imaging Component decodes, and the decoder is asked for BGRA8 whatever it
/// found, so a JPEG and a PNG come back as the same kind of buffer the capture path
/// produces.
/// </remarks>
public static class ImageLoader
{
    public static async Task<CapturedFrame> LoadAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Opened read-only and shared: a past capture may well be open in whatever the
        // machine shows PNGs with, and refusing to reopen it for that reason would be a
        // failure the user cannot act on.
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await LoadAsync(file.AsRandomAccessStream());
    }

    /// <summary>
    /// The same, for bytes that never were a file — an image handed over by the
    /// clipboard, which arrives as a stream of whatever format the copying program
    /// happened to put there.
    /// </summary>
    public static async Task<CapturedFrame> LoadAsync(IRandomAccessStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var decoder = await BitmapDecoder.CreateAsync(stream);

        // Premultiplied because that is what a WriteableBitmap holds, and the preview
        // writes these pixels straight into one. Asking for straight alpha would show
        // every semi-transparent pixel too bright.
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);

        var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
        bitmap.CopyToBuffer(pixels.AsBuffer());

        // No virtual-desktop origin: a file has no place on the screen, and the only
        // thing that reads it back is a pin window deciding where to open.
        return new CapturedFrame(0, 0, bitmap.PixelWidth, bitmap.PixelHeight, pixels);
    }
}
