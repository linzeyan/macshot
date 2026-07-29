using Macshot.Windows.Core.Annotations;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// One background a capture can be presented on: a named gradient between two or
/// three colours, at an angle across the finished image.
/// </summary>
/// <param name="Angle">
/// Degrees clockwise from left-to-right, so 0 runs across and 90 runs down. Measured
/// on the output rather than on the capture, which is what keeps a wide screenshot
/// and a tall one looking like the same style.
/// </param>
public sealed record BeautifyStyle(string Name, double Angle, params AnnotationColor[] Stops)
{
    /// <summary>
    /// The colour this style shows at <paramref name="progress"/> along the gradient,
    /// interpolated between the two stops it falls between.
    /// </summary>
    public AnnotationColor Sample(double progress)
    {
        if (Stops.Length == 0)
        {
            return new AnnotationColor(0, 0, 0);
        }

        if (Stops.Length == 1)
        {
            return Stops[0];
        }

        var scaled = Math.Clamp(progress, 0, 1) * (Stops.Length - 1);
        var index = Math.Min((int)scaled, Stops.Length - 2);
        var local = scaled - index;

        var from = Stops[index];
        var to = Stops[index + 1];
        return new AnnotationColor(
            Mix(from.Red, to.Red, local),
            Mix(from.Green, to.Green, local),
            Mix(from.Blue, to.Blue, local));
    }

    private static byte Mix(byte from, byte to, double progress) =>
        (byte)Math.Clamp(Math.Round(from + ((to - from) * progress)), 0, byte.MaxValue);
}

/// <summary>How a capture is framed: which background, how much of it, and how soft.</summary>
/// <remarks>
/// Every measurement is a fraction of the capture's shorter side rather than a pixel
/// count. Forty pixels of padding frames a phone-sized screenshot and disappears
/// around a 4K one, and the point of the feature is that the result looks the same
/// whatever was captured.
/// </remarks>
public sealed record BeautifyOptions(
    int StyleIndex = 0,
    double Padding = 0.08,
    double CornerRadius = 0.02,
    double ShadowRadius = 0.03,
    double ShadowOpacity = 0.35)
{
    public static BeautifyOptions Default { get; } = new();

    /// <summary>
    /// Clamps every field into the range the renderer can honour, so a hand-edited
    /// settings file cannot ask for a frame larger than the image inside it.
    /// </summary>
    public BeautifyOptions Normalized()
    {
        return this with
        {
            StyleIndex = BeautifyRenderer.Styles.Count == 0
                ? 0
                : Math.Clamp(StyleIndex, 0, BeautifyRenderer.Styles.Count - 1),
            Padding = Math.Clamp(Padding, 0, 0.5),
            CornerRadius = Math.Clamp(CornerRadius, 0, 0.5),
            ShadowRadius = Math.Clamp(ShadowRadius, 0, 0.25),
            ShadowOpacity = Math.Clamp(ShadowOpacity, 0, 1),
        };
    }
}

