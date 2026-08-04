namespace Macshot.Windows.Core.Capture;

/// <summary>What a stretch of the output is doing to the source underneath it.</summary>
public enum VideoPieceKind
{
    /// <summary>Playing at 1×, which is every part of a recording nobody has re-timed.</summary>
    Normal,

    Speed,

    Freeze,
}

/// <summary>
/// One contiguous stretch of the exported file, and where its pictures come from.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>VideoSpeeds.Piece</c>. Cuts, speeds and freezes are three different
/// edits to a user and one thing to a renderer: a statement that output second
/// <c>x</c> shows source second <c>y</c>. Reducing all three to a list of these is what
/// lets the export loop stay a single pass with no branch per effect in it.
/// </para>
/// <para>
/// <see cref="OutputDuration"/> is stored rather than derived because a freeze has no
/// source width to derive it from.
/// </para>
/// </remarks>
public readonly record struct VideoPiece(
    VideoPieceKind Kind,
    double SourceStart,
    double SourceEnd,
    double OutputDuration)
{
    public double SourceDuration => Math.Max(0, SourceEnd - SourceStart);

    /// <summary>
    /// How much source a second of output consumes. 1 for an untouched stretch, the
    /// speed for a sped-up one, and zero for a freeze — which is the whole of what
    /// makes a freeze a freeze.
    /// </summary>
    public double Factor => OutputDuration > 0 ? SourceDuration / OutputDuration : 1;
}

/// <summary>
/// Where on the source clock a moment of the exported file comes from.
/// </summary>
/// <remarks>
/// macshot's <c>EffectsCompositionInstruction.TimeMapEntry</c>. macOS hands this to an
/// <c>AVVideoCompositing</c>, which is given a frame and asks the map which source
/// instant it belongs to; this port has no such seat in the pipeline, so it asks the
/// map first and then goes and fetches that frame. The map is the same either way.
/// </remarks>
public readonly record struct VideoTimeMapEntry(
    double OutputStart,
    double OutputEnd,
    double SourceStart,
    double Factor)
{
    public double SourceAt(double outputSeconds) =>
        SourceStart + ((outputSeconds - OutputStart) * Factor);
}

/// <summary>
/// A stretch of the source whose audio reaches the output unaltered, and where it lands.
/// </summary>
/// <remarks>
/// Only 1× stretches produce one. A freeze is silent because a held frame has no sound,
/// which is macshot's behaviour too; a speed segment is silent because Windows has no
/// way to re-time an audio track — see <see cref="VideoTimeline.AudioRuns"/>.
/// </remarks>
public readonly record struct VideoAudioRun(double SourceStart, double SourceEnd, double OutputStart)
{
    public double Duration => Math.Max(0, SourceEnd - SourceStart);
}

