using Macshot.Windows.Core.Annotations;

namespace Macshot.Windows.Core.Capture;

/// <summary>What sits behind a caption's glyphs.</summary>
public enum VideoTextBackground
{
    /// <summary>Nothing — the glyphs alone.</summary>
    None,

    Solid,

    /// <summary>A filled rounded rectangle. macshot's default and its word for it.</summary>
    Rounded,
}

/// <summary>Which edge a caption's lines hang from.</summary>
public enum VideoTextAlignment
{
    Left,
    Centre,
    Right,
}

/// <summary>
/// A caption the export draws over a stretch of a recording.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>VideoTextSegment</c>, and deliberately much simpler than the screenshot
/// text tool: one face, a weight, a slant, a colour, an optional fill behind it, an
/// optional rim round the glyphs, an alignment and the two ramps. No per-character
/// formatting, for macshot's stated reason — a label on a video is a consistent string,
/// and rich text on a moving picture reads as a mistake.
/// </para>
/// <para>
/// The colours are <see cref="AnnotationColor"/> rather than macshot's own four doubles.
/// The port already has one colour type that the settings file round-trips and every
/// rasterizer takes; a second one whose only difference is its precision would be a
/// second conversion at every boundary.
/// </para>
/// </remarks>
public readonly record struct VideoTextSegment(
    double Start,
    double End,
    CaptureRegion Rect,
    string Text,
    double FontSize,
    bool Bold,
    bool Italic,
    string FontFamily,
    AnnotationColor TextColor,
    VideoTextBackground Background,
    AnnotationColor BackgroundColor,
    bool OutlineEnabled,
    AnnotationColor OutlineColor,
    double OutlineWidth,
    VideoTextAlignment Alignment,
    double FadeIn,
    double FadeOut)
{
    public const double MinDuration = 0.3;

    public const double DefaultFade = 0.25;

    /// <summary>A caption is read, so it stays up longer than a censor. macshot's three.</summary>
    public const double DefaultDuration = 3.0;

    /// <summary>
    /// The size the glyphs are set at, in points against a 1080-tall frame.
    /// </summary>
    /// <remarks>
    /// macshot's 48, and its reference height. Stated against a fixed height rather than
    /// in pixels so a caption is the same size on screen whether the recording is
    /// exported at 720 or at 4K — the rasterizer scales it by the real frame height.
    /// </remarks>
    public const double DefaultFontSize = 48;

    /// <summary>The ends of the size slider — macshot's 12 and 200.</summary>
    /// <remarks>
    /// A slider and not the four named presets this row used to offer: macshot dropped its
    /// own preset menu for a continuous one, and a caption is one of the few marks where
    /// the exact size matters more than a word for it — it has to sit inside a rectangle
    /// the user drew rather than beside a shape the user placed.
    /// </remarks>
    public const double MinFontSize = 12;

    public const double MaxFontSize = 200;

    /// <summary>
    /// The name that stands for "whatever this machine sets its interface in".
    /// </summary>
    /// <remarks>
    /// macshot's sentinel, and a real family name is stored as itself. A sentinel rather
    /// than an empty string because a caption's family is one of the things the row shows
    /// back to the user, and "System" is what that row has to say.
    /// </remarks>
    public const string SystemFontFamily = "System";

    /// <summary>
    /// The thinnest and thickest rim macshot's slider will set, in the same points against
    /// a 1080-tall frame that <see cref="FontSize"/> is in.
    /// </summary>
    public const double MinOutlineWidth = 0.5;

    public const double MaxOutlineWidth = 8;

    /// <summary>macshot's 2 — a rim that reads over a busy frame without closing up a glyph.</summary>
    public const double DefaultOutlineWidth = 2;

    /// <summary>
    /// Black, which is what a rim is for: white glyphs over a pale screenshot are the case
    /// the outline exists to rescue, and the pill cannot be relied on to be there.
    /// </summary>
    public static AnnotationColor DefaultOutlineColor { get; } = new(0, 0, 0);

    /// <summary>
    /// The smallest a caption rectangle may be, as a fraction of the frame. Larger than
    /// the censor's, because a rectangle this size still has to hold a glyph.
    /// </summary>
    public const double MinRectSize = 0.04;

    /// <summary>macshot's starting rectangle: a wide band across the lower third.</summary>
    public static CaptureRegion DefaultRect { get; } = new(0.1, 0.78, 0.8, 0.14);

    /// <summary>What a newly placed caption says until it is edited. macshot's word.</summary>
    public const string DefaultText = "Text";

    public static AnnotationColor DefaultTextColor { get; } = new(255, 255, 255);

    /// <summary>
    /// macshot's pill: black at seven tenths, which is dark enough to carry white glyphs
    /// over a bright screenshot and light enough not to read as a hole in the picture.
    /// </summary>
    public static AnnotationColor DefaultBackgroundColor { get; } = new(0, 0, 0, 179);

    /// <summary>The colours the caption menu offers, in macshot's order.</summary>
    public static IReadOnlyList<AnnotationColor> PresetColors { get; } =
    [
        new(255, 255, 255),
        new(0, 0, 0),
        new(255, 59, 48),
        new(255, 204, 0),
        new(52, 199, 89),
        new(0, 122, 255),
    ];

    public double Duration => Math.Max(0, End - Start);

    public VideoTimeRange Span => new(Start, End);

    /// <summary>macshot's, shared with the censor and the zoom.</summary>
    public static double AutoFade(double duration) => Math.Min(DefaultFade, Math.Max(0.05, duration * 0.20));

    public double EffectiveFadeIn => ClampFade(FadeIn);

    public double EffectiveFadeOut => ClampFade(FadeOut);

    /// <summary>A caption <paramref name="seconds"/> long centred on <paramref name="at"/>.</summary>
    public static VideoTextSegment Placed(double at, double totalSeconds, double seconds = DefaultDuration)
    {
        var span = VideoSegmentSpan.Placed(at, new VideoTimeRange(0, totalSeconds), seconds, MinDuration);
        var fade = AutoFade(span.Duration);

        return new VideoTextSegment(
            span.Start,
            span.End,
            DefaultRect,
            DefaultText,
            DefaultFontSize,
            Bold: true,
            Italic: false,
            SystemFontFamily,
            DefaultTextColor,
            VideoTextBackground.Rounded,
            DefaultBackgroundColor,
            OutlineEnabled: false,
            DefaultOutlineColor,
            DefaultOutlineWidth,
            VideoTextAlignment.Centre,
            fade,
            fade);
    }

    public VideoTextSegment WithStart(double start, double totalSeconds) =>
        Refaded(this with { Start = VideoSegmentSpan.NewStart(start, End, MinDuration, totalSeconds) });

    public VideoTextSegment WithEnd(double end, double totalSeconds) =>
        Refaded(this with { End = VideoSegmentSpan.NewEnd(end, Start, MinDuration, totalSeconds) });

    public VideoTextSegment MovedTo(double start, double totalSeconds)
    {
        var span = VideoSegmentSpan.Moved(start, Duration, totalSeconds);
        return this with { Start = span.Start, End = span.End };
    }

    public VideoTextSegment WithRect(CaptureRegion rect) => this with { Rect = ClampRect(rect) };

    /// <summary>
    /// This caption dressed the way <paramref name="other"/> is, keeping its own timing,
    /// rectangle and words.
    /// </summary>
    /// <remarks>
    /// The line between the two halves is macshot's, and it is the point of the whole
    /// feature: a caption's appearance is worth carrying to the next one, while its words
    /// and where it sits are what the user just chose. Nothing is clamped on the way
    /// through, because everything here came off a caption that was clamped when it was set.
    /// </remarks>
    public VideoTextSegment StyledLike(VideoTextSegment other) => this with
    {
        FontSize = other.FontSize,
        Bold = other.Bold,
        Italic = other.Italic,
        FontFamily = other.FontFamily,
        TextColor = other.TextColor,
        Background = other.Background,
        BackgroundColor = other.BackgroundColor,
        OutlineEnabled = other.OutlineEnabled,
        OutlineColor = other.OutlineColor,
        OutlineWidth = other.OutlineWidth,
        Alignment = other.Alignment,
    };

    /// <summary>
    /// Sets what the caption says, refusing to leave it empty.
    /// </summary>
    /// <remarks>
    /// An empty caption rasterizes to a bare pill sitting on the picture with nothing in
    /// it, which reads as a rendering fault rather than as a caption the user cleared.
    /// The band's Delete is how a caption is removed.
    /// </remarks>
    public VideoTextSegment WithText(string? text) =>
        this with { Text = string.IsNullOrWhiteSpace(text) ? DefaultText : text };

    /// <summary>Sets the size, holding it to the ends of macshot's slider.</summary>
    /// <remarks>
    /// Clamped here rather than trusted from the row, because a size also arrives from a
    /// caption written by another build, and <see cref="PixelFontSize"/> has no ceiling of
    /// its own — a 4000-point caption would be capped by its own rectangle into a raster
    /// the size of the frame.
    /// </remarks>
    public VideoTextSegment WithFontSize(double points) =>
        this with { FontSize = Math.Clamp(points, MinFontSize, MaxFontSize) };

    /// <summary>Sets the rim's thickness, holding it to the ends of macshot's slider.</summary>
    /// <remarks>
    /// The ceiling is what matters: the rim is drawn as copies of the glyphs offset around
    /// them, so a width past the stem thickness fills the counters in and the caption
    /// becomes a row of blobs rather than becoming more readable.
    /// </remarks>
    public VideoTextSegment WithOutlineWidth(double points) =>
        this with { OutlineWidth = Math.Clamp(points, MinOutlineWidth, MaxOutlineWidth) };

    /// <summary>Whether the caption is set in the interface's own face rather than a named one.</summary>
    /// <remarks>
    /// An unset family counts as the system one too. Nothing in the row can produce it, but
    /// a caption decoded from somewhere else can, and falling back to the interface face is
    /// what macshot does with a family it cannot resolve either.
    /// </remarks>
    public bool UsesSystemFont =>
        string.IsNullOrWhiteSpace(FontFamily)
        || string.Equals(FontFamily, SystemFontFamily, StringComparison.Ordinal);

    /// <summary>How strongly the caption is drawn at <paramref name="seconds"/>.</summary>
    public double OpacityAt(double seconds)
    {
        if (seconds < Start || seconds > End || Duration <= 0)
        {
            return 0;
        }

        var into = seconds - Start;
        var toEnd = End - seconds;
        var fadeIn = EffectiveFadeIn;
        var fadeOut = EffectiveFadeOut;

        var progress = into < fadeIn && fadeIn > 0
            ? into / fadeIn
            : toEnd < fadeOut && fadeOut > 0
                ? toEnd / fadeOut
                : 1;

        return VideoFade.Smoothstep(progress);
    }

    /// <summary>Keeps a rectangle inside the frame and large enough to set type in.</summary>
    public static CaptureRegion ClampRect(CaptureRegion rect)
    {
        var x = Math.Clamp(rect.X, 0, 1 - MinRectSize);
        var y = Math.Clamp(rect.Y, 0, 1 - MinRectSize);
        var width = Math.Clamp(rect.Width, MinRectSize, 1 - x);
        var height = Math.Clamp(rect.Height, MinRectSize, 1 - y);

        if (x + width > 1)
        {
            x = 1 - width;
        }

        if (y + height > 1)
        {
            y = 1 - height;
        }

        return new CaptureRegion(x, y, width, height);
    }

    /// <summary>
    /// The size the glyphs are actually set at, in pixels of a frame
    /// <paramref name="frameHeight"/> tall whose caption rectangle is
    /// <paramref name="rectHeight"/> pixels high.
    /// </summary>
    /// <remarks>
    /// macshot's two rules, both of them. The size is stated against a 1080-tall frame
    /// and scaled by the real one, so a caption keeps its proportion at every export
    /// size; and it is then capped at 78% of its own rectangle, because a caption whose
    /// glyphs are taller than the box they were given is one whose descenders are cut off.
    /// </remarks>
    public static double PixelFontSize(double logicalSize, double frameHeight, double rectHeight)
    {
        const double FitsInRect = 0.78;
        const double Smallest = 8;

        var capped = Math.Min(ScaledToFrame(logicalSize, frameHeight), rectHeight * FitsInRect);

        return Math.Max(Smallest, capped);
    }

    /// <summary>
    /// How thick the rim round the glyphs is drawn, in pixels of a frame
    /// <paramref name="frameHeight"/> tall — zero when the caption has no rim.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scaled by the frame exactly as <see cref="PixelFontSize"/> is, which is the whole
    /// reason the width is stated in points against 1080 rather than in pixels: a rim that
    /// stayed two pixels wide while the glyphs quadrupled would have vanished at 4K.
    /// </para>
    /// <para>
    /// Not capped by the rectangle the way the size is. macshot leaves it uncapped too, and
    /// on purpose — where a rectangle has already shrunk the glyphs, thinning their rim to
    /// match would take away the readability the rim was turned on for.
    /// </para>
    /// <para>
    /// Zero rather than a thin line when the outline is off, so nothing downstream has to
    /// consult the switch as well: the colour and the width are then unreachable, which is
    /// what makes a caption without an outline identical whatever those two happen to hold.
    /// </para>
    /// </remarks>
    public double OutlinePixels(double frameHeight)
    {
        // macshot's floor. Below half a pixel the rim lands on the same pixels as the fill
        // and disappears altogether rather than getting thinner.
        const double Thinnest = 0.5;

        return OutlineEnabled && OutlineWidth > 0
            ? Math.Max(Thinnest, ScaledToFrame(OutlineWidth, frameHeight))
            : 0;
    }

    /// <summary>
    /// A point size against a 1080-tall frame, in pixels of a frame that tall.
    /// </summary>
    /// <remarks>
    /// The one place the reference height is written down. The size and the rim have to
    /// grow together — a rim scaled against a different frame than the glyphs it surrounds
    /// would thicken or thin with the export resolution.
    /// </remarks>
    private static double ScaledToFrame(double points, double frameHeight)
    {
        const double ReferenceHeight = 1080;

        return points * Math.Max(1, frameHeight) / ReferenceHeight;
    }

    private static VideoTextSegment Refaded(VideoTextSegment segment)
    {
        var fade = AutoFade(segment.Duration);
        return segment with { FadeIn = fade, FadeOut = fade };
    }

    private double ClampFade(double fade) => Math.Clamp(fade, 0, Math.Max(0, (Duration / 2) - 0.001));
}