/// <summary>
/// Puts a capture on a gradient background, with rounded corners and a soft shadow.
/// </summary>
/// <remarks>
/// <para>
/// The macOS product renders this with SwiftUI and hands the work to the GPU. There
/// is no equivalent here worth taking a dependency for: the whole effect is a
/// gradient, a rounded rectangle, and a shadow, and all three are cheaper to write
/// than to import. Doing it in Core also means the preview and the exported file come
/// from one path, and that it can be tested without a display.
/// </para>
/// <para>
/// The corners and the shadow both come from one signed distance to the rounded
/// rectangle, which is what stops the shadow's shape and the image's own edge
/// disagreeing by a pixel at the corners — the one place a mismatch shows.
/// </para>
/// </remarks>
public static class BeautifyRenderer
{
    /// <summary>
    /// The backgrounds on offer. Ordered so the first is the safe one: a neutral
    /// slate that does not compete with whatever was captured.
    /// </summary>
    public static IReadOnlyList<BeautifyStyle> Styles { get; } =
    [
        new("Slate", 135, Rgb(0x3A, 0x41, 0x4E), Rgb(0x1B, 0x1F, 0x27)),
        new("Graphite", 135, Rgb(0x6B, 0x6B, 0x6B), Rgb(0x24, 0x24, 0x24)),
        new("Paper", 135, Rgb(0xF5, 0xF2, 0xEA), Rgb(0xD9, 0xD3, 0xC6)),
        new("Sky", 135, Rgb(0x4F, 0xAC, 0xFE), Rgb(0x00, 0xF2, 0xFE)),
        new("Ocean", 135, Rgb(0x20, 0x39, 0xA0), Rgb(0x21, 0xB6, 0xCF)),
        new("Mint", 135, Rgb(0x43, 0xE9, 0x7B), Rgb(0x38, 0xF9, 0xD7)),
        new("Sunset", 135, Rgb(0xFA, 0x70, 0x9A), Rgb(0xFE, 0xE1, 0x40)),
        new("Ember", 135, Rgb(0xF8, 0x31, 0x60), Rgb(0xFF, 0x9A, 0x44)),
        new("Grape", 135, Rgb(0x8E, 0x2D, 0xE2), Rgb(0x4A, 0x00, 0xE0)),
        new("Orchid", 135, Rgb(0xE0, 0xC3, 0xFC), Rgb(0x8E, 0xC5, 0xFC)),
        new("Peach", 135, Rgb(0xFF, 0xE0, 0xC3), Rgb(0xFF, 0xAF, 0xBD)),
        new("Forest", 135, Rgb(0x13, 0x4E, 0x5E), Rgb(0x71, 0xB2, 0x80)),
        new("Dusk", 135, Rgb(0x2B, 0x32, 0xB2), Rgb(0x48, 0x8C, 0xC4), Rgb(0x2B, 0xC0, 0xE4)),
        new("Aurora", 135, Rgb(0x00, 0xC6, 0xFB), Rgb(0x00, 0x5B, 0xEA), Rgb(0x8E, 0x2D, 0xE2)),
        new("Citrus", 135, Rgb(0xF0, 0x9E, 0x19), Rgb(0xF5, 0x51, 0x5B), Rgb(0xB2, 0x2E, 0x8A)),
        new("Midnight", 90, Rgb(0x0F, 0x20, 0x27), Rgb(0x20, 0x3A, 0x43), Rgb(0x2C, 0x53, 0x64)),
    ];

    /// <summary>
    /// Frames <paramref name="bgraPixels"/> and returns the larger image.
    /// </summary>
    /// <remarks>
    /// The capture is copied in unchanged rather than resampled. Everything drawn here
    /// is around it, so the pixels the user chose come out the size they went in and
    /// nothing softens the text inside them.
    /// </remarks>
    public static (int Width, int Height, byte[] Pixels) Render(
        int width,
        int height,
        ReadOnlySpan<byte> bgraPixels,
        BeautifyOptions? options = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (bgraPixels.Length != checked(width * height * 4))
        {
            throw new ArgumentException("The pixel buffer does not match the frame dimensions.", nameof(bgraPixels));
        }

        var resolved = (options ?? BeautifyOptions.Default).Normalized();
        var style = Styles.Count == 0
            ? new BeautifyStyle("None", 0, new AnnotationColor(0, 0, 0))
            : Styles[resolved.StyleIndex];

        var shortest = Math.Min(width, height);
        var padding = (int)Math.Round(shortest * resolved.Padding);
        var radius = shortest * resolved.CornerRadius;
        var shadow = shortest * resolved.ShadowRadius;

        var outputWidth = width + (padding * 2);
        var outputHeight = height + (padding * 2);
        var output = new byte[checked(outputWidth * outputHeight * 4)];

        // The shadow falls downwards, the way a card lifted off the page would cast
        // one. Tied to its own radius, so softening it also moves it.
        var shadowOffset = shadow * 0.4;

        for (var row = 0; row < outputHeight; row++)
        {
            for (var column = 0; column < outputWidth; column++)
            {
                var offset = ((row * outputWidth) + column) * 4;
                var background = style.Sample(
                    GradientProgress(style.Angle, column, row, outputWidth, outputHeight));

                var pixelX = column + 0.5 - padding;
                var pixelY = row + 0.5 - padding;

                var blue = background.Blue;
                var green = background.Green;
                var red = background.Red;

                if (shadow > 0 && resolved.ShadowOpacity > 0)
                {
                    var shadowDistance = RoundedBoxDistance(pixelX, pixelY - shadowOffset, width, height, radius);
                    var cast = 1 - Smoothstep(0, shadow, shadowDistance);
                    if (cast > 0)
                    {
                        var strength = cast * resolved.ShadowOpacity;
                        blue = Blend(blue, 0, strength);
                        green = Blend(green, 0, strength);
                        red = Blend(red, 0, strength);
                    }
                }

                // Negative inside the card, positive outside. One pixel of feather
                // across the edge is what turns a stair-stepped corner into a drawn one.
                var distance = RoundedBoxDistance(pixelX, pixelY, width, height, radius);
                var coverage = 1 - Smoothstep(-0.5, 0.5, distance);
                if (coverage > 0)
                {
                    var sourceX = Math.Clamp((int)pixelX, 0, width - 1);
                    var sourceY = Math.Clamp((int)pixelY, 0, height - 1);
                    var from = ((sourceY * width) + sourceX) * 4;

                    blue = Blend(blue, bgraPixels[from], coverage);
                    green = Blend(green, bgraPixels[from + 1], coverage);
                    red = Blend(red, bgraPixels[from + 2], coverage);
                }

                output[offset] = blue;
                output[offset + 1] = green;
                output[offset + 2] = red;
                output[offset + 3] = byte.MaxValue;
            }
        }

        return (outputWidth, outputHeight, output);
    }

