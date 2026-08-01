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

    /// <summary>
    /// The mean colour of a region, for a mark that has to sit on the page rather than
    /// on top of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What it is for is the translation overlay: a box painted the average of what it
    /// covers disappears into a white page, a dark code listing or a coloured banner
    /// without anyone choosing a colour per screenshot. It is a mean rather than the
    /// most common colour — a line of text is mostly background, so the mean lands on
    /// the background and is tinted very slightly by the ink, which is what makes it
    /// read as the same surface.
    /// </para>
    /// <para>
    /// Always opaque, for the reason <see cref="Sample"/> gives: a capture's alpha
    /// channel says nothing about what is on screen.
    /// </para>
    /// </remarks>
    public static AnnotationColor AverageColor(
        byte[] bgraPixels,
        int width,
        int height,
        CaptureRegion region)
    {
        ValidateFrame(bgraPixels, width, height);

        var left = Math.Clamp((int)Math.Floor(region.X), 0, width - 1);
        var top = Math.Clamp((int)Math.Floor(region.Y), 0, height - 1);
        var right = Math.Clamp((int)Math.Ceiling(region.Right), left + 1, width);
        var bottom = Math.Clamp((int)Math.Ceiling(region.Bottom), top + 1, height);

        long red = 0;
        long green = 0;
        long blue = 0;

        for (var row = top; row < bottom; row++)
        {
            var line = row * width * 4;
            for (var column = left; column < right; column++)
            {
                var offset = line + (column * 4);
                blue += bgraPixels[offset];
                green += bgraPixels[offset + 1];
                red += bgraPixels[offset + 2];
            }
        }

        var counted = (long)(right - left) * (bottom - top);
        return new AnnotationColor(
            (byte)(red / counted),
            (byte)(green / counted),
            (byte)(blue / counted));
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

    /// <summary>
    /// Fills <paramref name="region"/> with the colours around it, so what was there
    /// reads as empty background rather than as something covered up.
    /// </summary>
    /// <remarks>
    /// macshot's, and it is deliberately not an inpainting: each edge is sampled just
    /// outside the region, every pixel takes the horizontal blend of its row's two edge
    /// colours and the vertical blend of its column's two, and the two are averaged. On
    /// a flat or gently graded background — a toolbar, a document, a wall of one colour —
    /// that is indistinguishable from the real thing, and on a busy one it looks like a
    /// smear, which is honest about having removed something.
    /// </remarks>
    public static void Erase(byte[] bgraPixels, int width, int height, CaptureRegion region)
    {
        ValidateFrame(bgraPixels, width, height);

        if (!TryGetBounds(region, width, height, out var left, out var top, out var right, out var bottom))
        {
            return;
        }

        var regionWidth = right - left;
        var regionHeight = bottom - top;

        // Averaged over a few pixels rather than taken from one, so a single stray pixel
        // on the border does not colour a whole row of the fill.
        const int Reach = 3;

        var leftEdge = new AnnotationColor[regionHeight];
        var rightEdge = new AnnotationColor[regionHeight];
        for (var row = 0; row < regionHeight; row++)
        {
            leftEdge[row] = AverageOutside(bgraPixels, width, height, left, top + row, -1, 0, Reach);
            rightEdge[row] = AverageOutside(bgraPixels, width, height, right - 1, top + row, 1, 0, Reach);
        }

        var topEdge = new AnnotationColor[regionWidth];
        var bottomEdge = new AnnotationColor[regionWidth];
        for (var column = 0; column < regionWidth; column++)
        {
            topEdge[column] = AverageOutside(bgraPixels, width, height, left + column, top, 0, -1, Reach);
            bottomEdge[column] = AverageOutside(bgraPixels, width, height, left + column, bottom - 1, 0, 1, Reach);
        }

        for (var row = 0; row < regionHeight; row++)
        {
            var down = regionHeight > 1 ? (double)row / (regionHeight - 1) : 0;
            for (var column = 0; column < regionWidth; column++)
            {
                var across = regionWidth > 1 ? (double)column / (regionWidth - 1) : 0;
                var horizontal = Between(leftEdge[row], rightEdge[row], across);
                var vertical = Between(topEdge[column], bottomEdge[column], down);

                var offset = (((top + row) * width) + left + column) * 4;
                bgraPixels[offset] = Mean(horizontal.Blue, vertical.Blue);
                bgraPixels[offset + 1] = Mean(horizontal.Green, vertical.Green);
                bgraPixels[offset + 2] = Mean(horizontal.Red, vertical.Red);
                bgraPixels[offset + 3] = byte.MaxValue;
            }
        }
    }

    /// <summary>
    /// The mean of up to <paramref name="reach"/> pixels stepping away from
    /// (<paramref name="x"/>, <paramref name="y"/>) in the given direction. Clamped at
    /// the frame edge, which is what makes a region drawn against the edge of the screen
    /// take the colour of the last row inside it rather than nothing.
    /// </summary>
    private static AnnotationColor AverageOutside(
        byte[] pixels,
        int width,
        int height,
        int x,
        int y,
        int stepX,
        int stepY,
        int reach)
    {
        int blue = 0;
        int green = 0;
        int red = 0;
        for (var step = 1; step <= reach; step++)
        {
            var sample = Sample(pixels, width, height, x + (stepX * step), y + (stepY * step));
            blue += sample.Blue;
            green += sample.Green;
            red += sample.Red;
        }

        return new AnnotationColor((byte)(red / reach), (byte)(green / reach), (byte)(blue / reach));
    }

    private static AnnotationColor Between(AnnotationColor from, AnnotationColor to, double at) =>
        new(
            (byte)Math.Round(from.Red + ((to.Red - from.Red) * at)),
            (byte)Math.Round(from.Green + ((to.Green - from.Green) * at)),
            (byte)Math.Round(from.Blue + ((to.Blue - from.Blue) * at)));

    private static byte Mean(byte one, byte other) => (byte)((one + other + 1) / 2);

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
    /// A round, magnified view of what sits under one point, as its own small BGRA
    /// buffer <paramref name="diameter"/> pixels square.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the colour sampler, which has to be aimed at one pixel: at screen scale that
    /// pixel is a fifth of a millimetre, and picking a colour off a gradient or a
    /// photograph by eye is guesswork without magnification.
    /// </para>
    /// <para>
    /// Its own buffer rather than a region of the frame, unlike <see cref="Magnify"/>:
    /// this one follows the pointer and must not write on the image. Pixels outside the
    /// circle come back fully transparent, so the caller can lay it over whatever is
    /// there and ring it.
    /// </para>
    /// <para>
    /// Sampling past the edge of the frame repeats the edge pixel rather than leaving a
    /// hole, because a sampler aimed at the very corner of the screen still has to show
    /// what it is pointing at.
    /// </para>
    /// </remarks>
    public static byte[] MagnifiedPatch(
        byte[] bgraPixels,
        int width,
        int height,
        int centerX,
        int centerY,
        int diameter,
        double zoom)
    {
        ValidateFrame(bgraPixels, width, height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(diameter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zoom);

        var patch = new byte[diameter * diameter * 4];
        var radius = diameter / 2d;

        for (var y = 0; y < diameter; y++)
        {
            for (var x = 0; x < diameter; x++)
            {
                var offsetX = x + 0.5 - radius;
                var offsetY = y + 0.5 - radius;
                if ((offsetX * offsetX) + (offsetY * offsetY) > radius * radius)
                {
                    continue;
                }

                var sampleX = Math.Clamp((int)Math.Floor(centerX + 0.5 + (offsetX / zoom)), 0, width - 1);
                var sampleY = Math.Clamp((int)Math.Floor(centerY + 0.5 + (offsetY / zoom)), 0, height - 1);

                var from = ((sampleY * width) + sampleX) * 4;
                var to = ((y * diameter) + x) * 4;
                patch[to] = bgraPixels[from];
                patch[to + 1] = bgraPixels[from + 1];
                patch[to + 2] = bgraPixels[from + 2];

                // Opaque whatever the screenshot's own alpha byte holds: BitBlt leaves
                // it undefined, and a transparent patch would show nothing at all.
                patch[to + 3] = byte.MaxValue;
            }
        }

        return patch;
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
