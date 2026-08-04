namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// The arithmetic either background-removal backend needs: a capture turned into the
/// tensor a segmentation model wants, that model's answer turned into coverage, and the
/// coverage applied to the capture as alpha.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than beside the backends because it is the half that can be tested off
/// Windows, and because the two backends must not disagree about it. Windows AI Foundry
/// hands back a mask and the local U²-Net model hands back a prediction; from the point
/// where there is a mask, what happens to the pixels has to be one rule, or the same
/// capture comes out with different edges depending on which machine cut it.
/// </para>
/// <para>
/// The constants are the U²-Net family's, as the reference implementation uses them: a
/// 320×320 input, ImageNet channel statistics, and a prediction stretched to its own
/// range. They are shared by every U²-Net variant, so a second model can be offered
/// without a second code path.
/// </para>
/// </remarks>
public static class SubjectCutout
{
    /// <summary>The square the model is fed, whatever shape the capture is.</summary>
    public const int ModelSide = 320;

    // ImageNet statistics, in RGB order. The model was trained on inputs normalized with
    // these; feeding it raw bytes produces a mask that looks almost right and is wrong at
    // every edge, which is the kind of failure nobody reports as a bug.
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Deviation = [0.229f, 0.224f, 0.225f];

    /// <summary>
    /// A BGRA capture as the NCHW float tensor the model takes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bilinear rather than the reference implementation's Lanczos. The input is 320
    /// pixels square and the output is a coverage mask, not a picture: what a sharper
    /// downscale buys is detail the model then averages away, and Lanczos here would be a
    /// windowed convolution written by hand for no visible difference.
    /// </para>
    /// <para>
    /// Divided by the frame's own brightest channel rather than by 255, which is what the
    /// reference does. On an ordinary screenshot the two agree, because something on
    /// screen is white; on a dark capture dividing by 255 leaves every input near zero and
    /// the model sees a picture it was never shown.
    /// </para>
    /// </remarks>
    public static float[] Prepare(ReadOnlySpan<byte> bgra, int width, int height, int side = ModelSide)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(side);

        if (bgra.Length < width * height * 4)
        {
            throw new ArgumentException("the buffer is smaller than the frame it describes", nameof(bgra));
        }

        var brightest = 0;
        for (var index = 0; index < width * height * 4; index += 4)
        {
            brightest = Math.Max(brightest, Math.Max(bgra[index], Math.Max(bgra[index + 1], bgra[index + 2])));
        }

        // A frame that is pure black divides by nothing. Fall back to the full range so the
        // tensor is zeros rather than NaNs — the model then finds no subject, which is the
        // truthful answer for a black rectangle.
        var scale = brightest == 0 ? 1f / 255f : 1f / brightest;

        var plane = side * side;
        var tensor = new float[3 * plane];

        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var (blue, green, red) = SampleBilinear(bgra, width, height, (x + 0.5f) * width / side - 0.5f, (y + 0.5f) * height / side - 0.5f);
                var at = (y * side) + x;

