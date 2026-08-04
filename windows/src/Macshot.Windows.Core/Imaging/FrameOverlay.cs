using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// Draws a rasterized overlay — a caption — onto a video frame.
/// </summary>
/// <remarks>
/// <para>
/// The same division of labour the screenshot text tool uses: the UI half turns glyphs
/// into pixels once, and Core composites those pixels. See architecture decision D7.
/// What is different here is that a video caption is composited thousands of times from
/// one raster, and under a zoom the rectangle it lands in moves and changes size between
/// frames — so unlike <c>AnnotationRasterizer</c>'s one-to-one sprite blit, this one
/// resamples.
/// </para>
/// <para>
/// macshot caches the raster on the compositor's instruction and scales it per frame for
/// exactly the same reason: font shaping at video frame rate would cost more than the
/// encode.
/// </para>
/// </remarks>
public static class FrameOverlay
{
    private const int BytesPerPixel = 4;

    /// <summary>
    /// Composites <paramref name="sprite"/> into <paramref name="region"/> of a BGRA,
    /// top-down frame at <paramref name="opacity"/>.
    /// </summary>
    /// <param name="sprite">
    /// Premultiplied BGRA, as <c>RenderTargetBitmap</c> produces it. Premultiplied is
    /// what makes resampling correct: interpolating a straight alpha channel against its
    /// colours pulls the colour of fully transparent pixels into the edges of the glyphs,
    /// which shows as a dark halo round white text.
    /// </param>
    public static void Composite(
        byte[] pixels,
        int width,
        int height,
        byte[] sprite,
        int spriteWidth,
        int spriteHeight,
        CaptureRegion region,
        double opacity)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(sprite);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (opacity <= 0.001 || spriteWidth <= 0 || spriteHeight <= 0)
        {
            return;
        }

        var left = (int)Math.Round(region.X);
        var top = (int)Math.Round(region.Y);
        var across = (int)Math.Round(region.Width);
        var down = (int)Math.Round(region.Height);

        if (across < 1 || down < 1)
        {
            return;
        }

        // Resampled through the same bilinear sampler a zoom uses rather than a second
        // one written here. It hands the sprite straight back when nothing has to change,
        // which is the common case: the caption was rasterized at the size of its own
        // rectangle and only a zoom ever moves it off that size.
        var scaled = FrameZoom.Sample(
            sprite,
            spriteWidth,
            spriteHeight,
            new CaptureRegion(0, 0, spriteWidth, spriteHeight),
            across,
            down);

        for (var row = 0; row < down; row++)
        {
            var y = top + row;
            if (y < 0 || y >= height)
            {
                continue;
            }

            for (var column = 0; column < across; column++)
            {
                var x = left + column;
                if (x < 0 || x >= width)
                {
                    continue;
                }

                var from = ((row * across) + column) * BytesPerPixel;
                var alpha = scaled[from + 3] * opacity;

                // Most of a caption's raster is the clear space round the pill, so this
                // is the common branch.
                if (alpha < 0.5)
                {
                    continue;
                }

                var to = ((y * width) + x) * BytesPerPixel;
                var keep = 1 - (alpha / byte.MaxValue);

                for (var channel = 0; channel < 3; channel++)
                {
                    pixels[to + channel] = (byte)Math.Clamp(
                        Math.Round((scaled[from + channel] * opacity) + (pixels[to + channel] * keep)),
                        0,
                        255);
                }

                // The frame stays opaque whatever the caption's alpha was. Everything
                // downstream treats a video frame as having no transparency, and an
                // encoder handed one comes back with a hole in it.
                pixels[to + 3] = byte.MaxValue;
            }
        }
    }
}
