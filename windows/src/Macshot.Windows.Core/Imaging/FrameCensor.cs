using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// Hides a rectangle of a video frame, as far in as the segment's ramp has got.
/// </summary>
/// <remarks>
/// <para>
/// The pixel half of a censor. <see cref="PixelEffects"/> already owns the two ways of
/// obscuring something that this port has — a screenshot's pixelate and blur tools are
/// the same operation on a still — so this adds no second implementation of either. What
/// it adds is the part a still has no use for: a strength between nothing and all of it,
/// so that a censor arrives and leaves rather than appearing between one frame and the
/// next.
/// </para>
/// <para>
/// macshot cross-fades by rendering the obscured region at a reduced alpha over the
/// original through <c>CIColorMatrix</c>. There is no compositor here to hand a partial
/// alpha to, so the region is kept, obscured at full strength, and mixed back — the same
/// arithmetic in the same order.
/// </para>
/// </remarks>
public static class FrameCensor
{
    private const int BytesPerPixel = 4;

    /// <summary>
    /// Below this the censor is not visible and the region would be copied twice for
    /// nothing. macshot's own threshold on the same comparison.
    /// </summary>
    private const double Invisible = 0.001;

    /// <summary>
    /// Applies <paramref name="style"/> to <paramref name="region"/> of a BGRA,
    /// top-down frame at <paramref name="opacity"/> of full strength.
    /// </summary>
    /// <param name="region">
    /// In pixels of this frame — that is, of the export, after any zoom has been applied.
    /// See <see cref="VideoOverlayGeometry.OutputRect"/>.
    /// </param>
    public static void Apply(
        byte[] pixels,
        int width,
        int height,
        CaptureRegion region,
        VideoCensorStyle style,
        double opacity)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (opacity <= Invisible)
        {
            return;
        }

        var clipped = region.Intersect(new CaptureRegion(0, 0, width, height));
        var left = (int)Math.Floor(clipped.X);
        var top = (int)Math.Floor(clipped.Y);
        var right = (int)Math.Ceiling(clipped.Right);
        var bottom = (int)Math.Ceiling(clipped.Bottom);

        // A rectangle a pixel wide cannot be obscured usefully and would make the box
        // blur's radius larger than the thing it is blurring. macshot skips it too.
        if (right - left < 2 || bottom - top < 2)
        {
            return;
        }

        var target = new CaptureRegion(left, top, right - left, bottom - top);
        var full = opacity >= 1 - Invisible;
        var before = full ? null : Extract(pixels, width, target);

        Obscure(pixels, width, height, target, style);

        if (before is not null)
        {
            MixBack(pixels, width, target, before, 1 - opacity);
        }
    }

    private static void Obscure(
        byte[] pixels,
        int width,
        int height,
        CaptureRegion region,
        VideoCensorStyle style)
    {
        switch (style)
        {
            case VideoCensorStyle.Solid:
                Fill(pixels, width, region);
                break;

            case VideoCensorStyle.Pixelate:
                PixelEffects.Pixelate(
                    pixels,
                    width,
                    height,
                    region,
                    VideoCensorSegment.PixelateBlockSize);
                break;

            default:
                PixelEffects.Blur(pixels, width, height, region, VideoCensorSegment.BlurRadius);
                break;
        }
    }

    /// <remarks>
    /// Black and opaque, which is macshot's solid censor. The alpha is written as well as
    /// the colour because everything downstream of here treats the frame as opaque, and a
    /// region left at whatever alpha the decoder happened to produce would come out of
    /// the encoder as a hole rather than as a redaction.
    /// </remarks>
    private static void Fill(byte[] pixels, int width, CaptureRegion region)
    {
        for (var row = (int)region.Y; row < (int)region.Bottom; row++)
        {
            var line = row * width * BytesPerPixel;
            for (var column = (int)region.X; column < (int)region.Right; column++)
            {
                var offset = line + (column * BytesPerPixel);
                pixels[offset] = 0;
                pixels[offset + 1] = 0;
                pixels[offset + 2] = 0;
                pixels[offset + 3] = byte.MaxValue;
            }
        }
    }

    private static byte[] Extract(byte[] pixels, int width, CaptureRegion region)
    {
        var regionWidth = (int)region.Width;
        var regionHeight = (int)region.Height;
        var copy = new byte[regionWidth * regionHeight * BytesPerPixel];

        for (var row = 0; row < regionHeight; row++)
        {
            var from = ((((int)region.Y + row) * width) + (int)region.X) * BytesPerPixel;
            Array.Copy(pixels, from, copy, row * regionWidth * BytesPerPixel, regionWidth * BytesPerPixel);
        }

        return copy;
    }

    /// <summary>Mixes <paramref name="before"/> back in at <paramref name="weight"/>.</summary>
    private static void MixBack(
        byte[] pixels,
        int width,
        CaptureRegion region,
        byte[] before,
        double weight)
    {
        var regionWidth = (int)region.Width;
        var regionHeight = (int)region.Height;

        for (var row = 0; row < regionHeight; row++)
        {
            var into = ((((int)region.Y + row) * width) + (int)region.X) * BytesPerPixel;
            var from = row * regionWidth * BytesPerPixel;

            for (var byteIndex = 0; byteIndex < regionWidth * BytesPerPixel; byteIndex++)
            {
                // Rounded rather than truncated, for the reason FrameZoom rounds: a whole
                // level lost per channel per frame bands a gradient visibly, and a censor
                // ramping in over a third of a second is nine or ten such frames in a row.
                pixels[into + byteIndex] = (byte)Math.Clamp(
                    Math.Round((pixels[into + byteIndex] * (1 - weight)) + (before[from + byteIndex] * weight)),
                    0,
                    255);
            }
        }
    }
}
