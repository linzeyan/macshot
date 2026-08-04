namespace Macshot.Windows.Core.Capture;

/// <summary>How a censor hides what is under it.</summary>
public enum VideoCensorStyle
{
    Solid,
    Pixelate,
    Blur,
}

/// <summary>
/// A rectangle of a recording that the export hides for a stretch of its length.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>VideoCensorSegment</c>. <see cref="Rect"/> is normalised against the
/// source's own frame, (0,0) at the top-left, which is what keeps a censor over the
/// thing it was drawn over when the export is scaled to a different size.
/// </para>
/// <para>
/// The strengths are fixed rather than offered as sliders, which is macshot's decision
/// and its wording: a redaction that can be turned down is one somebody will turn down
/// too far.
/// </para>
/// </remarks>
public readonly record struct VideoCensorSegment(
    double Start,
    double End,
    CaptureRegion Rect,
    VideoCensorStyle Style,
    double FadeIn,
    double FadeOut)
{
    public const double MinDuration = 0.3;

    /// <summary>macshot's ramp, shorter than the zoom's.</summary>
    public const double DefaultFade = 0.25;

    /// <summary>How much of the recording a newly placed censor covers.</summary>
    public const double DefaultDuration = 2.0;

    /// <summary>
    /// The block a pixelated censor is reduced to, in pixels <em>of the export</em>.
    /// </summary>
    /// <remarks>
    /// macshot's 20, and measured where macshot measures it — after the frame has been
    /// scaled to the output size, so a censor stays equally unreadable whether the
    /// recording is exported at full size or at a quarter of it.
    /// </remarks>
    public const double PixelateBlockSize = 20;

    /// <summary>
    /// The blur's radius, in pixels of the export.
    /// </summary>
    /// <remarks>
    /// macshot's 30, and its note on why not less: below about 25 a blurred region still
    /// shows faint shapes and edges, which is worse than no redaction because it looks
    /// like one.
    /// </remarks>
    public const double BlurRadius = 30;

    /// <summary>The smallest a censor rectangle may be, as a fraction of the frame.</summary>
    /// <remarks>
    /// macshot's floor. A zero-area rectangle would hide nothing and leave a handle
    /// nobody can take hold of to make it bigger again.
    /// </remarks>
    public const double MinRectSize = 0.02;

    /// <summary>macshot's starting rectangle: a third of the frame, in the middle of it.</summary>
    public static CaptureRegion DefaultRect { get; } = new(0.35, 0.35, 0.3, 0.3);

    public double Duration => Math.Max(0, End - Start);

    public VideoTimeRange Span => new(Start, End);

    /// <summary>The ramp a segment this long can carry. macshot's, shared with the zoom.</summary>
    public static double AutoFade(double duration) => Math.Min(DefaultFade, Math.Max(0.05, duration * 0.20));

    /// <remarks>
    /// Never more than half the segment, so a plateau frame always survives between the
    /// two ramps; without it the strength reached would depend on which ramp was tested
    /// first rather than on anything the user set.
    /// </remarks>
    public double EffectiveFadeIn => ClampFade(FadeIn);

    public double EffectiveFadeOut => ClampFade(FadeOut);

    /// <summary>A censor <paramref name="seconds"/> long centred on <paramref name="at"/>.</summary>
    /// <remarks>
    /// Censors may sit on top of one another, unlike zooms and speeds: two rectangles
    /// hiding two different things at the same moment is an ordinary thing to want, and
    /// the renderer applies them in order rather than choosing between them.
    /// </remarks>
    public static VideoCensorSegment Placed(
        double at,
        double totalSeconds,
        VideoCensorStyle style = VideoCensorStyle.Blur,
        double seconds = DefaultDuration)
    {
        var span = VideoSegmentSpan.Placed(at, new VideoTimeRange(0, totalSeconds), seconds, MinDuration);
        var fade = AutoFade(span.Duration);

        return new VideoCensorSegment(span.Start, span.End, DefaultRect, style, fade, fade);
    }

    public VideoCensorSegment WithStart(double start, double totalSeconds) =>
        Refaded(this with { Start = VideoSegmentSpan.NewStart(start, End, MinDuration, totalSeconds) });

    public VideoCensorSegment WithEnd(double end, double totalSeconds) =>
        Refaded(this with { End = VideoSegmentSpan.NewEnd(end, Start, MinDuration, totalSeconds) });

    public VideoCensorSegment MovedTo(double start, double totalSeconds)
    {
        var span = VideoSegmentSpan.Moved(start, Duration, totalSeconds);
        return this with { Start = span.Start, End = span.End };
    }

    public VideoCensorSegment WithStyle(VideoCensorStyle style) => this with { Style = style };

    public VideoCensorSegment WithRect(CaptureRegion rect) => this with { Rect = ClampRect(rect) };

    /// <summary>
    /// How strongly the censor is applied at <paramref name="seconds"/>: nothing outside
    /// the segment, all of it in the middle, and an eased ramp between.
    /// </summary>
    /// <remarks>
    /// Ramped rather than switched on, which is macshot's choice: a redaction that
    /// appears between one frame and the next reads as a glitch in the recording, and
    /// the eye goes to it rather than past it.
    /// </remarks>
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

    /// <summary>Keeps a rectangle inside the frame and big enough to take hold of.</summary>
    public static CaptureRegion ClampRect(CaptureRegion rect)
    {
        var x = Math.Clamp(rect.X, 0, 1 - MinRectSize);
        var y = Math.Clamp(rect.Y, 0, 1 - MinRectSize);
        var width = Math.Clamp(rect.Width, MinRectSize, 1 - x);
        var height = Math.Clamp(rect.Height, MinRectSize, 1 - y);

        // Re-checked after the sizes were clamped, because widening a rectangle at the
        // right edge is what pushes its left edge out of the frame.
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

    /// <remarks>
    /// The ramps are rescaled whenever the length changes, because the drag may have
    /// left the segment shorter than two of them: a censor dragged to half a second with
    /// the default 0.25 ramps would be nothing but ramp and never fully hide anything.
    /// </remarks>
    private static VideoCensorSegment Refaded(VideoCensorSegment segment)
    {
        var fade = AutoFade(segment.Duration);
        return segment with { FadeIn = fade, FadeOut = fade };
    }

    private double ClampFade(double fade) => Math.Clamp(fade, 0, Math.Max(0, (Duration / 2) - 0.001));
}

/// <summary>The one ramp curve every fading effect uses.</summary>
/// <remarks>
/// macshot writes the same <c>easeInOut</c> out in <c>VideoZoomSegment</c>,
/// <c>VideoCensorSegment</c>, <c>VideoTextSegment</c> and again inside the compositor's
/// text snapshot. It is one curve, and its zero slope at both ends is what makes an
/// effect arrive without a visible kick.
/// </remarks>
public static class VideoFade
{
    public static double Smoothstep(double progress)
    {
        var clamped = Math.Clamp(progress, 0, 1);
        return clamped * clamped * (3 - (2 * clamped));
    }
}