/// <summary>
/// Turns a trim and the three temporal effects into the one thing the export reads.
/// </summary>
/// <remarks>
/// macshot's <c>VideoSpeeds</c> and the <c>piecesToTimeMap</c> beside it, joined because
/// nothing here ever wants one without the other.
/// </remarks>
public static class VideoTimeline
{
    /// <summary>
    /// The pieces the output is made of, in the order they play.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defensive in macshot's own three ways, because the band cannot prevent all of
    /// them: a freeze inside a cut is dropped, overlapping speeds have the earlier one
    /// truncated at the later one's start, and a freeze outside every kept range is
    /// dropped.
    /// </para>
    /// <para>
    /// One deliberate difference from macshot, recorded rather than copied. Its freeze
    /// covers a six-hundredth of a second of source, because <c>insertTimeRange</c>
    /// needs a non-empty range to hand to <c>scaleTimeRange</c> afterwards. This port
    /// fetches frames by timestamp and needs no such range, so its freeze covers no
    /// source at all — which means the frame after a freeze is the frame that would have
    /// followed it anyway, rather than the one a six-hundredth later.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<VideoPiece> Pieces(
        IEnumerable<VideoTimeRange> keptRanges,
        IEnumerable<VideoSpeedSegment> speeds,
        IEnumerable<VideoFreezeSegment> freezes)
    {
        ArgumentNullException.ThrowIfNull(keptRanges);
        ArgumentNullException.ThrowIfNull(speeds);
        ArgumentNullException.ThrowIfNull(freezes);

        var ordered = speeds
            .Where(speed => speed.End > speed.Start && speed.Factor > 0)
            .OrderBy(speed => speed.Start)
            .ToList();

        // Overlaps truncated rather than refused, and the later one wins. The band stops
        // a speed being placed over another, but a drag can still push one across its
        // neighbour, and two claims on the same source would otherwise make the output
        // length depend on which was tested first.
        var clean = new List<VideoSpeedSegment>(ordered.Count);
        foreach (var speed in ordered)
        {
            if (clean.Count > 0 && speed.Start < clean[^1].End)
            {
                clean[^1] = clean[^1] with { End = Math.Max(clean[^1].Start, speed.Start) };
            }

            clean.Add(speed);
        }

        var points = freezes
            .Where(freeze => freeze.Hold > 0)
            .OrderBy(freeze => freeze.At)
            .ToList();

        var pieces = new List<VideoPiece>();

        foreach (var range in keptRanges)
        {
            if (range.End <= range.Start)
            {
                continue;
            }

            var cursor = range.Start;

            foreach (var freeze in points.Where(f => f.At > range.Start && f.At < range.End))
            {
                if (freeze.At > cursor)
                {
                    cursor = EmitSpeedSliced(pieces, clean, cursor, freeze.At);
                }

                pieces.Add(new VideoPiece(VideoPieceKind.Freeze, freeze.At, freeze.At, freeze.Hold));
            }

            EmitSpeedSliced(pieces, clean, cursor, range.End);
        }

        return pieces;
    }

    /// <summary>
    /// Fills <c>[from, until)</c> with normal and sped-up pieces, and says where it got to.
    /// </summary>
    private static double EmitSpeedSliced(
        List<VideoPiece> pieces,
        List<VideoSpeedSegment> speeds,
        double from,
        double until)
    {
        var cursor = from;
        if (until <= cursor)
        {
            return cursor;
        }

        foreach (var speed in speeds)
        {
            if (speed.End <= cursor)
            {
                continue;
            }

            if (speed.Start >= until)
            {
                break;
            }

            var speedStart = Math.Max(speed.Start, cursor);
            var speedEnd = Math.Min(speed.End, until);

            if (speedStart > cursor)
            {
                pieces.Add(new VideoPiece(VideoPieceKind.Normal, cursor, speedStart, speedStart - cursor));
            }

            if (speedEnd > speedStart)
            {
                pieces.Add(new VideoPiece(
                    VideoPieceKind.Speed,
                    speedStart,
                    speedEnd,
                    (speedEnd - speedStart) / speed.Factor));

                cursor = speedEnd;
            }
        }

        if (cursor < until)
        {
            pieces.Add(new VideoPiece(VideoPieceKind.Normal, cursor, until, until - cursor));
            cursor = until;
        }

        return cursor;
    }

    /// <summary>The pieces laid end to end on the output clock.</summary>
    public static IReadOnlyList<VideoTimeMapEntry> TimeMap(IEnumerable<VideoPiece> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);

        var entries = new List<VideoTimeMapEntry>();
        var cursor = 0.0;

        foreach (var piece in pieces)
        {
            if (piece.OutputDuration <= 0)
            {
                continue;
            }

            entries.Add(new VideoTimeMapEntry(
                cursor,
                cursor + piece.OutputDuration,
                piece.SourceStart,
                piece.Factor));

            cursor += piece.OutputDuration;
        }

        return entries;
    }

    public static double TotalOutputSeconds(IEnumerable<VideoPiece> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);

        return pieces.Sum(piece => piece.OutputDuration);
    }

    /// <summary>
    /// Which source instant the frame at <paramref name="outputSeconds"/> shows.
    /// </summary>
    /// <remarks>
    /// Past the end of the map the last entry is extended rather than the answer falling
    /// back to the output clock, which is macshot's rule and its reason: rounding at the
    /// tail puts the final frame a hair beyond the last entry, and answering with the
    /// output time there would make a zoom or a censor snap off on the very last frame.
    /// </remarks>
    public static double SourceAt(IReadOnlyList<VideoTimeMapEntry> map, double outputSeconds)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (map.Count == 0)
        {
            return outputSeconds;
        }

        foreach (var entry in map)
        {
            if (outputSeconds >= entry.OutputStart && outputSeconds < entry.OutputEnd)
            {
                return entry.SourceAt(outputSeconds);
            }
        }

        var last = map[^1];
        return outputSeconds >= last.OutputEnd ? last.SourceAt(last.OutputEnd) : map[0].SourceStart;
    }

    /// <summary>
    /// The stretches of source whose audio can be carried into the output as it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything playing at 1× produces one, and nothing else does. A freeze is silent
    /// on macOS too — it holds a frame, and two frames of audio stretched over a second
    /// is a chirp.
    /// </para>
    /// <para>
    /// A speed segment is silent here and is <em>not</em> on macOS, which is the one
    /// place these two products differ on this feature. AVFoundation's
    /// <c>scaleTimeRange</c> re-times an audio track along with the video and re-pitches
    /// it while it does; Windows exposes no equivalent anywhere —
    /// <see cref="System.Collections.Generic.IEnumerable{T}"/> of PCM through an
    /// <c>AudioGraph</c> is the only route, and an <c>AudioGraph</c> renders in real time,
    /// so a two-minute recording would take two minutes to export. Silence over the
    /// sped-up stretch is the honest answer, and the editor says so before writing the
    /// file.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<VideoAudioRun> AudioRuns(IEnumerable<VideoPiece> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);

        var runs = new List<VideoAudioRun>();
        var cursor = 0.0;

        foreach (var piece in pieces)
        {
            if (piece.Kind is VideoPieceKind.Normal && piece.OutputDuration > 0)
            {
                // Merged with the one before where the two meet on both clocks, so a
                // recording split only by speeds does not become a background track per
                // piece. Windows mixes background tracks, and a hundred of them is a
                // hundred decoders.
                if (runs.Count > 0
                    && Math.Abs(runs[^1].SourceEnd - piece.SourceStart) < Meeting
                    && Math.Abs(runs[^1].OutputStart + (runs[^1].SourceEnd - runs[^1].SourceStart) - cursor) < Meeting)
                {
                    runs[^1] = runs[^1] with { SourceEnd = piece.SourceEnd };
                }
                else
                {
                    runs.Add(new VideoAudioRun(piece.SourceStart, piece.SourceEnd, cursor));
                }
            }

            cursor += piece.OutputDuration;
        }

        return runs;
    }

    /// <summary>
    /// How near two boundaries have to be to count as the same one, for the same reason
    /// <see cref="VideoCuts"/> keeps its own slack: these numbers come from pixels.
    /// </summary>
    private const double Meeting = 0.001;
}
