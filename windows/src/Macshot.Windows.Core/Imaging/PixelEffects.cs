using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// In-place pixel operations on a BGRA, top-down frame buffer. These are the
/// annotation tools that rewrite what is underneath them instead of drawing on
/// top, so they must run before anything is composited over the same area.
/// </summary>
public static class PixelEffects
{
    /// <summary>Replaces each block inside <paramref name="region"/> with its average color.</summary>
    /// <summary>
    /// The colour of one pixel, with the point clamped into the frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Clamped rather than refused. The sampler follows the pointer, and a pointer a
    /// pixel past the edge of the frame is an ordinary thing to happen rather than a
    /// reason to abandon the pick.
    /// </para>
    /// <para>
    /// Always opaque. A screen capture's alpha channel says nothing about what is on
    /// screen — <c>BitBlt</c> leaves it at zero — so carrying it through would sample
    /// every pixel of the desktop as invisible.
    /// </para>
    /// </remarks>
    public static AnnotationColor Sample(byte[] bgraPixels, int width, int height, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(bgraPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var column = Math.Clamp(x, 0, width - 1);
        var row = Math.Clamp(y, 0, height - 1);
        var offset = (((row * width) + column) * 4);

        return new AnnotationColor(bgraPixels[offset + 2], bgraPixels[offset + 1], bgraPixels[offset]);
    }

    public static void Pixelate(byte[] bgraPixels, int width, int height, CaptureRegion region, double blockSize)
    {
        ValidateFrame(bgraPixels, width, height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);

        if (!TryGetBounds(region, width, height, out var left, out var top, out var right, out var bottom))
        {
            return;
        }

        // A one pixel block would be a no-op and would silently fail to redact.
        var block = Math.Max(2, (int)Math.Round(blockSize));

        for (var blockTop = top; blockTop < bottom; blockTop += block)
        {
            var blockBottom = Math.Min(blockTop + block, bottom);
            for (var blockLeft = left; blockLeft < right; blockLeft += block)
            {
                var blockRight = Math.Min(blockLeft + block, right);
                AverageBlock(bgraPixels, width, blockLeft, blockTop, blockRight, blockBottom);
            }
        }
    }

    /// <summary>
    /// Blurs <paramref name="region"/> with three box passes, which approximates a
    /// Gaussian closely enough to be indistinguishable at redaction strengths and
    /// runs in time independent of the radius.
    /// </summary>
    public static void Blur(byte[] bgraPixels, int width, int height, CaptureRegion region, double radius)
    {
        ValidateFrame(bgraPixels, width, height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);

        if (!TryGetBounds(region, width, height, out var left, out var top, out var right, out var bottom))
        {
            return;
        }

        var regionWidth = right - left;
        var regionHeight = bottom - top;
        var boxRadius = Math.Max(1, (int)Math.Round(radius));

        // Work on a copy of the region so the blur never samples pixels outside it:
        // a redaction must not pull the sharp content next to it back in, and edge
        // clamping keeps the result deterministic.
        var source = ExtractRegion(bgraPixels, width, left, top, regionWidth, regionHeight);
        var scratch = new byte[source.Length];

        for (var pass = 0; pass < 3; pass++)
        {
            BoxBlur(source, scratch, regionWidth, regionHeight, boxRadius, horizontal: true);
            BoxBlur(scratch, source, regionWidth, regionHeight, boxRadius, horizontal: false);
        }

        WriteRegion(bgraPixels, width, left, top, regionWidth, regionHeight, source);
    }

    private static void AverageBlock(byte[] pixels, int width, int left, int top, int right, int bottom)
    {
        var count = (right - left) * (bottom - top);
        if (count <= 0)
        {
            return;
        }

        long sumBlue = 0;
        long sumGreen = 0;
        long sumRed = 0;

        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var offset = (y * width + x) * 4;
                sumBlue += pixels[offset];
                sumGreen += pixels[offset + 1];
                sumRed += pixels[offset + 2];
            }
        }

