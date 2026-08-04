using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// Stretches part of a BGRA frame over the whole of a new one.
/// </summary>
/// <remarks>
/// <para>
/// The pixel half of a zoom: <see cref="VideoZoomSegment.SourceRectAt"/> says which
/// rectangle to take, this takes it. Bilinear, because a zoom magnifies — the box average
/// <see cref="FrameScaler"/> uses is right for shrinking and degenerates to picking the
/// nearest pixel when going the other way, which on a magnified desktop turns every
/// one-pixel line into a staircase that crawls as the ramp moves.
/// </para>
/// <para>
/// Here rather than through the platform's imaging pipeline, for the reason
/// <see cref="FrameScaler"/> gives and one more. Windows would do this through a
/// <c>BitmapTransform</c>, which crops <em>after</em> it scales — so a five-times zoom
/// means asking it for a frame five times the size of the source and then cutting a
/// window out of that. Whether it materialises the intermediate is an implementation
/// detail of WIC that no test on this side of the port could settle, and a 4K frame at
/// five times is two hundred megabytes if the answer is the wrong one. A loop over bytes
/// has an answer that can be checked.
/// </para>
/// </remarks>
public static class FrameZoom
{
    private const int BytesPerPixel = 4;

    /// <summary>
    /// Returns <paramref name="region"/> of a <paramref name="width"/> by
    /// <paramref name="height"/> BGRA frame, resampled to
    /// <paramref name="targetWidth"/> by <paramref name="targetHeight"/>.
    /// </summary>
    /// <remarks>
    /// The frame itself is handed straight back when the region is the whole of it and
    /// the size already matches — which is every frame outside a zoom segment, and most
    /// frames in an export. Copying a display's worth of pixels to return the same image
    /// is worth not doing thirty times a second.
    /// </remarks>
    public static byte[] Sample(
        byte[] pixels,
        int width,
        int height,
        CaptureRegion region,
        int targetWidth,
        int targetHeight)
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

        if (region.IsEmpty)
        {
            throw new ArgumentException("A zoom cannot sample an empty rectangle.", nameof(region));
        }

        var whole = region.X <= 0
            && region.Y <= 0
            && region.Width >= width
            && region.Height >= height;

        if (whole && targetWidth == width && targetHeight == height)
        {
            return pixels;
        }

        var sampled = new byte[checked(targetWidth * targetHeight * BytesPerPixel)];

        // Pixel centres, not pixel corners. Mapping corner to corner shifts the picture
        // half an output pixel up and left, which at 1:1 is the difference between the
        // frame coming back untouched and coming back blurred.
        var stepX = region.Width / targetWidth;
        var stepY = region.Height / targetHeight;

        for (var targetY = 0; targetY < targetHeight; targetY++)
        {
            var sourceY = region.Y + ((targetY + 0.5) * stepY) - 0.5;
            var topRow = (int)Math.Floor(sourceY);
            var downWeight = sourceY - topRow;

            var top = Math.Clamp(topRow, 0, height - 1);
            var bottom = Math.Clamp(topRow + 1, 0, height - 1);
            var topOffset = top * width * BytesPerPixel;
            var bottomOffset = bottom * width * BytesPerPixel;

            for (var targetX = 0; targetX < targetWidth; targetX++)
            {
                var sourceX = region.X + ((targetX + 0.5) * stepX) - 0.5;
                var leftColumn = (int)Math.Floor(sourceX);
                var rightWeight = sourceX - leftColumn;

                var left = Math.Clamp(leftColumn, 0, width - 1) * BytesPerPixel;
                var right = Math.Clamp(leftColumn + 1, 0, width - 1) * BytesPerPixel;

                var target = ((targetY * targetWidth) + targetX) * BytesPerPixel;

                for (var channel = 0; channel < BytesPerPixel; channel++)
                {
                    var topValue = Lerp(
                        pixels[topOffset + left + channel],
                        pixels[topOffset + right + channel],
                        rightWeight);

                    var bottomValue = Lerp(
                        pixels[bottomOffset + left + channel],
                        pixels[bottomOffset + right + channel],
                        rightWeight);

                    // Rounded rather than truncated: truncation loses up to a whole level
                    // per channel, and a gradient resampled that way bands visibly.
                    sampled[target + channel] = (byte)Math.Clamp(
                        Math.Round(Lerp(topValue, bottomValue, downWeight)),
                        0,
                        255);
                }
            }
        }

        return sampled;
    }

    private static double Lerp(double from, double to, double weight) => from + ((to - from) * weight);
}