                // RGB, not the BGRA the frame is in. The channels carry different statistics
                // and a swapped pair costs mask quality without costing a single error.
                tensor[at] = ((red * scale) - Mean[0]) / Deviation[0];
                tensor[plane + at] = ((green * scale) - Mean[1]) / Deviation[1];
                tensor[(2 * plane) + at] = ((blue * scale) - Mean[2]) / Deviation[2];
            }
        }

        return tensor;
    }

    /// <summary>
    /// A model's raw prediction as one coverage byte per pixel.
    /// </summary>
    /// <remarks>
    /// Stretched to its own range rather than clamped to 0..1, which is what the reference
    /// implementation does and what the model's own training assumes. A prediction that
    /// spans 0.2 to 0.4 is a confident mask expressed narrowly; read literally it is a
    /// uniform grey haze over the whole picture.
    /// </remarks>
    public static byte[] ToCoverage(ReadOnlySpan<float> prediction)
    {
        if (prediction.IsEmpty)
        {
            return [];
        }

        var low = float.MaxValue;
        var high = float.MinValue;
        foreach (var value in prediction)
        {
            low = Math.Min(low, value);
            high = Math.Max(high, value);
        }

        var span = high - low;
        var coverage = new byte[prediction.Length];

        // A flat prediction means the model separated nothing. Zero rather than 255: an
        // empty cut-out is reported to the user, a fully opaque one silently does nothing.
        if (span <= float.Epsilon)
        {
            return coverage;
        }

        for (var index = 0; index < prediction.Length; index++)
        {
            coverage[index] = (byte)Math.Clamp((prediction[index] - low) / span * 255f, 0f, 255f);
        }

        return coverage;
    }

    /// <summary>
    /// The frame with the coverage as its alpha channel, and how many pixels survived.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sampled by position rather than copied row for row. Nothing promises a mask comes
    /// back the size that went in — Foundry's does not — and a mask read at the wrong
    /// stride is a cut-out sheared diagonally across the picture, a failure that looks
    /// like a bad model rather than a bad loop.
    /// </para>
    /// <para>
    /// Interpolated, because a 320-pixel mask stretched over a 4K capture is a twelvefold
    /// magnification: sampled nearest it gives a staircase along every edge of the subject,
    /// which is the one part of a cut-out anybody looks at.
    /// </para>
    /// <para>
    /// Alpha is written straight rather than premultiplied, because that is what the PNG
    /// encoder is told these pixels are.
    /// </para>
    /// </remarks>
    public static byte[] Cut(
        ReadOnlySpan<byte> bgra,
        int width,
        int height,
        ReadOnlySpan<byte> coverage,
        int maskWidth,
        int maskHeight,
        out int lifted)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maskWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maskHeight);

        if (coverage.Length < maskWidth * maskHeight)
        {
            throw new ArgumentException("the mask is smaller than the size it claims", nameof(coverage));
        }

        var pixels = new byte[width * height * 4];
        bgra[..(width * height * 4)].CopyTo(pixels);

        lifted = 0;
        for (var y = 0; y < height; y++)
        {
            var maskY = ((y + 0.5f) * maskHeight / height) - 0.5f;
            var row = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var maskX = ((x + 0.5f) * maskWidth / width) - 0.5f;
                var alpha = (byte)Math.Clamp(SampleCoverage(coverage, maskWidth, maskHeight, maskX, maskY) + 0.5f, 0f, 255f);

                pixels[row + (x * 4) + 3] = alpha;
                if (alpha > 0)
                {
                    lifted++;
                }
            }
        }

        return pixels;
    }

    /// <summary>One interpolated coverage byte, with the point clamped into the mask.</summary>
    private static float SampleCoverage(ReadOnlySpan<byte> coverage, int width, int height, float x, float y)
    {
        var (left, right, alongX) = Neighbours(x, width);
        var (top, bottom, alongY) = Neighbours(y, height);

        var upper = Lerp(coverage[(top * width) + left], coverage[(top * width) + right], alongX);
        var lower = Lerp(coverage[(bottom * width) + left], coverage[(bottom * width) + right], alongX);

        return Lerp(upper, lower, alongY);
    }

    /// <summary>One interpolated BGRA pixel, with the point clamped into the frame.</summary>
    private static (float Blue, float Green, float Red) SampleBilinear(ReadOnlySpan<byte> bgra, int width, int height, float x, float y)
    {
        var (left, right, alongX) = Neighbours(x, width);
        var (top, bottom, alongY) = Neighbours(y, height);

        var topLeft = ((top * width) + left) * 4;
        var topRight = ((top * width) + right) * 4;
        var bottomLeft = ((bottom * width) + left) * 4;
        var bottomRight = ((bottom * width) + right) * 4;

        return (
            Blend(bgra, topLeft, topRight, bottomLeft, bottomRight, 0, alongX, alongY),
            Blend(bgra, topLeft, topRight, bottomLeft, bottomRight, 1, alongX, alongY),
            Blend(bgra, topLeft, topRight, bottomLeft, bottomRight, 2, alongX, alongY));
    }

    private static float Blend(
        ReadOnlySpan<byte> bgra,
        int topLeft,
        int topRight,
        int bottomLeft,
        int bottomRight,
        int channel,
        float alongX,
        float alongY)
    {
        var upper = Lerp(bgra[topLeft + channel], bgra[topRight + channel], alongX);
        var lower = Lerp(bgra[bottomLeft + channel], bgra[bottomRight + channel], alongX);

        return Lerp(upper, lower, alongY);
    }

    /// <summary>
    /// The two sample positions either side of a coordinate, and how far between them it
    /// falls. Clamped rather than wrapped: an edge pixel repeats itself, where wrapping
    /// would bleed the opposite side of the picture into the border of the mask.
    /// </summary>
    private static (int Low, int High, float Fraction) Neighbours(float position, int extent)
    {
        var clamped = Math.Clamp(position, 0f, extent - 1f);
        var low = (int)MathF.Floor(clamped);
        var high = Math.Min(low + 1, extent - 1);

        return (low, high, clamped - low);
    }

    private static float Lerp(float from, float to, float fraction) => from + ((to - from) * fraction);
}
