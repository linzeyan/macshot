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
    public static VideoZoomSegment Placed(double at, double totalSeconds, double seconds = DefaultDuration) =>
        Placed(at, new VideoTimeRange(0, totalSeconds), seconds);

    /// <summary>
    /// The same, confined to <paramref name="gap"/> — the stretch of band no other zoom
    /// occupies. Two zooms running at once would magnify by whichever the renderer
    /// happened to test first, so the band refuses to place one over another.
    /// </summary>
    public static VideoZoomSegment Placed(double at, VideoTimeRange gap, double seconds = DefaultDuration)
    {
        var span = VideoSegmentSpan.Placed(at, gap, seconds, MinDuration);
        var fade = AutoFade(span.Duration);

        return new VideoZoomSegment(
            span.Start,
            span.End,
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
        this with { Start = VideoSegmentSpan.NewStart(start, End, MinDuration, totalSeconds) };

    public VideoZoomSegment WithEnd(double end, double totalSeconds) =>
        this with { End = VideoSegmentSpan.NewEnd(end, Start, MinDuration, totalSeconds) };

    /// <summary>
    /// Slides the whole segment so it starts at <paramref name="start"/>, keeping its
    /// length. What dragging the body of the pill does, as opposed to an edge.
    /// </summary>
    public VideoZoomSegment MovedTo(double start, double totalSeconds)
    {
        var span = VideoSegmentSpan.Moved(start, Duration, totalSeconds);
        return this with { Start = span.Start, End = span.End };
    }

    /// <summary>Where the segment sits, for the band's row packing and overlap checks.</summary>
    public VideoTimeRange Span => new(Start, End);

    /// <summary>
    /// The part of the frame this zoom shows once it has reached its full level, as
    /// fractions of the frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// macshot's <c>rectForZoom</c>: what the editor draws over the picture, and what a
    /// drag on it edits. Square in these units, because a zoom takes the same fraction off
    /// both axes — on a 16:9 frame that square is a 16:9 rectangle, which is the aspect
    /// lock the editor's one corner handle relies on.
    /// </para>
    /// <para>
    /// Equal to <see cref="SourceRectAt"/> at the plateau, divided by the frame size, and
    /// that equality is the point: the rectangle the user drew is the rectangle the export
    /// crops. It holds because both go through the same clamp — this one through
    /// <see cref="ClampedCenter"/>, the other through the clamp on its corner.
    /// </para>
    /// </remarks>
    public CaptureRegion Window
    {
        get
        {
            var side = 1 / Math.Max(Level, Flat);
            var centre = ClampedCenter(Center, Level);

            return new CaptureRegion(centre.X - (side / 2), centre.Y - (side / 2), side, side);
        }
    }

    /// <summary>
    /// A normalised centre pulled far enough in that the zoom window it names is wholly
    /// inside the frame.
    /// </summary>
    /// <remarks>
    /// macshot's <c>clampedCenter</c>, and its reason verbatim: a centre inside this range
    /// is one <see cref="SourceRectAt"/> never has to slide, so the region the user drew
    /// and the region actually shown stay identical. Without it, a zoom placed against an
    /// edge magnifies somewhere other than where its rectangle was drawn.
    /// </remarks>
    public static CapturePoint ClampedCenter(CapturePoint centre, double level)
    {
        var half = 1 / (2 * Math.Max(level, Flat));

        return new CapturePoint(Math.Clamp(centre.X, half, 1 - half), Math.Clamp(centre.Y, half, 1 - half));
    }

    /// <summary>
    /// The level and centre a rectangle dragged on the preview means.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="Window"/>: a window one fraction <c>f</c> of the frame
    /// across is level <c>1/f</c>, and its centre is the rectangle's own. The longer side
    /// decides, which is macshot's choice — neither editor can hand this a rectangle that
    /// is not square, but one that did arrive would be contained by the window rather than
    /// cropped by it.
    /// </remarks>
    public VideoZoomSegment WithWindow(CaptureRegion window)
    {
        var side = Math.Max(Math.Max(window.Width, window.Height), 1e-4);
        var level = Math.Clamp(1 / side, MinLevel, MaxLevel);

        return this with
        {
            Level = level,
            Center = ClampedCenter(
                new CapturePoint(window.X + (window.Width / 2), window.Y + (window.Height / 2)),
                level),
        };
    }

    /// <summary>
    /// <paramref name="window"/> with its bottom-right corner dragged to
    /// <paramref name="corner"/>, in fractions of a <paramref name="width"/> by
    /// <paramref name="height"/> frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// macshot's <c>zoomResizedRect</c>, reduced to the one handle this port draws. A zoom
    /// window is aspect-locked to the video — one fraction of the frame in both axes — so
    /// the drag names a single number rather than a width and a height, and that is why one
    /// corner handle is as expressive here as macshot's eight.
    /// </para>
    /// <para>
    /// The fraction is the pointer projected onto the aspect-locked diagonal, weighted by
    /// each axis in frame pixels: dragging sideways on a 16:9 frame moves the corner mostly
    /// along x, so x is what the drag should mostly follow. Then the level range decides how
    /// far it may go, the opposite corner stays where it was, and the result slides inside
    /// the frame — a window that hung over an edge would show pixels the recording has not
    /// got.
    /// </para>
    /// </remarks>
    public static CaptureRegion ResizedWindow(CaptureRegion window, CapturePoint corner, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return window;
        }

        var across = corner.X - window.X;
        var down = corner.Y - window.Y;
        var acrossWeight = width * width;
        var downWeight = height * height;

        var side = Math.Clamp(
            ((across * acrossWeight) + (down * downWeight)) / (acrossWeight + downWeight),
            1 / MaxLevel,
            1 / MinLevel);

        return new CaptureRegion(
            Math.Clamp(window.X, 0, 1 - side),
            Math.Clamp(window.Y, 0, 1 - side),
            side,
            side);
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

        return 1 + ((Level - 1) * VideoFade.Smoothstep(progress));
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
    /// <see cref="Center"/> is the user's: both apps let the rectangle be dragged on the
    /// editor's preview, and both store only centres <see cref="ClampedCenter"/> has
    /// already pulled in. That range is chosen to be the one where this clamp does
    /// nothing, which is what makes the rectangle drawn on the preview and the rectangle
    /// cropped here the same rectangle — macshot arrives at the same guarantee from the
    /// other end, by clamping a translation, and its own note for
    /// <c>clampedCenter</c> says so.
    /// </para>
    /// <para>
    /// The clamp is still needed for the ramp. A frame partway into one is rendered below
    /// the plateau's level, so its window is wider than the one the centre was clamped
    /// for, and near an edge it does have to slide — which is what closes the zoom in on
    /// the chosen region rather than letting a corner of the picture fall off the frame.
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

    /// <remarks>
    /// A thousandth short of half, so a segment always has at least one frame at the
    /// level asked for. Exactly half would leave the plateau a single instant wide, and
    /// which frame landed on it would depend on the frame rate.
    /// </remarks>
    private double ClampFade(double fade) => Math.Clamp(fade, 0, Math.Max(0, (Duration / 2) - 0.001));
}
