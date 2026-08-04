namespace Macshot.Windows.Core.Capture;

/// <summary>
/// A stretch of a recording's timeline that the export magnifies.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>VideoZoomSegment</c>, with its arithmetic re-expressed for a different
/// kind of renderer. macOS hands Core Image an affine transform and lets it draw the
/// magnified frame; Windows has no compositor to hand a transform to, so the export
/// crops the source frame and stretches the crop to fill the output. Those are the same
/// operation read from opposite ends, and <see cref="SourceRectAt"/> is the inversion —
/// the rectangle whose contents end up filling the frame.
/// </para>
/// <para>
/// Times are seconds on the source clock, so a segment survives the trim handles moving
/// under it. <see cref="Center"/> is normalised against the source's own pixels, (0,0)
/// at the top-left, which is how macOS stores it and what keeps a segment meaningful
/// when the export is scaled to a different size.
/// </para>
/// <para>
/// A record struct rather than macOS's class: nothing here has identity. macOS gives
/// each segment a UUID because its band holds several of them; this port's band holds
/// one, and a value the editor replaces wholesale is simpler than one it mutates.
/// </para>
/// </remarks>
public readonly record struct VideoZoomSegment(
    double Start,
    double End,
    double Level,
    CapturePoint Center,
    double FadeIn,
    double FadeOut)
{
    /// <summary>
    /// The shortest segment worth having. macshot's own floor: below it the two ramps
    /// meet and the zoom never reaches the level that was asked for.
    /// </summary>
    public const double MinDuration = 0.3;

    /// <summary>How long a ramp lasts on a segment long enough to afford it.</summary>
    public const double DefaultFade = 0.35;

    /// <summary>
    /// Below this a zoom is not visible enough to be worth the re-encode, which is why
    /// macshot's slider starts here rather than at 1.
    /// </summary>
    public const double MinLevel = 1.2;

    public const double MaxLevel = 5.0;

    public const double DefaultLevel = 2.0;

    /// <summary>How long a newly placed segment is, before the user drags it.</summary>
    public const double DefaultDuration = 2.0;

    /// <summary>
    /// Where <see cref="LevelAt"/> stops treating a level as a zoom at all. macOS's
    /// same threshold: a level a ten-thousandth above 1 crops the frame by a fraction
    /// of a pixel, and the rounding that follows would make the output jitter rather
    /// than hold still.
    /// </summary>
    private const double Flat = 1.0001;

    /// <summary>
    /// A segment <paramref name="seconds"/> long centred on <paramref name="at"/>, kept
    /// inside a recording <paramref name="totalSeconds"/> long.
    /// </summary>
    /// <remarks>
    /// macshot's "Add Zoom": two seconds around where the menu was opened, pushed off
    /// whichever end it would otherwise hang over rather than shortened, so a zoom
    /// placed near the start is still the length every other zoom is.
    /// </remarks>
    public static VideoZoomSegment Placed(double at, double totalSeconds, double seconds = DefaultDuration)
    {
        var length = Math.Clamp(seconds, MinDuration, Math.Max(MinDuration, totalSeconds));
        var start = Math.Clamp(at - (length / 2), 0, Math.Max(0, totalSeconds - length));
        var fade = AutoFade(length);

        return new VideoZoomSegment(
            start,
            start + length,
            DefaultLevel,
            new CapturePoint(0.5, 0.5),
            fade,
            fade);
    }

    /// <summary>
    /// The ramp length a segment this long can carry.
    /// </summary>
    /// <remarks>
    /// macshot's rule, and the reason for it: a ramp at each end takes a fifth of the
    /// segment apiece, so two fifths is the most the transitions can ever be and the
    /// middle three fifths always hold at the level that was asked for. A zoom that is
    /// all ramp reads as a wobble rather than as a zoom.
    /// </remarks>
    public static double AutoFade(double duration) => Math.Min(DefaultFade, Math.Max(0.05, duration * 0.20));

    public double Duration => Math.Max(0, End - Start);

    /// <summary>
    /// The ramp actually used at the head of the segment.
    /// </summary>
    /// <remarks>
    /// Never more than half the segment, so the two ramps cannot overlap. Overlapping
    /// ones would have the zoom ramping in and out at the same moment, and the level
    /// reached would depend on which branch was tested first rather than on anything
    /// the user set.
    /// </remarks>
    public double EffectiveFadeIn => ClampFade(FadeIn);

    public double EffectiveFadeOut => ClampFade(FadeOut);

    /// <summary>Whether the segment magnifies at all.</summary>
    public bool IsFlat => Level <= Flat || Duration <= 0;

    /// <summary>Moves the head of the segment, keeping it a segment.</summary>
    public VideoZoomSegment WithStart(double start, double totalSeconds) =>
        this with { Start = Math.Clamp(start, 0, Math.Max(0, Math.Min(End, totalSeconds) - MinDuration)) };

    public VideoZoomSegment WithEnd(double end, double totalSeconds) =>
        this with { End = Math.Clamp(end, Math.Min(Start + MinDuration, totalSeconds), Math.Max(0, totalSeconds)) };

    /// <summary>
    /// Slides the whole segment so it starts at <paramref name="start"/>, keeping its
    /// length. What dragging the body of the pill does, as opposed to an edge.
    /// </summary>
    public VideoZoomSegment MovedTo(double start, double totalSeconds)
    {
        var length = Duration;
        var placed = Math.Clamp(start, 0, Math.Max(0, totalSeconds - length));
        return this with { Start = placed, End = placed + length };
    }

    /// <summary>
    /// Sets the level, and re-scales the ramps to suit the length.
    /// </summary>
    /// <remarks>
    /// The ramps come along because the length may have been dragged since they were
    /// last set: a segment shortened to half a second with macshot's default 0.35 ramps
    /// would be nothing but ramp.
    /// </remarks>
    public VideoZoomSegment WithLevel(double level)
    {
        var fade = AutoFade(Duration);
        return this with { Level = Math.Clamp(level, MinLevel, MaxLevel), FadeIn = fade, FadeOut = fade };
    }

    /// <summary>
    /// How far the frame at <paramref name="seconds"/> is magnified: 1 outside the
    /// segment, the level in its middle, and a smooth ramp between the two.
    /// </summary>
    /// <remarks>
    /// Smoothstep rather than a straight line, which is macshot's curve. A linear ramp
    /// starts and stops at full speed, and on a magnifying frame that reads as the
    /// picture being knocked rather than moved.
    /// </remarks>
    public double LevelAt(double seconds)
    {
        if (seconds < Start || seconds > End || Duration <= 0)
        {
            return 1;
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

        return 1 + ((Level - 1) * Smoothstep(progress));
    }

    /// <summary>
    /// The part of a <paramref name="width"/> by <paramref name="height"/> source frame
    /// that fills the output at <paramref name="seconds"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole frame when nothing is being magnified, so a renderer can use this for
    /// every frame of the export rather than branching on whether a zoom is running.
    /// </para>
    /// <para>
    /// Clamped to the frame, which is macshot's <c>translation(zoom:videoSize:)</c> doing
    /// its work: a zoom centred on a corner would otherwise reach for pixels that are not
    /// there, and the output would carry a black wedge along two edges. The rectangle
    /// slides until it fits instead, so a corner zoom shows the corner.
    /// </para>
    /// <para>
    /// One deliberate difference from macshot, recorded rather than copied. Its
    /// translation is scaled by the zoom on the way out but divided by it on the way in,
    /// so an off-centre point is carried only part of the way to the middle. Both apps
    /// hold <see cref="Center"/> at the middle of the frame — neither has a control that
    /// moves it — so the two agree everywhere either of them is ever asked, and this one
    /// is the answer that would still be right if a control were added.
    /// </para>
    /// </remarks>
    public CaptureRegion SourceRectAt(double seconds, double width, double height)
    {
        var level = LevelAt(seconds);
        if (level <= Flat || width <= 0 || height <= 0)
        {
            return new CaptureRegion(0, 0, width, height);
        }

        var visibleWidth = width / level;
        var visibleHeight = height / level;

        // The chosen point at the middle of the output, then pulled back until the
        // rectangle is inside the frame. Written as a clamp of the corner rather than of
        // macOS's translation because the two are the same arithmetic, and a corner is
        // what the renderer wants.
        var left = (Center.X * width) - (visibleWidth / 2);
        var top = (Center.Y * height) - (visibleHeight / 2);

        return new CaptureRegion(
            Math.Clamp(left, 0, width - visibleWidth),
            Math.Clamp(top, 0, height - visibleHeight),
            visibleWidth,
            visibleHeight);
    }

    /// <summary>Whether <paramref name="seconds"/> is inside the segment.</summary>
    public bool Covers(double seconds) => seconds >= Start && seconds <= End && Duration > 0;

    /// <summary>
    /// Cubic ease over [0, 1]. macshot's <c>easeInOut</c>: zero slope at both ends, which
    /// is what makes the zoom start and stop without a visible kick.
    /// </summary>
    private static double Smoothstep(double progress)
    {
        var clamped = Math.Clamp(progress, 0, 1);
        return clamped * clamped * (3 - (2 * clamped));
    }

    /// <remarks>
    /// A thousandth short of half, so a segment always has at least one frame at the
    /// level asked for. Exactly half would leave the plateau a single instant wide, and
    /// which frame landed on it would depend on the frame rate.
    /// </remarks>
    private double ClampFade(double fade) => Math.Clamp(fade, 0, Math.Max(0, (Duration / 2) - 0.001));
}
