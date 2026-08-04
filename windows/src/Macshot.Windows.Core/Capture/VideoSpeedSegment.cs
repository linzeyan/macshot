namespace Macshot.Windows.Core.Capture;

/// <summary>
/// A stretch of a recording that the export plays at something other than 1×.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>VideoSpeedSegment</c>. Like a cut this is time rather than pixels: the
/// segment's source length is unchanged and its <em>output</em> length becomes
/// <c>SourceDuration / Factor</c>, so everything after it moves.
/// </para>
/// <para>
/// A factor of exactly 1 is not allowed — it is a segment that says it does something
/// and does not, and the band offers Delete instead, which is macshot's rule as well.
/// </para>
/// </remarks>
public readonly record struct VideoSpeedSegment(double Start, double End, double Factor)
{
    /// <summary>
    /// The shortest a speed segment may be <em>on the output clock</em>.
    /// </summary>
    /// <remarks>
    /// Measured after the scaling rather than before it, which is macshot's choice and
    /// the only one that works: half a second at 10× is a twentieth of a second of
    /// output, which is one or two frames, and a segment the user cannot see the result
    /// of is one they will drag again wondering why nothing happened.
    /// </remarks>
    public const double MinOutputDuration = 0.1;

    /// <summary>Slow enough for a tutorial; macshot's floor.</summary>
    public const double MinFactor = 0.25;

    /// <summary>
    /// macshot's ceiling. Past it a segment short enough to be worth speeding up
    /// collapses below <see cref="MinOutputDuration"/>, and duplicating frames to fill
    /// the gap helps nobody.
    /// </summary>
    public const double MaxFactor = 10.0;

    /// <summary>What the band places, and what the menu offers first.</summary>
    public const double DefaultFactor = 2.0;

    /// <summary>How much source a newly placed segment covers.</summary>
    public const double DefaultDuration = 2.0;

    /// <summary>The factors the box beside the band offers, in macshot's order.</summary>
    /// <remarks>
    /// 1× is deliberately absent: it is not a speed, it is the absence of one, and the
    /// button next to the box already says Delete.
    /// </remarks>
    public static IReadOnlyList<double> PresetFactors { get; } = [0.25, 0.5, 0.75, 2.0, 3.0, 5.0, 10.0];

    /// <summary>How much source the segment covers.</summary>
    public double SourceDuration => Math.Max(0, End - Start);

    /// <summary>How long that source takes to play once the factor is applied.</summary>
    public double OutputDuration => Factor > 0 ? SourceDuration / Factor : SourceDuration;

    /// <summary>
    /// The shortest source a segment at this factor may cover, so that what it produces
    /// is still <see cref="MinOutputDuration"/> long.
    /// </summary>
    public static double MinSourceDuration(double factor) => MinOutputDuration * ClampFactor(factor);

    public static double ClampFactor(double factor) => Math.Clamp(factor, MinFactor, MaxFactor);

    /// <summary>A segment <paramref name="seconds"/> long centred on <paramref name="at"/>.</summary>
    public static VideoSpeedSegment Placed(
        double at,
        VideoTimeRange gap,
        double factor = DefaultFactor,
        double seconds = DefaultDuration)
    {
        var clamped = ClampFactor(factor);
        var span = VideoSegmentSpan.Placed(at, gap, seconds, MinSourceDuration(clamped));

        return new VideoSpeedSegment(span.Start, span.End, clamped);
    }

    public VideoSpeedSegment WithStart(double start, double totalSeconds) =>
        this with { Start = VideoSegmentSpan.NewStart(start, End, MinSourceDuration(Factor), totalSeconds) };

    public VideoSpeedSegment WithEnd(double end, double totalSeconds) =>
        this with { End = VideoSegmentSpan.NewEnd(end, Start, MinSourceDuration(Factor), totalSeconds) };

    public VideoSpeedSegment MovedTo(double start, double totalSeconds)
    {
        var span = VideoSegmentSpan.Moved(start, SourceDuration, totalSeconds);
        return this with { Start = span.Start, End = span.End };
    }

    /// <summary>
    /// Sets the factor, widening the segment when the new one would leave too little
    /// output to see.
    /// </summary>
    /// <remarks>
    /// The widening is this port's, and it is what stops a silent failure: macshot's
    /// menu can put 10× on a segment whose source is a fifth of a second, and the result
    /// is a two-frame flicker the user reads as the setting not having worked. Growing
    /// the segment instead makes the trade visible on the band.
    /// </remarks>
    public VideoSpeedSegment WithFactor(double factor, double totalSeconds)
    {
        var clamped = ClampFactor(factor);
        var needed = MinSourceDuration(clamped);

        if (SourceDuration >= needed)
        {
            return this with { Factor = clamped };
        }

        var span = VideoSegmentSpan.Placed(
            (Start + End) / 2,
            new VideoTimeRange(0, totalSeconds),
            needed,
            needed);

        return new VideoSpeedSegment(span.Start, span.End, clamped);
    }

    public VideoTimeRange Span => new(Start, End);
}
