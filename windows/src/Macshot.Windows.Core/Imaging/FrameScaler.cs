namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// Shrinks a BGRA frame by averaging the pixels each output pixel covers.
/// </summary>
/// <remarks>
/// <para>
/// Averaging rather than sampling, because what is being shrunk is a desktop: single
/// pixel lines, one pixel borders, and text a stroke or two thick. Taking the nearest
/// source pixel drops half of that and leaves the rest crawling from frame to frame,
/// which on a GIF's palette turns into visible noise.
/// </para>
/// <para>
/// Done here in Core rather than by asking the platform, because it is a loop over
/// bytes with an answer that can be checked, and every alternative on Windows means
/// routing frames through an imaging pipeline to accomplish the same thing.
/// </para>
/// </remarks>
public static class FrameScaler
{
    private const int BytesPerPixel = 4;

    /// <summary>
    /// Returns <paramref name="pixels"/> scaled to the target size, or the same array
    /// when it is already that size.
    /// </summary>
    public static byte[] Downscale(byte[] pixels, int width, int height, int targetWidth, int targetHeight)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeight);

        var expected = checked(width * height * BytesPerPixel);
        if (pixels.Length < expected)
        {
            throw new ArgumentException(
                $"A {width}x{height} BGRA frame needs {expected} bytes, not {pixels.Length}.",
                nameof(pixels));
        }

        if (targetWidth == width && targetHeight == height)
        {
            // The caller owns the frame either way, and copying a display's worth of
            // pixels to hand back the same image is worth not doing.
            return pixels;
        }

        var scaled = new byte[checked(targetWidth * targetHeight * BytesPerPixel)];

        for (var targetY = 0; targetY < targetHeight; targetY++)
        {
            // Source rows come from the target pixel's own extent rather than from a
            // step, so the last one covers the remainder instead of reading past the
            // end of the frame.
            var fromY = targetY * height / targetHeight;
            var toY = Math.Max(fromY + 1, (targetY + 1) * height / targetHeight);

            for (var targetX = 0; targetX < targetWidth; targetX++)
            {
                var fromX = targetX * width / targetWidth;
                var toX = Math.Max(fromX + 1, (targetX + 1) * width / targetWidth);

                var blue = 0;
                var green = 0;
                var red = 0;
                var alpha = 0;
                var counted = 0;

                for (var y = fromY; y < toY; y++)
                {
                    var row = y * width * BytesPerPixel;
                    for (var x = fromX; x < toX; x++)
                    {
                        var source = row + (x * BytesPerPixel);
                        blue += pixels[source];
                        green += pixels[source + 1];
                        red += pixels[source + 2];
                        alpha += pixels[source + 3];
                        counted++;
                    }
                }

                var target = ((targetY * targetWidth) + targetX) * BytesPerPixel;
                scaled[target] = (byte)(blue / counted);
                scaled[target + 1] = (byte)(green / counted);
                scaled[target + 2] = (byte)(red / counted);
                scaled[target + 3] = (byte)(alpha / counted);
            }
        }

        return scaled;
    }
}
