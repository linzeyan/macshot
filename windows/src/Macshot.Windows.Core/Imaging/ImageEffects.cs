using Macshot.Windows.Core.Annotations;

namespace Macshot.Windows.Core.Imaging;

/// <summary>The named looks the Adjust popover offers, in macshot's order.</summary>
public enum ImageEffectPreset
{
    None = 0,
    Noir = 1,
    Mono = 2,
    Sepia = 3,
    Chrome = 4,
    Fade = 5,
    Instant = 6,
    Vivid = 7,
}

/// <summary>
/// What the Adjust popover is currently asking for.
/// </summary>
/// <remarks>
/// The four ranges are macshot's own — <c>ImageEffects.swift:42–45</c> — so a slider
/// dragged to the end means the same thing on both products.
/// </remarks>
public sealed record ImageEffectsOptions(
    ImageEffectPreset Preset = ImageEffectPreset.None,
    double Brightness = 0,
    double Contrast = 1,
    double Saturation = 1,
    double Sharpness = 0)
{
    public static ImageEffectsOptions Default { get; } = new();

    /// <summary>
    /// Nothing to do. Checked before any work, because the common case is a capture
    /// nobody opened this popover for, and copying a 4K frame to change nothing is a
    /// visible pause.
    /// </summary>
    public bool IsIdentity =>
        Preset == ImageEffectPreset.None && Brightness == 0 && Contrast == 1 && Saturation == 1 && Sharpness == 0;

    /// <summary>
    /// Clamps every field into the range the sliders offer, so a hand-edited settings
    /// file cannot ask for a contrast that drives the whole image to two colours.
    /// </summary>
    public ImageEffectsOptions Normalized() => this with
    {
        Brightness = Math.Clamp(Brightness, -0.5, 0.5),
        Contrast = Math.Clamp(Contrast, 0.5, 2),
        Saturation = Math.Clamp(Saturation, 0, 2),
        Sharpness = Math.Clamp(Sharpness, 0, 2),
    };
}

/// <summary>
/// The photographic adjustments behind macshot's Adjust button: a named look, then
/// brightness, contrast and saturation, then sharpening.
/// </summary>
/// <remarks>
/// <para>
/// macOS reaches for Core Image, whose <c>CIPhotoEffect</c> filters are Apple's own
/// closed tone curves. There is nothing to import here that would reproduce them
/// exactly, so the presets are built from the same materials the names describe —
/// a greyscale conversion, a warm or cool tint, a contrast curve — and are honest
/// approximations rather than the same numbers. The names, the order, the slider
/// ranges and the order the stages run in do match.
/// </para>
/// <para>
/// In Core rather than in the app layer for the reason every other image operation is:
/// the preview and the exported file then come out of one path, and the arithmetic can
/// be tested without a display.
/// </para>
/// </remarks>
public static class ImageEffects
{
    /// <summary>
    /// Luma weights. Rec. 709, which is what a screenshot of an sRGB display is in —
    /// Rec. 601's weights would make greens read darker than they look.
    /// </summary>
    private const double RedLuma = 0.2126;
    private const double GreenLuma = 0.7152;
    private const double BlueLuma = 0.0722;

    /// <summary>Vivid is a fixed look rather than slider state — <c>ImageEffects.swift:56</c>.</summary>
    private const double VividContrast = 1.2;
    private const double VividSaturation = 1.5;

    /// <summary>What macshot's sepia is mixed at — <c>CISepiaTone</c> intensity 0.8.</summary>
    private const double SepiaIntensity = 0.8;

    /// <summary>The name shown under each swatch.</summary>
    public static string DisplayName(ImageEffectPreset preset) => preset switch
    {
        ImageEffectPreset.None => "None",
        ImageEffectPreset.Noir => "Noir",
        ImageEffectPreset.Mono => "Mono",
        ImageEffectPreset.Sepia => "Sepia",
        ImageEffectPreset.Chrome => "Chrome",
        ImageEffectPreset.Fade => "Fade",
        ImageEffectPreset.Instant => "Instant",
        ImageEffectPreset.Vivid => "Vivid",
        _ => preset.ToString(),
    };

