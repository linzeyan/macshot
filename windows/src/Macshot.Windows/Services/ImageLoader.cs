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

    /// <summary>
    /// The same, no larger than <paramref name="within"/> on its longer side.
    /// </summary>
    /// <remarks>
    /// The decoder scales on the way out, so what never existed is the full-sized copy.
    /// That is the whole difference for anything drawn small: the beautify swatch is forty
    /// points across and was being painted from a 2038x1588 decode of the user's picture —
    /// twelve megabytes for a thumbnail, held for as long as macshot ran.
    /// </remarks>
    /// <param name="within">The longest side the caller will draw, in pixels.</param>
    public static async Task<CapturedFrame> LoadAsync(IRandomAccessStream stream, int within)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(within);

        var decoder = await BitmapDecoder.CreateAsync(stream);

        // Only ever down. A picture already smaller than the caller asked for is left
        // alone rather than enlarged into a buffer bigger than the file it came from.
        var scale = Math.Min(
            1.0,
            within / (double)Math.Max(decoder.PixelWidth, decoder.PixelHeight));

        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };

        // The same two as the overload above, spelled out because this overload of
        // GetSoftwareBitmapAsync has no defaults to fall back on.
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);

        var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
        bitmap.CopyToBuffer(pixels.AsBuffer());
        return new CapturedFrame(0, 0, bitmap.PixelWidth, bitmap.PixelHeight, pixels);
    }
}
