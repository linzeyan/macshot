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
/// alignment and the two ramps. No per-character formatting, for macshot's stated
/// reason — a label on a video is a consistent string, and rich text on a moving picture
/// reads as a mistake.
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
    AnnotationColor TextColor,
    VideoTextBackground Background,
    AnnotationColor BackgroundColor,
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

    /// <summary>The sizes the box beside the band offers.</summary>
    public static IReadOnlyList<double> PresetFontSizes { get; } = [24, 32, 48, 64, 96];

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
            DefaultTextColor,
            VideoTextBackground.Rounded,
            DefaultBackgroundColor,
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
    /// Sets what the caption says, refusing to leave it empty.
    /// </summary>
    /// <remarks>
    /// An empty caption rasterizes to a bare pill sitting on the picture with nothing in
    /// it, which reads as a rendering fault rather than as a caption the user cleared.
    /// The band's Delete is how a caption is removed.
    /// </remarks>
    public VideoTextSegment WithText(string? text) =>
        this with { Text = string.IsNullOrWhiteSpace(text) ? DefaultText : text };

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
        const double ReferenceHeight = 1080;
        const double FitsInRect = 0.78;
        const double Smallest = 8;

        var scaled = logicalSize * Math.Max(1, frameHeight) / ReferenceHeight;
        var capped = Math.Min(scaled, rectHeight * FitsInRect);

        return Math.Max(Smallest, capped);
    }

    private static VideoTextSegment Refaded(VideoTextSegment segment)
    {
        var fade = AutoFade(segment.Duration);
        return segment with { FadeIn = fade, FadeOut = fade };
    }

    private double ClampFade(double fade) => Math.Clamp(fade, 0, Math.Max(0, (Duration / 2) - 0.001));
}
