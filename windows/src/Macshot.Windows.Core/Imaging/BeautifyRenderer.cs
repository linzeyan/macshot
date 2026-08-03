using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

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
    /// Where each stop sits along the gradient, from 0 to 1. Null means evenly spaced,
    /// which is what most styles want.
    /// </summary>
    /// <remarks>
    /// A three-stop gradient with its middle at 0.45 rather than 0.5 is not a rounding
    /// difference — it is which of the two colours the image sits against, and several
    /// of the styles copied from the macOS product depend on it. Ignored unless it has
    /// exactly one entry per stop, so a mismatched catalogue degrades to even spacing
    /// rather than throwing at first use.
    /// </remarks>
    public double[]? Offsets { get; init; }

    /// <summary>
    /// The mesh this style really is, when it is one. Null for a plain linear gradient.
    /// </summary>
    /// <remarks>
    /// The stops are kept alongside it rather than replaced. They are macshot's own
    /// macOS 14 fallback, so they are the right answer if the mesh ever cannot be drawn,
    /// and they are what the swatch in a picker can be painted from without solving a
    /// patch inversion per pixel.
    /// </remarks>
    public BeautifyMesh? Mesh { get; init; }

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

        var clamped = Math.Clamp(progress, 0, 1);
        int index;
        double local;

        if (Offsets is { } offsets && offsets.Length == Stops.Length)
        {
            index = 0;
            while (index < offsets.Length - 2 && clamped > offsets[index + 1])
            {
                index++;
            }

            var span = offsets[index + 1] - offsets[index];
            local = span <= 0 ? 1 : Math.Clamp((clamped - offsets[index]) / span, 0, 1);
        }
        else
        {
            var scaled = clamped * (Stops.Length - 1);
            index = Math.Min((int)scaled, Stops.Length - 2);
            local = scaled - index;
        }

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

/// <summary>Which of the two cards a capture is mounted on.</summary>
/// <remarks>
/// The raw values are macshot's (<c>BeautifyRenderer.swift:4-7</c>) and are stored, so they
/// have to keep meaning the same thing in both products. Window is the default there
/// (<c>OverlayView.swift:441-442</c>), which is worth saying because a frame is much more
/// often wanted around a window than around a bare rectangle.
/// </remarks>
public enum BeautifyMode
{
    /// <summary>A macOS window: a title bar with traffic lights above the capture.</summary>
    Window = 0,

    /// <summary>The capture alone, corners rounded.</summary>
    Rounded = 1,
}

