namespace Macshot.Windows.Core.Capture;

/// <summary>
/// A stretch of a recording that the export leaves out.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>VideoCutSegment</c>. Alone among the six effects a cut is temporal
/// rather than pixel work: the frames it covers never reach the output at all, so
/// everything after it moves earlier. That is why <see cref="VideoCuts.KeptRanges"/>
/// exists — the rest of the pipeline is written against what survives, not against
/// what was removed.
/// </para>
/// <para>
/// Times are seconds on the source clock, as every segment's are, so a cut survives
/// the trim handles moving under it.
/// </para>
/// </remarks>
public readonly record struct VideoCutSegment(double Start, double End)
{
    /// <summary>
    /// macshot's floor. A cut shorter than this removes a frame or two and reads as a
    /// stutter rather than as an edit, and dragging one to nothing would leave a pill
    /// on the band with no way to take hold of it again.
    /// </summary>
    public const double MinDuration = 0.1;

    /// <summary>How long a newly placed cut is, before the user drags it.</summary>
    /// <remarks>
    /// A second, against the zoom's two. macshot's numbers, and the reason is that a
    /// cut is destructive: the shorter default asks the user to extend it deliberately
    /// rather than to notice that a second more than they meant has gone.
    /// </remarks>
    public const double DefaultDuration = 1.0;

    public double Duration => Math.Max(0, End - Start);

    /// <summary>A cut <paramref name="seconds"/> long centred on <paramref name="at"/>.</summary>
    public static VideoCutSegment Placed(double at, double totalSeconds, double seconds = DefaultDuration)
    {
        var span = VideoSegmentSpan.Placed(at, new VideoTimeRange(0, totalSeconds), seconds, MinDuration);
        return new VideoCutSegment(span.Start, span.End);
    }

    public VideoCutSegment WithStart(double start, double totalSeconds) =>
        this with { Start = VideoSegmentSpan.NewStart(start, End, MinDuration, totalSeconds) };

    public VideoCutSegment WithEnd(double end, double totalSeconds) =>
        this with { End = VideoSegmentSpan.NewEnd(end, Start, MinDuration, totalSeconds) };

    public VideoCutSegment MovedTo(double start, double totalSeconds)
    {
        var span = VideoSegmentSpan.Moved(start, Duration, totalSeconds);
        return new VideoCutSegment(span.Start, span.End);
    }

    public VideoTimeRange Span => new(Start, End);
}

/// <summary>
/// What survives a trim and a set of cuts.
/// </summary>
/// <remarks>
/// macshot's <c>VideoCuts</c>, and the reason it is a free function rather than a
/// method on the segment: the answer depends on every cut at once. Overlapping cuts
/// are one cut, and a cut half outside the trim removes only the half inside it.
/// </remarks>
public static class VideoCuts
{
    /// <summary>
    /// How near two boundaries have to be before they are treated as the same one.
    /// </summary>
    /// <remarks>
    /// macshot's own thousandth. Cuts are dragged in pixels on a band a few hundred
    /// wide, so two that were meant to meet land a ten-thousandth apart, and without
    /// the slack the export would insert a kept range one frame long between them —
    /// which shows as a single stray frame of the material that was supposed to go.
    /// </remarks>
    private const double Slack = 0.001;

    /// <summary>
    /// The stretches of source between <paramref name="trimStart"/> and
    /// <paramref name="trimEnd"/> that no cut removes, in order and never overlapping.
    /// </summary>
    /// <remarks>
    /// Cuts outside the trim are ignored and cuts straddling an edge are clipped to it,
    /// so dragging a trim handle over a cut does what it looks like it does rather than
    /// producing a range that starts before the recording is meant to.
    /// </remarks>
    public static IReadOnlyList<VideoTimeRange> KeptRanges(
        double trimStart,
        double trimEnd,
        IEnumerable<VideoCutSegment> cuts)
    {
        ArgumentNullException.ThrowIfNull(cuts);

        if (trimEnd <= trimStart)
        {
            return [];
        }

        var clipped = cuts
            .Where(cut => cut.End > cut.Start)
            .Select(cut => new VideoTimeRange(
                Math.Max(trimStart, cut.Start),
                Math.Min(trimEnd, cut.End)))
            .Where(range => range.Start < range.End)
            .OrderBy(range => range.Start)
            .ToList();

        var merged = new List<VideoTimeRange>(clipped.Count);
        foreach (var range in clipped)
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End + Slack)
            {
                merged[^1] = merged[^1] with { End = Math.Max(merged[^1].End, range.End) };
            }
            else
            {
                merged.Add(range);
            }
        }

        var kept = new List<VideoTimeRange>(merged.Count + 1);
        var cursor = trimStart;

        foreach (var cut in merged)
        {
            if (cut.Start > cursor + Slack)
            {
                kept.Add(new VideoTimeRange(cursor, cut.Start));
            }

            cursor = Math.Max(cursor, cut.End);
        }

        if (cursor < trimEnd - Slack)
        {
            kept.Add(new VideoTimeRange(cursor, trimEnd));
        }

        return kept;
    }

    /// <summary>How long the output runs once the cuts are gone.</summary>
    public static double TotalSeconds(IEnumerable<VideoTimeRange> keptRanges)
    {
        ArgumentNullException.ThrowIfNull(keptRanges);

        return keptRanges.Sum(range => range.Duration);
    }
}