    private static AnnotationColor Rgb(byte red, byte green, byte blue) => new(red, green, blue);

    /// <summary>
    /// How far along the gradient a pixel sits, as a fraction, by projecting it onto
    /// the gradient's direction and normalizing by the span that direction covers.
    /// </summary>
    private static double GradientProgress(double angleDegrees, int column, int row, int width, int height)
    {
        var radians = angleDegrees * Math.PI / 180;
        var directionX = Math.Cos(radians);
        var directionY = Math.Sin(radians);

        // The rectangle's extent along the direction, which is what puts the first and
        // last stop exactly on opposite corners at any angle.
        var span = (Math.Abs(directionX) * width) + (Math.Abs(directionY) * height);
        if (span <= 0)
        {
            return 0;
        }

        var projected = ((column + 0.5) * directionX) + ((row + 0.5) * directionY);
        var origin = (Math.Min(0, directionX) * width) + (Math.Min(0, directionY) * height);
        return (projected - origin) / span;
    }

    /// <summary>
    /// Signed distance from a point to a rounded rectangle whose top-left is the
    /// origin: negative inside, positive outside, zero on the edge.
    /// </summary>
    private static double RoundedBoxDistance(double x, double y, double width, double height, double radius)
    {
        var corner = Math.Clamp(radius, 0, Math.Min(width, height) / 2);

        // Measured from the centre, which makes the two halves symmetric and lets one
        // expression cover all four corners.
        var offsetX = Math.Abs(x - (width / 2)) - ((width / 2) - corner);
        var offsetY = Math.Abs(y - (height / 2)) - ((height / 2) - corner);

        var outsideX = Math.Max(offsetX, 0);
        var outsideY = Math.Max(offsetY, 0);
        var outside = Math.Sqrt((outsideX * outsideX) + (outsideY * outsideY));
        var inside = Math.Min(Math.Max(offsetX, offsetY), 0);
        return outside + inside - corner;
    }

    private static double Smoothstep(double edge0, double edge1, double value)
    {
        if (edge1 <= edge0)
        {
            return value < edge0 ? 0 : 1;
        }

        var progress = Math.Clamp((value - edge0) / (edge1 - edge0), 0, 1);
        return progress * progress * (3 - (2 * progress));
    }

    private static byte Blend(byte under, byte over, double coverage) =>
        (byte)Math.Clamp(Math.Round(under + ((over - under) * coverage)), 0, byte.MaxValue);
}