/// <summary>How a capture is framed: which background, how much of it, and how soft.</summary>
/// <remarks>
/// Every measurement is a fraction of the capture's shorter side rather than a pixel
/// count. Forty pixels of padding frames a phone-sized screenshot and disappears
/// around a 4K one, and the point of the feature is that the result looks the same
/// whatever was captured.
/// </remarks>
/// <param name="Padding">How far the frame reaches past the capture, in points.</param>
/// <param name="CornerRadius">How far the capture's corners are rounded, in points.</param>
/// <param name="ShadowRadius">How far the shadow spreads, in points.</param>
/// <remarks>
/// The three sizes are points and not fractions of the capture, which is macshot's choice
/// and worth stating because the port had it the other way round. A fraction makes the
/// frame grow with the picture, so the same slider position gives a hairline round a
/// screenshot of a dialog and a hand's breadth round one of a display — and the two
/// products then disagree at every setting rather than at none. The numbers here are
/// macshot's own defaults (<c>OverlayView.swift:443-455</c>) and its slider ranges.
/// </remarks>
public sealed record BeautifyOptions(
    int StyleIndex = 0,
    double Padding = 48,
    double CornerRadius = 10,
    double ShadowRadius = 20,
    double ShadowOpacity = 0.35,
    bool Enabled = false,
    BeautifyMode Mode = BeautifyMode.Window)
{
    /// <summary>
    /// How tall the window mode's title bar is, in points
    /// (<c>BeautifyRenderer.swift:741</c>).
    /// </summary>
    /// <remarks>
    /// The one measurement here that is not a slider, and the one that makes the frame
    /// asymmetric: everything else grows the card evenly, and this only grows it upwards.
    /// </remarks>
    public const double TitleBarHeight = 28;

    /// <summary>
    /// The narrowest frame the row can ask for — macshot's slider starts here
    /// (<c>ToolOptionsRowView.swift:1301</c>). Not a clamp on this record, which draws any
    /// width it is handed; it is the floor a stored setting is held to.
    /// </summary>
    public const double MinimumPadding = 16;

    public const double MaximumPadding = 96;

    /// <summary>The far end of the corner slider, in points.</summary>
    public const double MaximumCornerRadius = 30;

    /// <summary>The far end of the shadow slider, in points.</summary>
    public const double MaximumShadowRadius = 100;

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
            // The far ends the sliders offer, so a settings file cannot ask for a frame
            // the row has no way to ask back down again. The near end is not enforced:
            // macshot's slider starts at 16 but its stored value is unclamped, and no
            // padding at all is a thing the renderer can draw and callers do ask for.
            Padding = Math.Clamp(Padding, 0, MaximumPadding),
            CornerRadius = Math.Clamp(CornerRadius, 0, MaximumCornerRadius),
            ShadowRadius = Math.Clamp(ShadowRadius, 0, MaximumShadowRadius),
            ShadowOpacity = Math.Clamp(ShadowOpacity, 0, 1),

            // A stored number that names no mode falls back to the default rather than
            // drawing a title bar of some third height, which is what an unchecked cast
            // would let a hand-edited settings file ask for.
            Mode = Enum.IsDefined(Mode) ? Mode : BeautifyMode.Window,
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
    /// The backgrounds on offer, in the macOS product's order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the point: the chosen background is persisted as an index, and
    /// keeping it aligned means a style number names the same background in both
    /// products. The colours were extracted from the Swift source rather than
    /// transcribed, because a wrong digit in forty-eight gradients would look
    /// plausible and no test would catch it.
    /// </para>
    /// <para>
    /// The first eighteen are mesh gradients there. This renderer has no mesh, but
    /// macshot ships a linear fallback with each one — what it draws itself on macOS
    /// 14, where <c>MeshGradient</c> does not exist — so those are the colours used
    /// here. The result is dimmer than the mesh, not different from it.
    /// </para>
    /// <para>
    /// Only the first eighteen are named in the Swift; its picker is a grid of
    /// swatches, so the rest never needed words. The Frame menu here is a list, so
    /// the remaining thirty are named after macshot's own section comments and the
    /// colours each one runs through.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<BeautifyStyle> Styles { get; } = Meshed(
    [
        new("Ultraviolet", 135, Rgb(0x8C, 0x1A, 0xF2), Rgb(0xE6, 0x66, 0xE6), Rgb(0xD9, 0x59, 0xB2)),
        new("Inferno", 135, Rgb(0xFF, 0x40, 0x66), Rgb(0xFF, 0xA6, 0x4C), Rgb(0xFF, 0xD9, 0x40)),
        new("Deep Ocean", 135, Rgb(0x0D, 0x26, 0x99), Rgb(0x33, 0xE6, 0xF2), Rgb(0x0D, 0x2E, 0x8C)),
        new("Candy Floss", 135, Rgb(0xFF, 0x99, 0xB2), Rgb(0xFF, 0xD9, 0xB2), Rgb(0xB2, 0x80, 0xFA)),
        new("Emerald Fire", 135, Rgb(0x1A, 0xD9, 0x66), Rgb(0x99, 0xE6, 0x4C), Rgb(0xFF, 0x73, 0x1A)),
        new("Electric Dusk", 180, Rgb(0xFF, 0x80, 0x4C), Rgb(0xD9, 0x4C, 0x99), Rgb(0x14, 0x14, 0x66)),
        new("Plasma", 135, Rgb(0xF2, 0x33, 0x99), Rgb(0x33, 0xD9, 0xD9), Rgb(0x40, 0x26, 0xF2)),
        new("Silk Storm", 135, Rgb(0xEB, 0xE6, 0xF2), Rgb(0xD9, 0xBF, 0xF2), Rgb(0xA6, 0xBF, 0xF2)),
        new("Opal", 135, Rgb(0xFF, 0x99, 0x26), Rgb(0xA6, 0xB2, 0xF2), Rgb(0x99, 0x26, 0xD9)),
        new("Nebula", 135, Rgb(0x1A, 0x0D, 0x66), Rgb(0xFF, 0x4C, 0x8C), Rgb(0x1A, 0x66, 0xBF)),
        new("Sunset Blaze", 180, Rgb(0xFF, 0xD9, 0x40), Rgb(0xE6, 0x40, 0x66), Rgb(0x14, 0x0D, 0x59)),
        new("Lagoon", 135, Rgb(0x1A, 0xF2, 0xB2), Rgb(0x26, 0xE6, 0xD9), Rgb(0x1A, 0x59, 0xD9)),
        new("Molten Core", 135, Rgb(0x14, 0x0D, 0x0D), Rgb(0xFF, 0xB2, 0x26), Rgb(0x14, 0x0A, 0x0A)),
        new("Aurora Borealis", 180, Rgb(0x0D, 0xE6, 0x80), Rgb(0x26, 0xD9, 0xB2), Rgb(0x0A, 0x0F, 0x33)),
        new("Prism Burst", 135, Rgb(0x33, 0x66, 0xFF), Rgb(0xFF, 0xF2, 0x80), Rgb(0x99, 0x1A, 0xF2)),
        new("Velvet Night", 135, Rgb(0x40, 0x0D, 0x26), Rgb(0xF2, 0xA6, 0x80), Rgb(0x40, 0x14, 0x38)),
        new("Cosmic Reef", 135, Rgb(0x0F, 0x0A, 0x33), Rgb(0xE6, 0x73, 0x4C), Rgb(0xFF, 0xCC, 0x40)) { Offsets = [0, 0.4, 1] },
        new("Ember Glow", 135, Rgb(0xFF, 0xD9, 0x59), Rgb(0xF2, 0x66, 0x59), Rgb(0xBF, 0x26, 0x66)),
        new("Sunset", 135, Rgb(0xFF, 0x99, 0x26), Rgb(0xFA, 0x59, 0x4C), Rgb(0xD9, 0x2E, 0x73)) { Offsets = [0, 0.45, 1] },
        new("Apricot", 135, Rgb(0xFA, 0xD1, 0xAD), Rgb(0xF2, 0x99, 0x8C)),
        new("Wildfire", 135, Rgb(0xE6, 0x40, 0x1A), Rgb(0xF2, 0x8C, 0x0D), Rgb(0xFF, 0xD9, 0x33)),
        new("Azure", 135, Rgb(0x1A, 0xB2, 0xF2), Rgb(0x38, 0x66, 0xE6), Rgb(0x59, 0x33, 0xCC)) { Offsets = [0, 0.55, 1] },
        new("Ice", 160, Rgb(0xB8, 0xE6, 0xFA), Rgb(0x80, 0xBF, 0xF2)),
        new("Sapphire", 150, Rgb(0x0D, 0x26, 0x8C), Rgb(0x26, 0x59, 0xD9), Rgb(0x4C, 0x99, 0xF2)),
        new("Bloom", 135, Rgb(0xFA, 0x66, 0x8C), Rgb(0xE6, 0x4C, 0xB2), Rgb(0x99, 0x40, 0xE6), Rgb(0x59, 0x4C, 0xF2)) { Offsets = [0, 0.4, 0.75, 1] },
        new("Rosewood", 150, Rgb(0xF2, 0x40, 0x73), Rgb(0xEB, 0x80, 0x8C)),
        new("Lilac", 135, Rgb(0xBF, 0xA6, 0xF2), Rgb(0xE6, 0xC7, 0xFA)),
        new("Carnival", 135, Rgb(0xFA, 0x33, 0x99), Rgb(0xE6, 0x80, 0x26), Rgb(0x33, 0xE6, 0x99), Rgb(0x40, 0x80, 0xFA)) { Offsets = [0, 0.3, 0.6, 1] },
        new("Pine", 150, Rgb(0x0D, 0x73, 0x4C), Rgb(0x1A, 0x99, 0x66), Rgb(0x4C, 0xCC, 0x80)),
        new("Reef", 135, Rgb(0x1A, 0xBF, 0x80), Rgb(0x26, 0x8C, 0xCC), Rgb(0x66, 0x4C, 0xD9), Rgb(0xB2, 0x40, 0xBF)) { Offsets = [0, 0.35, 0.65, 1] },
        new("Meadow", 135, Rgb(0x8C, 0xE6, 0x33), Rgb(0x4C, 0xBF, 0x59), Rgb(0x26, 0x99, 0x73)),
        new("Daydream", 150, Rgb(0x8C, 0xD9, 0xFA), Rgb(0xBF, 0x99, 0xF2), Rgb(0xF2, 0x73, 0xB2), Rgb(0xFA, 0x8C, 0x66)) { Offsets = [0, 0.35, 0.7, 1] },
        new("Spectrum", 135, Rgb(0xF2, 0x4C, 0x4C), Rgb(0xF2, 0xB2, 0x33), Rgb(0x4C, 0xD9, 0x66), Rgb(0x4C, 0x99, 0xF2), Rgb(0xB2, 0x4C, 0xE6)),
        new("Twilight", 135, Rgb(0x26, 0x1A, 0x59), Rgb(0x73, 0x33, 0x99), Rgb(0xD9, 0x66, 0x80), Rgb(0xF2, 0xB2, 0x66)) { Offsets = [0, 0.4, 0.7, 1] },
        new("Seafoam", 120, Rgb(0x66, 0xE6, 0xD9), Rgb(0x80, 0xA6, 0xFA), Rgb(0xCC, 0x80, 0xF2), Rgb(0xF2, 0x99, 0xCC)) { Offsets = [0, 0.35, 0.65, 1] },
        new("Deep Space", 150, Rgb(0x0D, 0x0D, 0x26), Rgb(0x1A, 0x1A, 0x4C), Rgb(0x33, 0x26, 0x73)),
        new("Abyss", 135, Rgb(0x05, 0x0D, 0x1F), Rgb(0x0D, 0x26, 0x4C), Rgb(0x1A, 0x59, 0x80), Rgb(0x26, 0x80, 0x8C)) { Offsets = [0, 0.4, 0.75, 1] },
        new("Charcoal", 135, Rgb(0x08, 0x08, 0x08), Rgb(0x26, 0x26, 0x26)),
        new("Porcelain", 160, Rgb(0xF5, 0xF5, 0xF7), Rgb(0xE6, 0xE8, 0xED)),
        new("Parchment", 135, Rgb(0xFA, 0xF5, 0xE6), Rgb(0xF2, 0xE6, 0xCC)),
        new("Pewter", 135, Rgb(0x4C, 0x59, 0x6B), Rgb(0x73, 0x80, 0x94), Rgb(0x99, 0xA6, 0xB8)),
        new("Graphite", 150, Rgb(0x26, 0x26, 0x2E), Rgb(0x40, 0x40, 0x4C), Rgb(0x59, 0x59, 0x66)),
        new("Jade", 135, Rgb(0x00, 0x99, 0x66), Rgb(0x1A, 0xD9, 0x99), Rgb(0x00, 0x80, 0x4C)),
        new("Ruby", 150, Rgb(0xB2, 0x1A, 0x33), Rgb(0xE6, 0x33, 0x4C), Rgb(0x8C, 0x0D, 0x26)),
        new("Cobalt", 120, Rgb(0x1A, 0x26, 0x80), Rgb(0x33, 0x4C, 0xBF), Rgb(0x0D, 0x1A, 0x66)),
        new("Sand", 135, Rgb(0xD9, 0xBF, 0x8C), Rgb(0xF2, 0xE0, 0xB2), Rgb(0xBF, 0xA6, 0x73)),
        new("Paper", 180, Rgb(0xF2, 0xF2, 0xF2), Rgb(0xFF, 0xFF, 0xFF), Rgb(0xEB, 0xEB, 0xEB)),
        new("Ink", 180, Rgb(0x0D, 0x0D, 0x0D), Rgb(0x1F, 0x1F, 0x1F), Rgb(0x00, 0x00, 0x00)),
    ]);

    /// <summary>
    /// Hands each style that is really a mesh the mesh it is.
    /// </summary>
    /// <remarks>
    /// Done here rather than in the list above so the catalogue stays a readable column
    /// of colours, and so the eighteen meshes stay in the one file a script writes. The
    /// pairing is by position, which is the same thing that makes a stored style index
    /// mean the same background in both products.
    /// </remarks>
    private static IReadOnlyList<BeautifyStyle> Meshed(BeautifyStyle[] styles)
    {
        var meshes = BeautifyMeshes.Catalogue;
        for (var index = 0; index < meshes.Count && index < styles.Length; index++)
        {
            styles[index] = styles[index] with { Mesh = meshes[index] };
        }

        return styles;
    }

    /// <summary>
    /// A square of one style's background, for the swatch it is picked by.
    /// </summary>
    /// <remarks>
    /// Painted by the same sampler the framed image is, so what is picked from is what
    /// arrives — the meshes especially, where the whole character of the style is that
    /// it bulges rather than runs in a line, and a swatch drawn as a plain gradient
    /// would be a promise the result does not keep.
    /// </remarks>
    public static (int Width, int Height, byte[] Pixels) Swatch(int styleIndex, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        var style = Styles[Math.Clamp(styleIndex, 0, Styles.Count - 1)];
        var mesh = style.Mesh is { IsUsable: true } definition ? definition.CreateSampler() : null;
        var pixels = new byte[size * size * 4];

        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                var colour = mesh is null
                    ? style.Sample(GradientProgress(style.Angle, column, row, size, size))
                    : mesh.Sample((column + 0.5) / size, (row + 0.5) / size);

                var offset = ((row * size) + column) * 4;
                pixels[offset] = colour.Blue;
                pixels[offset + 1] = colour.Green;
                pixels[offset + 2] = colour.Red;
                pixels[offset + 3] = byte.MaxValue;
            }
        }

        return (size, size, pixels);
    }

    /// <summary>
    /// How wide the frame is around the capture, in the capture's own pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one place the padding becomes a whole number of pixels. Anything that has to
    /// know where the frame lands asks here rather than rounding again, because a preview
    /// that rounded the other way would be a frame one pixel out from the one the file
    /// gets. It does not depend on the capture's size.
    /// </para>
    /// <para>
    /// <paramref name="scale"/> is how many of those pixels there are to a point. macshot
    /// measures the frame in points against an image that is itself in points, so on a
    /// Retina display a 48-point frame is 96 pixels wide in the file. This port captures
    /// device pixels, so the same 48 has to be multiplied out — left at 1 on a 175%
    /// display it drew a frame little more than half the width of the Mac's, which is also
    /// what stopped the size box fitting inside it.
    /// </para>
    /// </remarks>
    public static int PaddingFor(BeautifyOptions? options = null, double scale = 1) =>
        (int)Math.Round((options ?? BeautifyOptions.Default).Normalized().Padding * Sane(scale));

    /// <summary>
    /// A usable pixels-per-point, so a display that has not reported one yet cannot make
    /// the frame vanish or grow without bound.
    /// </summary>
    private static double Sane(double scale) =>
        double.IsFinite(scale) && scale > 0 ? Math.Clamp(scale, 0.25, 8) : 1;

    /// <summary>
    /// How much taller than the capture the card is: the title bar in window mode, and
    /// nothing in rounded mode.
    /// </summary>
    /// <remarks>
    /// Rounded to whole pixels for <see cref="PaddingFor"/>'s reason — it is an offset into
    /// a pixel buffer, and half a pixel of it would put the capture on a half-pixel row and
    /// soften every line in the screenshot.
    /// </remarks>
    public static int TitleBarFor(BeautifyOptions? options = null, double scale = 1)
    {
        var resolved = (options ?? BeautifyOptions.Default).Normalized();
        return resolved.Mode == BeautifyMode.Window
            ? (int)Math.Round(BeautifyOptions.TitleBarHeight * Sane(scale))
            : 0;
    }

    /// <summary>
    /// Where the frame lands around a region of the capture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The region grows outwards and does not move: the capture stays on the pixels it
    /// was chosen from, which is what lets a preview be drawn beside a selection rather
    /// than through it. Everything that hangs off the selection — the marks on it, the
    /// grips, what a click lands on — is left measuring against the same rectangle it
    /// always did.
    /// </para>
    /// <para>
    /// Evenly, except upwards in window mode, where the title bar goes. It is the only
    /// asymmetry in the whole feature, and the reason this returns a region rather than a
    /// single inset every caller could apply itself.
    /// </para>
    /// </remarks>
    public static CaptureRegion FrameAround(
        CaptureRegion selection,
        BeautifyOptions? options = null,
        double scale = 1)
    {
        var width = (int)selection.Width;
        var height = (int)selection.Height;

        // Nothing to frame. Covers the empty region and the sliver under a pixel across
        // that a drag passes through on its way to being a selection.
        if (width <= 0 || height <= 0)
        {
            return selection;
        }

        var padding = PaddingFor(options, scale);
        var titleBar = TitleBarFor(options, scale);

        return new CaptureRegion(
            selection.X - padding,
            selection.Y - padding - titleBar,
            selection.Width + (padding * 2),
            selection.Height + (padding * 2) + titleBar);
    }

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
        BeautifyOptions? options = null,
        double scale = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (bgraPixels.Length != checked(width * height * 4))
        {
            throw new ArgumentException("The pixel buffer does not match the frame dimensions.", nameof(bgraPixels));
        }

        return Compose(width, height, bgraPixels, options, scale);
    }

    /// <summary>
    /// The frame with nothing in it: the same background and the same shadow
    /// <see cref="Render"/> lays down, with the card's own area left clear.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What the overlay shows while the frame is armed. Laying this over a capture that
    /// is already on screen gives the same picture as framing a copy of it, without
    /// moving the capture — so the selection, the marks on it and what a click lands on
    /// are what they are with the frame off, and the preview is a thing drawn around
    /// them rather than a second coordinate system to keep in step.
    /// </para>
    /// <para>
    /// It goes through the same composer as the export for the reason a swatch does: a
    /// preview drawn by other means is a promise the file may not keep. The padding, the
    /// corner and both shadows are not re-derived here — they are the ones the file will
    /// have, because this is that code with the capture left out.
    /// </para>
    /// <para>
    /// Premultiplied, which is the form a preview surface takes and the form this falls
    /// out in anyway: the colour is the background scaled by how much of it shows.
    /// </para>
    /// </remarks>
    public static (int Width, int Height, byte[] Pixels) Backdrop(
        int width,
        int height,
        BeautifyOptions? options = null,
        double scale = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        return Compose(width, height, default, options, scale);
    }

    /// <summary>
    /// Both of the above. An empty <paramref name="bgraPixels"/> means there is no
    /// capture to put in, which is the preview's case.
    /// </summary>
    private static (int Width, int Height, byte[] Pixels) Compose(
        int width,
        int height,
        ReadOnlySpan<byte> bgraPixels,
        BeautifyOptions? options,
        double scale)
    {
        var framing = bgraPixels.Length > 0;
        var resolved = (options ?? BeautifyOptions.Default).Normalized();
        var style = Styles.Count == 0
            ? new BeautifyStyle("None", 0, new AnnotationColor(0, 0, 0))
            : Styles[resolved.StyleIndex];

        // All three are points, and all three become pixels the same way: a corner drawn
        // at half the padding's scale would not follow the frame it is cut into.
        var pixelsPerPoint = Sane(scale);
        var padding = PaddingFor(resolved, scale);
        var radius = resolved.CornerRadius * pixelsPerPoint;
        var shadow = resolved.ShadowRadius * pixelsPerPoint;

        // The card, which in window mode is taller than the capture by a title bar. Every
        // rounded corner, both shadows and the clip are the card's, so once these two are
        // the card's size the rest of the scan needs no second case for window mode.
        var titleBar = TitleBarFor(resolved, scale);
        var cardWidth = width;
        var cardHeight = height + titleBar;

        var outputWidth = cardWidth + (padding * 2);
        var outputHeight = cardHeight + (padding * 2);
        var output = new byte[checked(outputWidth * outputHeight * 4)];

        // The shadow falls downwards, the way a card lifted off the page would cast
        // one. Tied to its own radius, so softening it also moves it.
        var shadowOffset = shadow * AmbientOffsetRatio;

        // And a second, tighter one right under the edge. One soft shadow alone reads
        // as a card floating some distance above the background; the contact shadow is
        // what puts it down on it. macshot casts both — BeautifyRenderer.swift:538–555 —
        // and the ratios here are its own at its default radius, carried over as
        // fractions so they survive this port measuring everything against the capture's
        // shorter side rather than in points.
        var contactBlur = shadow * ContactBlurRatio;
        var contactOffset = shadowOffset * ContactOffsetRatio;

        // One sampler for the whole scan, which saves an allocation per pixel. It answers
        // from the mesh alone rather than from where the last pixel landed, so a scan
        // that skips ground gets the same colours as one that does not.
        var mesh = style.Mesh is { IsUsable: true } definition ? definition.CreateSampler() : null;

        for (var row = 0; row < outputHeight; row++)
        {
            for (var column = 0; column < outputWidth; column++)
            {
                var offset = ((row * outputWidth) + column) * 4;

                var pixelX = column + 0.5 - padding;
                var pixelY = row + 0.5 - padding;

                // Negative inside the card, positive outside. One pixel of feather
                // across the edge is what turns a stair-stepped corner into a drawn one.
                var distance = RoundedBoxDistance(pixelX, pixelY, cardWidth, cardHeight, radius);
                var coverage = 1 - Smoothstep(-0.5, 0.5, distance);

                // The title bar belongs to the frame rather than to the capture, so it is
                // drawn in both passes: the preview has to show it, or arming the frame
                // would put a window's chrome on the file that the overlay never showed.
                var onTitleBar = titleBar > 0 && pixelY < titleBar;

                // Nothing else of the frame shows through the card, so the preview does not
                // pay for the ground the capture stands on — which is most of the image.
                // The buffer is already zeroed, and zero premultiplied is clear.
                if (!framing && !onTitleBar && coverage >= 1)
                {
                    continue;
                }

                var background = mesh is null
                    ? style.Sample(GradientProgress(style.Angle, column, row, outputWidth, outputHeight))
                    : mesh.Sample(
                        (column + 0.5) / outputWidth,
                        (row + 0.5) / outputHeight);

                var blue = background.Blue;
                var green = background.Green;
                var red = background.Red;

                if (shadow > 0 && resolved.ShadowOpacity > 0)
                {
                    // Cast in the order they sit: the wide ambient one first, the tight
                    // contact one over it, so the darkest part of the result is the few
                    // pixels directly under the edge.
                    var ambient = 1 - Smoothstep(
                        0,
                        shadow,
                        RoundedBoxDistance(pixelX, pixelY - shadowOffset, cardWidth, cardHeight, radius));
                    var contact = 1 - Smoothstep(
                        0,
                        contactBlur,
                        RoundedBoxDistance(pixelX, pixelY - contactOffset, cardWidth, cardHeight, radius));

                    var strength = Over(
                        ambient * resolved.ShadowOpacity,
                        contact * resolved.ShadowOpacity * ContactOpacityRatio);
                    if (strength > 0)
                    {
                        blue = Blend(blue, 0, strength);
                        green = Blend(green, 0, strength);
                        red = Blend(red, 0, strength);
                    }
                }

                var alpha = byte.MaxValue;
                if (coverage > 0)
                {
                    if (onTitleBar)
                    {
                        // Opaque in both passes, and blended with the background only
                        // where the card's own rounded edge feathers through it.
                        var chrome = TitleBarPixel(pixelX, pixelY, titleBar, pixelsPerPoint);
                        blue = Blend(blue, chrome.Blue, coverage);
                        green = Blend(green, chrome.Green, coverage);
                        red = Blend(red, chrome.Red, coverage);
                    }
                    else if (framing)
                    {
                        var sourceX = Math.Clamp((int)pixelX, 0, width - 1);
                        var sourceY = Math.Clamp((int)(pixelY - titleBar), 0, height - 1);
                        var from = ((sourceY * width) + sourceX) * 4;

                        blue = Blend(blue, bgraPixels[from], coverage);
                        green = Blend(green, bgraPixels[from + 1], coverage);
                        red = Blend(red, bgraPixels[from + 2], coverage);
                    }
                    else
                    {
                        // The capture is not here to be blended with — it is underneath,
                        // already on screen. Giving away exactly the coverage it would
                        // have taken lets it show through by the same amount, so the
                        // feathered edge and the rounded corners come out where the file
                        // would have put them.
                        var showing = 1 - coverage;
                        blue = Scale(blue, showing);
                        green = Scale(green, showing);
                        red = Scale(red, showing);
                        alpha = Scale(byte.MaxValue, showing);
                    }
                }

                output[offset] = blue;
                output[offset + 1] = green;
                output[offset + 2] = red;
                output[offset + 3] = alpha;
            }
        }

        return (outputWidth, outputHeight, output);
    }

    /// <summary>
    /// The window chrome at a point on the title bar: the band, and a traffic light where
    /// one falls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Card space, so <paramref name="y"/> runs from the top of the card and the band is
    /// everything above <paramref name="titleBar"/>.
    /// </para>
    /// <para>
    /// Two things macshot draws are deliberately not drawn here, because neither of them
    /// can be seen. The window's own <c>white 0.97</c> fill is covered edge to edge by the
    /// capture (<c>contentRect</c> is exactly the card below the bar), and the separator is
    /// laid at <c>titleBarRect.minY - 0.5</c>, which is inside <c>contentRect</c> and so
    /// goes under the screenshot drawn after it (<c>BeautifyRenderer.swift:791</c> against
    /// <c>:820-821</c>). Reproducing either would put a line on this port that the Mac does
    /// not show — and in the preview, where there is no capture over them, it would show.
    /// </para>
    /// </remarks>
    private static AnnotationColor TitleBarPixel(
        double x, double y, int titleBar, double pixelsPerPoint)
    {
        var radius = TrafficLightRadius * pixelsPerPoint;
        var centreY = titleBar / 2.0;

        var colour = TitleBarFill;

        for (var index = 0; index < TrafficLights.Length; index++)
        {
            var centreX = (TrafficLightInset + (index * TrafficLightSpacing)) * pixelsPerPoint;
            var offsetX = x - centreX;
            var offsetY = y - centreY;
            var away = Math.Sqrt((offsetX * offsetX) + (offsetY * offsetY));

            var inside = 1 - Smoothstep(-0.5, 0.5, away - radius);
            if (inside <= 0)
            {
                continue;
            }

            var (fill, ring) = TrafficLights[index];
            colour = new AnnotationColor(
                Blend(colour.Red, fill.Red, inside),
                Blend(colour.Green, fill.Green, inside),
                Blend(colour.Blue, fill.Blue, inside));

            // macshot strokes a half-point line on a path inset by half a point, so the
            // ring sits just inside the rim rather than on it — which is what keeps a
            // light from reading as a flat disc against a light title bar.
            var ringCentre = radius - (0.5 * pixelsPerPoint);
            var ringHalf = 0.25 * pixelsPerPoint;
            var onRing = (1 - Smoothstep(ringHalf - 0.5, ringHalf + 0.5, Math.Abs(away - ringCentre)))
                * inside;

            if (onRing > 0)
            {
                colour = new AnnotationColor(
                    Blend(colour.Red, ring.Red, onRing),
                    Blend(colour.Green, ring.Green, onRing),
                    Blend(colour.Blue, ring.Blue, onRing));
            }

            // The lights are 12 points across and 20 apart, so no pixel is on two of them.
            break;
        }

        return colour;
    }

    /// <summary>The title bar's band, <c>white 0.94</c> (<c>BeautifyRenderer.swift:786</c>).</summary>
    private static readonly AnnotationColor TitleBarFill = new(240, 240, 240);

    /// <summary>How far in from the card's left edge the first light's centre is, in points.</summary>
    private const double TrafficLightInset = 14;

    /// <summary>Centre to centre, in points.</summary>
    private const double TrafficLightSpacing = 20;

    /// <summary>In points.</summary>
    private const double TrafficLightRadius = 6;

    /// <summary>
    /// Close, minimise, zoom: the fill and the darker ring inside its rim
    /// (<c>BeautifyRenderer.swift:799-806</c>).
    /// </summary>
    private static readonly (AnnotationColor Fill, AnnotationColor Ring)[] TrafficLights =
    [
        (new AnnotationColor(255, 97, 89), new AnnotationColor(217, 64, 56)),
        (new AnnotationColor(255, 191, 64), new AnnotationColor(217, 153, 38)),
        (new AnnotationColor(77, 204, 89), new AnnotationColor(51, 166, 64)),
    ];

    /// <summary>How far the ambient shadow falls, as a fraction of how soft it is.</summary>
    private const double AmbientOffsetRatio = 0.4;

    /// <summary>
    /// The contact shadow against the ambient one. macshot's blur is
    /// <c>min(4 + 0.18r, 16)</c> against an ambient blur of <c>r</c>, its offset
    /// <c>min(2 + 0.12r, 10)</c> against <c>min(4 + 0.35r, 18)</c>, and its alpha
    /// <c>0.20 + 0.30t</c> against <c>0.42 + 0.38t</c> — <c>BeautifyRenderer.swift:493–518</c>.
    /// Taken at its default radius of 20, which is the shape those three curves hold
    /// across the range that matters.
    /// </summary>
    private const double ContactBlurRatio = 0.38;

    private const double ContactOffsetRatio = 0.4;

    private const double ContactOpacityRatio = 0.52;

    private static AnnotationColor Rgb(byte red, byte green, byte blue) => new(red, green, blue);

    /// <summary>
    /// Two coverages stacked. Added rather than composited would let the pair reach
    /// full black where each alone is faint, which is a dark ring rather than a shadow.
    /// </summary>
    private static double Over(double under, double over) =>
        Math.Clamp(under + (over * (1 - under)), 0, 1);

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

    private static byte Scale(byte value, double factor) =>
        (byte)Math.Clamp(Math.Round(value * factor), 0, byte.MaxValue);
}