    /// <summary>
    /// Applies the adjustments and answers a new buffer, or the pixels handed in when
    /// there is nothing to do.
    /// </summary>
    public static byte[] Apply(int width, int height, ReadOnlySpan<byte> bgraPixels, ImageEffectsOptions? options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (bgraPixels.Length != checked(width * height * 4))
        {
            throw new ArgumentException("The pixel buffer does not match the frame dimensions.", nameof(bgraPixels));
        }

        var resolved = (options ?? ImageEffectsOptions.Default).Normalized();
        if (resolved.IsIdentity)
        {
            return bgraPixels.ToArray();
        }

        var output = bgraPixels.ToArray();

        // Vivid is the one preset that is a pair of slider values rather than a look,
        // and macshot applies it *instead of* the sliders so the two cannot compound.
        var colour = resolved.Preset == ImageEffectPreset.Vivid
            ? new ImageEffectsOptions(ImageEffectPreset.None, 0, VividContrast, VividSaturation)
            : resolved;

        for (var index = 0; index < output.Length; index += 4)
        {
            var blue = output[index] / 255.0;
            var green = output[index + 1] / 255.0;
            var red = output[index + 2] / 255.0;

            (red, green, blue) = ApplyPreset(colour.Preset, red, green, blue);
            (red, green, blue) = ApplyControls(colour, red, green, blue);

            output[index] = ToByte(blue);
            output[index + 1] = ToByte(green);
            output[index + 2] = ToByte(red);

            // Alpha is left alone. An adjustment is about how the picture looks, and a
            // capture that became partly transparent could not be saved as a JPEG.
        }

        return resolved.Sharpness > 0
            ? Sharpen(width, height, output, resolved.Sharpness)
            : output;
    }

    /// <summary>
    /// The named look. Each is the plainest reading of its name: Noir is greyscale with
    /// the contrast pushed, Mono is greyscale, Fade lifts the blacks and drains the
    /// colour, Instant is Fade with the warmth an instant print has.
    /// </summary>
    private static (double Red, double Green, double Blue) ApplyPreset(
        ImageEffectPreset preset,
        double red,
        double green,
        double blue)
    {
        var luma = (red * RedLuma) + (green * GreenLuma) + (blue * BlueLuma);

        return preset switch
        {
            ImageEffectPreset.Mono => (luma, luma, luma),
            ImageEffectPreset.Noir => Curve(luma, 1.35),
            ImageEffectPreset.Sepia => Mix(red, green, blue, luma * 1.07, luma * 0.74, luma * 0.43, SepiaIntensity),
            ImageEffectPreset.Chrome => Saturate(Contrast(red, green, blue, 1.15), 1.25),
            ImageEffectPreset.Fade => Lift(Saturate((red, green, blue), 0.75), 0.08),
            ImageEffectPreset.Instant => Warm(Lift(Saturate((red, green, blue), 0.85), 0.1)),
            _ => (red, green, blue),
        };
    }

    /// <summary>
    /// Saturation, then contrast about mid grey, then brightness — the order Core
    /// Image's colour controls run in, and the reason contrast at 2 does not also
    /// double whatever brightness added.
    /// </summary>
    private static (double Red, double Green, double Blue) ApplyControls(
        ImageEffectsOptions options,
        double red,
        double green,
        double blue)
    {
        var (r, g, b) = Saturate((red, green, blue), options.Saturation);
        (r, g, b) = Contrast(r, g, b, options.Contrast);
        return (r + options.Brightness, g + options.Brightness, b + options.Brightness);
    }

    private static (double Red, double Green, double Blue) Saturate(
        (double Red, double Green, double Blue) colour,
        double amount)
    {
        if (amount == 1)
        {
            return colour;
        }

        var luma = (colour.Red * RedLuma) + (colour.Green * GreenLuma) + (colour.Blue * BlueLuma);
        return (
            luma + ((colour.Red - luma) * amount),
            luma + ((colour.Green - luma) * amount),
            luma + ((colour.Blue - luma) * amount));
    }

    private static (double Red, double Green, double Blue) Contrast(double red, double green, double blue, double amount) =>
        amount == 1
            ? (red, green, blue)
            : (((red - 0.5) * amount) + 0.5, ((green - 0.5) * amount) + 0.5, ((blue - 0.5) * amount) + 0.5);

    /// <summary>A grey with its contrast pushed, which is what Noir is.</summary>
    private static (double Red, double Green, double Blue) Curve(double luma, double amount)
    {
        var pushed = ((luma - 0.5) * amount) + 0.5;
        return (pushed, pushed, pushed);
    }

    /// <summary>Blends a preset's answer with the original, for the ones that are a mix.</summary>
    private static (double Red, double Green, double Blue) Mix(
        double red,
        double green,
        double blue,
        double toRed,
        double toGreen,
        double toBlue,
        double amount) =>
        (red + ((toRed - red) * amount),
            green + ((toGreen - green) * amount),
            blue + ((toBlue - blue) * amount));

    /// <summary>
    /// Raises the black point, which is what makes a faded print look faded: nothing in
    /// it is quite black.
    /// </summary>
    private static (double Red, double Green, double Blue) Lift(
        (double Red, double Green, double Blue) colour,
        double amount) =>
        (colour.Red + (amount * (1 - colour.Red)),
            colour.Green + (amount * (1 - colour.Green)),
            colour.Blue + (amount * (1 - colour.Blue)));

    private static (double Red, double Green, double Blue) Warm(
        (double Red, double Green, double Blue) colour) =>
        (colour.Red * 1.06, colour.Green * 1.01, colour.Blue * 0.94);

