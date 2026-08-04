namespace Macshot.Windows.Core.Capture;

/// <summary>Which of the six the band is about to place, or which a pill is.</summary>
public enum VideoEffectKind
{
    Zoom,
    Censor,
    Cut,
    Speed,
    Freeze,
    Text,
}

/// <summary>
/// Everything the effects band is holding, and what the export has to do about it.
/// </summary>
/// <remarks>
/// <para>
/// macshot keeps six arrays on <c>EffectsBandView</c> and asks the band for each of them
/// by name at export time. This is the same six, gathered so that the window holds one
/// field, the export takes one argument, and the questions that decide which export path
/// runs can be asked — and tested — without a window.
/// </para>
/// <para>
/// A mutable class rather than a record, because the band adds to it, drags things in it
/// and deletes from it. The segments inside are values: a pill the editor replaces
/// wholesale is simpler than one it mutates, and there is nothing in a segment that has
/// identity beyond where it sits in its list.
/// </para>
/// </remarks>
public sealed class VideoEffects
{
    public List<VideoZoomSegment> Zooms { get; } = [];

    public List<VideoCensorSegment> Censors { get; } = [];

    public List<VideoCutSegment> Cuts { get; } = [];

    public List<VideoSpeedSegment> Speeds { get; } = [];

    public List<VideoFreezeSegment> Freezes { get; } = [];

    public List<VideoTextSegment> Texts { get; } = [];

    /// <summary>Whether the band is holding nothing at all.</summary>
    public bool IsEmpty =>
        Zooms.Count == 0
        && Censors.Count == 0
        && Cuts.Count == 0
        && Speeds.Count == 0
        && Freezes.Count == 0
        && Texts.Count == 0;

    public int Count =>
        Zooms.Count + Censors.Count + Cuts.Count + Speeds.Count + Freezes.Count + Texts.Count;

    /// <summary>
    /// Whether anything here has to be drawn onto the frames themselves.
    /// </summary>
    /// <remarks>
    /// A zoom that never magnifies and a censor the user faded to nothing are not pixel
    /// work, so they do not count: the difference between this being true and false is
    /// the difference between an export that decodes and re-encodes every frame by hand
    /// and one the platform does in a single call, and nobody should pay the first for a
    /// segment that does nothing.
    /// </remarks>
    public bool NeedsPixelWork =>
        Zooms.Any(zoom => !zoom.IsFlat)
        || Censors.Any(censor => censor.Duration > 0)
        || Texts.Any(text => text.Duration > 0);

    /// <summary>
    /// Whether anything here changes which source instant an output instant shows in a
    /// way <see cref="System.Collections.Generic.List{T}"/> of clips cannot express.
    /// </summary>
    /// <remarks>
    /// Speeds and freezes only. A cut is temporal too, but Windows can express a cut as
    /// several clips in one composition, which keeps the platform's own encoder — and
    /// the recording's audio — on the cheap path. Nothing in Windows re-times a track,
    /// so a speed or a freeze forces the hand-built pipeline whether or not there is a
    /// pixel effect to justify it.
    /// </remarks>
    public bool NeedsRetiming =>
        Speeds.Any(speed => speed.SourceDuration > 0 && Math.Abs(speed.Factor - 1) > 0.0001)
        || Freezes.Any(freeze => freeze.Hold > 0);

    public bool HasCuts => Cuts.Any(cut => cut.Duration > 0);

    /// <summary>Whether the export has to go frame by frame rather than through the platform.</summary>
    public bool NeedsFramePipeline => NeedsPixelWork || NeedsRetiming;

    /// <summary>Whether anything here would change the file at all.</summary>
    public bool ChangesAnything => NeedsFramePipeline || HasCuts;

    /// <summary>
    /// Whether the export will drop the recording's audio anywhere.
    /// </summary>
    /// <remarks>
    /// Only a speed segment does, and the editor says so before writing rather than
    /// leaving it to be discovered on playback. See <see cref="VideoTimeline.AudioRuns"/>
    /// for why Windows cannot do what macOS does here.
    /// </remarks>
    public bool SilencesAnything =>
        Speeds.Any(speed => speed.SourceDuration > 0 && Math.Abs(speed.Factor - 1) > 0.0001);