        var blue = (byte)(sumBlue / count);
        var green = (byte)(sumGreen / count);
        var red = (byte)(sumRed / count);

        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var offset = (y * width + x) * 4;
                pixels[offset] = blue;
                pixels[offset + 1] = green;
                pixels[offset + 2] = red;
                pixels[offset + 3] = byte.MaxValue;
            }
        }
    }

    private static void BoxBlur(byte[] source, byte[] destination, int width, int height, int radius, bool horizontal)
    {
        var window = radius * 2 + 1;
        var outerCount = horizontal ? height : width;
        var innerCount = horizontal ? width : height;

        for (var outer = 0; outer < outerCount; outer++)
        {
            var sumBlue = 0;
            var sumGreen = 0;
            var sumRed = 0;

            for (var offset = -radius; offset <= radius; offset++)
            {
                var index = OffsetOf(outer, Math.Clamp(offset, 0, innerCount - 1), width, horizontal);
                sumBlue += source[index];
                sumGreen += source[index + 1];
                sumRed += source[index + 2];
            }

            for (var inner = 0; inner < innerCount; inner++)
            {
                var target = OffsetOf(outer, inner, width, horizontal);
                destination[target] = (byte)(sumBlue / window);
                destination[target + 1] = (byte)(sumGreen / window);
                destination[target + 2] = (byte)(sumRed / window);
                destination[target + 3] = byte.MaxValue;

                var leaving = OffsetOf(outer, Math.Clamp(inner - radius, 0, innerCount - 1), width, horizontal);
                var entering = OffsetOf(outer, Math.Clamp(inner + radius + 1, 0, innerCount - 1), width, horizontal);
                sumBlue += source[entering] - source[leaving];
                sumGreen += source[entering + 1] - source[leaving + 1];
                sumRed += source[entering + 2] - source[leaving + 2];
            }
        }
    }

    private static int OffsetOf(int outer, int inner, int width, bool horizontal)
    {
        return horizontal
            ? (outer * width + inner) * 4
            : (inner * width + outer) * 4;
    }

    private static byte[] ExtractRegion(byte[] pixels, int frameWidth, int left, int top, int width, int height)
    {
        var region = new byte[checked(width * height * 4)];
        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(pixels, ((top + row) * frameWidth + left) * 4, region, row * width * 4, width * 4);
        }

        return region;
    }

    private static void WriteRegion(
        byte[] pixels,
        int frameWidth,
        int left,
        int top,
        int width,
        int height,
        byte[] region)
    {
        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(region, row * width * 4, pixels, ((top + row) * frameWidth + left) * 4, width * 4);
        }
    }

    /// <summary>
    /// Redraws a circle inside <paramref name="region"/> as a magnified view of what
    /// sits under its centre.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The frame is copied before anything is written, because the area being
    /// magnified and the circle it is drawn into are the same pixels: sampling
    /// straight from the buffer would feed the magnifier its own output and smear it
    /// outwards.
    /// </para>
    /// <para>
    /// Nearest neighbour rather than a smooth resample. A loupe over a screenshot is
    /// pointed at a hairline or a character, and interpolation is exactly what would
    /// blur away the thing being looked at.
    /// </para>
    /// </remarks>
    public static void Magnify(
        byte[] bgraPixels,
        int width,
        int height,
        CaptureRegion region,
        double zoom)
    {
        ValidateFrame(bgraPixels, width, height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zoom);

        if (!TryGetBounds(region, width, height, out var left, out var top, out var right, out var bottom))
        {
            return;
        }

        var centerX = region.X + (region.Width / 2);
        var centerY = region.Y + (region.Height / 2);
        var radius = Math.Min(region.Width, region.Height) / 2;
        if (radius <= 0)
        {
            return;
        }

        var source = ExtractRegion(bgraPixels, width, 0, 0, width, height);

        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var offsetX = x + 0.5 - centerX;
                var offsetY = y + 0.5 - centerY;
                if ((offsetX * offsetX) + (offsetY * offsetY) > radius * radius)
                {
                    continue;
                }

                var sampleX = (int)Math.Floor(centerX + (offsetX / zoom));
                var sampleY = (int)Math.Floor(centerY + (offsetY / zoom));
                if (sampleX < 0 || sampleX >= width || sampleY < 0 || sampleY >= height)
                {
                    continue;
                }

                var from = ((sampleY * width) + sampleX) * 4;
                var to = ((y * width) + x) * 4;
                source.AsSpan(from, 4).CopyTo(bgraPixels.AsSpan(to, 4));
            }
        }
    }

    private static bool TryGetBounds(
        CaptureRegion region,
        int frameWidth,
        int frameHeight,
        out int left,
        out int top,
        out int right,
        out int bottom)
    {
        left = Math.Clamp((int)Math.Floor(region.X), 0, frameWidth);
        top = Math.Clamp((int)Math.Floor(region.Y), 0, frameHeight);
        right = Math.Clamp((int)Math.Ceiling(region.X + region.Width), left, frameWidth);
        bottom = Math.Clamp((int)Math.Ceiling(region.Y + region.Height), top, frameHeight);
        return right > left && bottom > top;
    }

    private static void ValidateFrame(byte[] bgraPixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(bgraPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (bgraPixels.Length != checked(width * height * 4))
        {
            throw new ArgumentException("The pixel buffer does not match the frame dimensions.", nameof(bgraPixels));
        }
    }
}