    /// <summary>
    /// An unsharp mask on luminance only, which is what <c>CISharpenLuminance</c> is:
    /// sharpening the colour channels separately is what produces coloured fringes on
    /// the edges of text.
    /// </summary>
    private static byte[] Sharpen(int width, int height, byte[] bgraPixels, double amount)
    {
        var output = (byte[])bgraPixels.Clone();

        // The border is left as it is. A one-pixel frame is not worth the branch in the
        // inner loop, and a screenshot's outermost pixels are its window edge.
        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var offset = ((y * width) + x) * 4;
                var centre = Luma(bgraPixels, offset);
                var neighbours =
                    Luma(bgraPixels, offset - 4)
                    + Luma(bgraPixels, offset + 4)
                    + Luma(bgraPixels, offset - (width * 4))
                    + Luma(bgraPixels, offset + (width * 4));

                // How much brighter this pixel is than what is around it, added back on
                // top of itself: the edges gain, the flat areas move by nothing.
                var boost = (centre - (neighbours / 4)) * amount;
                if (boost == 0)
                {
                    continue;
                }

                output[offset] = ToByte((bgraPixels[offset] / 255.0) + boost);
                output[offset + 1] = ToByte((bgraPixels[offset + 1] / 255.0) + boost);
                output[offset + 2] = ToByte((bgraPixels[offset + 2] / 255.0) + boost);
            }
        }

        return output;
    }

    private static double Luma(byte[] pixels, int offset) =>
        ((pixels[offset + 2] * RedLuma) + (pixels[offset + 1] * GreenLuma) + (pixels[offset] * BlueLuma)) / 255.0;

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

    /// <summary>
    /// A small sample of a picture, for the swatch under each preset's name. Built here
    /// rather than in the app layer so the swatch is put through the same code the
    /// capture will be — a swatch drawn by other means is a promise the result may not
    /// keep.
    /// </summary>
    /// <remarks>
    /// A gradient with two shapes laid on it, which is macshot's own sample
    /// (<c>ImageEffects.swift:131-148</c>): a near-white disc on the left and a near-black
    /// bar on the right. The gradient alone — which is all this drew — shows what a preset
    /// does to hue and nothing at all about what it does to the ends of the range, so Noir,
    /// Mono, Chrome and Fade all came out as the same grey square and the grid was eight
    /// swatches saying nothing. The disc and the bar are where the difference between
    /// crushing the blacks and lifting them is visible.
    /// </remarks>
    public static (int Width, int Height, byte[] Pixels) Swatch(ImageEffectPreset preset, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        // macshot's fractions of the swatch, read in its bottom-left space and flipped
        // here because these pixels run top-down.
        var discRadius = size * 0.175;
        var discCentreX = (size * 0.15) + discRadius;
        var discCentreY = size - ((size * 0.3) + discRadius);
        var barLeft = size * 0.55;
        var barRight = barLeft + (size * 0.3);
        var barBottom = size - (size * 0.2);
        var barTop = barBottom - (size * 0.5);

        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                // A diagonal sweep through three colours, which is enough to show what a
                // preset does to hue, to contrast and to saturation at once.
                var along = (x + y) / (2.0 * (size - 1));
                var colour = along < 0.5
                    ? Blend(new AnnotationColor(51, 128, 230), new AnnotationColor(230, 153, 77), along * 2)
                    : Blend(new AnnotationColor(230, 153, 77), new AnnotationColor(77, 204, 128), (along - 0.5) * 2);

                // Pixel centres, so the disc's edge is where its radius says rather than
                // half a pixel inside it.
                var pointX = x + 0.5;
                var pointY = y + 0.5;

                var dx = pointX - discCentreX;
                var dy = pointY - discCentreY;
                if ((dx * dx) + (dy * dy) <= discRadius * discRadius)
                {
                    colour = Blend(colour, new AnnotationColor(255, 255, 255), 0.8);
                }
                else if (pointX >= barLeft && pointX <= barRight
                    && pointY >= barTop && pointY <= barBottom)
                {
                    colour = Blend(colour, new AnnotationColor(51, 51, 51), 0.6);
                }

                var offset = ((y * size) + x) * 4;
                pixels[offset] = colour.Blue;
                pixels[offset + 1] = colour.Green;
                pixels[offset + 2] = colour.Red;
                pixels[offset + 3] = byte.MaxValue;
            }
        }

        return (size, size, Apply(size, size, pixels, new ImageEffectsOptions(preset)));
    }

    private static AnnotationColor Blend(AnnotationColor from, AnnotationColor to, double amount) => new(
        (byte)Math.Round(from.Red + ((to.Red - from.Red) * amount)),
        (byte)Math.Round(from.Green + ((to.Green - from.Green) * amount)),
        (byte)Math.Round(from.Blue + ((to.Blue - from.Blue) * amount)));
}