    /// <summary>How many segments of <paramref name="kind"/> are on the band.</summary>
    public int CountOf(VideoEffectKind kind) => kind switch
    {
        VideoEffectKind.Zoom => Zooms.Count,
        VideoEffectKind.Censor => Censors.Count,
        VideoEffectKind.Cut => Cuts.Count,
        VideoEffectKind.Speed => Speeds.Count,
        VideoEffectKind.Freeze => Freezes.Count,
        _ => Texts.Count,
    };

    /// <summary>Takes the segment at <paramref name="index"/> off the band.</summary>
    public void Remove(VideoEffectKind kind, int index)
    {
        if (index < 0 || index >= CountOf(kind))
        {
            return;
        }

        switch (kind)
        {
            case VideoEffectKind.Zoom:
                Zooms.RemoveAt(index);
                break;
            case VideoEffectKind.Censor:
                Censors.RemoveAt(index);
                break;
            case VideoEffectKind.Cut:
                Cuts.RemoveAt(index);
                break;
            case VideoEffectKind.Speed:
                Speeds.RemoveAt(index);
                break;
            case VideoEffectKind.Freeze:
                Freezes.RemoveAt(index);
                break;
            default:
                Texts.RemoveAt(index);
                break;
        }
    }

    /// <summary>Where the segment at <paramref name="index"/> sits on the source clock.</summary>
    /// <remarks>
    /// A freeze has no width, so it answers with the instant twice. The band gives it a
    /// pill of its own fixed width instead, because a rectangle with no width cannot be
    /// clicked on.
    /// </remarks>
    public VideoTimeRange SpanOf(VideoEffectKind kind, int index) => kind switch
    {
        VideoEffectKind.Zoom => Zooms[index].Span,
        VideoEffectKind.Censor => Censors[index].Span,
        VideoEffectKind.Cut => Cuts[index].Span,
        VideoEffectKind.Speed => Speeds[index].Span,
        VideoEffectKind.Freeze => new VideoTimeRange(Freezes[index].At, Freezes[index].At),
        _ => Texts[index].Span,
    };

    /// <summary>
    /// The pieces the export is made of, given where the trim handles are.
    /// </summary>
    /// <remarks>
    /// The one place cuts, speeds and freezes are read together, so that no caller has to
    /// remember the order the three compose in — cuts decide what survives, and the other
    /// two re-time what is left.
    /// </remarks>
    public IReadOnlyList<VideoPiece> Pieces(VideoTrim trim) =>
        VideoTimeline.Pieces(VideoCuts.KeptRanges(trim.Start, trim.End, Cuts), Speeds, Freezes);

    /// <summary>How long the exported file runs.</summary>
    public double OutputSeconds(VideoTrim trim) => VideoTimeline.TotalOutputSeconds(Pieces(trim));

    /// <summary>
    /// The stretch around <paramref name="at"/> that a new segment of
    /// <paramref name="kind"/> may occupy, or nothing when it may not go there.
    /// </summary>
    /// <remarks>
    /// Only zooms and speeds refuse to overlap their own kind, which is macshot's rule:
    /// two zooms at once magnify by whichever the renderer tested first, and two speeds
    /// each claim to be re-timing the same source. Everything else stacks — two censors
    /// hiding two things, or a caption over a cut, are ordinary.
    /// </remarks>
    public VideoTimeRange? GapFor(VideoEffectKind kind, double at, double totalSeconds) => kind switch
    {
        VideoEffectKind.Zoom => VideoSegmentSpan.GapAround(at, totalSeconds, Zooms.Select(zoom => zoom.Span)),
        VideoEffectKind.Speed => VideoSegmentSpan.GapAround(at, totalSeconds, Speeds.Select(speed => speed.Span)),
        _ => new VideoTimeRange(0, totalSeconds),
    };
}
