namespace Macshot.Windows.Core.Capture;

/// <summary>A stretch of a recording's clock, in seconds.</summary>
public readonly record struct VideoTimeRange(double Start, double End)
{
    public double Duration => Math.Max(0, End - Start);

    /// <summary>Whether <paramref name="seconds"/> falls inside, ends included.</summary>
    public bool Covers(double seconds) => seconds >= Start && seconds <= End && Duration > 0;
}

/// <summary>
/// What dragging an edge or the body of a pill does to the segment underneath it.
/// </summary>
/// <remarks>
/// <para>
/// One implementation for all six effects rather than one per type. macshot writes the
/// same three clamps out again in <c>dragZoomSegment</c>, <c>dragCutSegment</c>,
/// <c>dragSpeedSegment</c>, <c>dragCensorSegment</c> and <c>dragTextSegment</c>; the
/// arithmetic is identical in every one of them and only the minimum length differs, so
/// here it is a parameter.
/// </para>
/// <para>
/// The rules the clamps encode: an edge may never pass its opposite number closer than
/// the type's minimum, neither edge may leave the recording, and the body carries its
/// length with it rather than being squashed against an end.
/// </para>
/// </remarks>
public static class VideoSegmentSpan
{
    /// <summary>Where the head lands when it is dragged to <paramref name="wanted"/>.</summary>
    public static double NewStart(double wanted, double end, double minDuration, double totalSeconds) =>
        Math.Clamp(wanted, 0, Math.Max(0, Math.Min(end, totalSeconds) - minDuration));

    /// <summary>Where the tail lands when it is dragged to <paramref name="wanted"/>.</summary>
    public static double NewEnd(double wanted, double start, double minDuration, double totalSeconds) =>
        Math.Clamp(wanted, Math.Min(start + minDuration, totalSeconds), Math.Max(0, totalSeconds));

    /// <summary>
    /// Where a segment <paramref name="duration"/> long lands when its body is dragged so
    /// that it would start at <paramref name="wanted"/>.
    /// </summary>
    public static VideoTimeRange Moved(double wanted, double duration, double totalSeconds)
    {
        var placed = Math.Clamp(wanted, 0, Math.Max(0, totalSeconds - duration));
        return new VideoTimeRange(placed, placed + duration);
    }

    /// <summary>
    /// A segment <paramref name="seconds"/> long centred on <paramref name="at"/> and kept
    /// inside <paramref name="gap"/>.
    /// </summary>
    /// <remarks>
    /// macshot's shape for every "Add …" on the band: the new segment is its full length
    /// and is pushed off whichever end it would hang over rather than shortened, so one
    /// placed near a boundary is still the length every other one is.
    /// </remarks>
    public static VideoTimeRange Placed(double at, VideoTimeRange gap, double seconds, double minDuration)
    {
        var room = Math.Max(0, gap.Duration);
        var length = Math.Clamp(seconds, Math.Min(minDuration, room), Math.Max(minDuration, room));
        var start = Math.Clamp(at - (length / 2), gap.Start, Math.Max(gap.Start, gap.End - length));

        return new VideoTimeRange(start, start + length);
    }

    /// <summary>
    /// The stretch around <paramref name="at"/> that no segment in <paramref name="taken"/>
    /// occupies, or nothing when <paramref name="at"/> is already inside one.
    /// </summary>
    /// <remarks>
    /// macshot's <c>zoomGapAtClickTime</c> and <c>speedGapAtClickTime</c>, which are the
    /// same function written twice. Zoom and speed refuse to overlap their own kind — two
    /// zooms running at once would magnify by whichever the renderer tested first, and two
    /// speeds would each claim to be re-timing the same stretch of source.
    /// </remarks>
    public static VideoTimeRange? GapAround(
        double at,
        double totalSeconds,
        IEnumerable<VideoTimeRange> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);

        if (totalSeconds <= 0)
        {
            return null;
        }

        var ordered = taken.Where(range => range.Duration > 0).OrderBy(range => range.Start).ToList();
        var gapStart = 0.0;

        foreach (var range in ordered)
        {
            if (at >= range.Start && at <= range.End)
            {
                return null;
            }

            if (range.End <= at)
            {
                gapStart = Math.Max(gapStart, range.End);
            }
            else
            {
                return new VideoTimeRange(gapStart, range.Start);
            }
        }

        return new VideoTimeRange(gapStart, totalSeconds);
    }
}
