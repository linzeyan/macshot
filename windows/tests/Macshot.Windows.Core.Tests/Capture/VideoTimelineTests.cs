using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class VideoTimelineTests
{
    private const double Tolerance = 1e-9;

    private static readonly VideoTimeRange[] Whole = [new(0, 10)];

    /// <summary>
    /// A recording nobody has re-timed must come out as exactly one piece covering all of
    /// it. Anything else means the export would seek to a piece boundary in the middle of
    /// an untouched recording, and every seek is a chance for the decoder to land on the
    /// wrong frame.
    /// </summary>
    [TestMethod]
    public void Pieces_LeavesAnUntouchedRecordingAsASinglePieceAtOneToOne()
    {
        var pieces = VideoTimeline.Pieces(Whole, [], []);

        Assert.AreEqual(1, pieces.Count);
        Assert.AreEqual(VideoPieceKind.Normal, pieces[0].Kind);
        Assert.AreEqual(10, pieces[0].OutputDuration, Tolerance);
        Assert.AreEqual(1, pieces[0].Factor, Tolerance);
    }

    /// <summary>
    /// The point of a speed segment is that its output is shorter than its source. Two
    /// seconds at 2× must take one second of the exported file, and the untouched
    /// material either side must keep its own length — a factor applied to the whole
    /// recording rather than to the segment is the mistake this pins.
    /// </summary>
    [TestMethod]
    public void Pieces_ShortensOnlyTheStretchTheSpeedCovers()
    {
        var pieces = VideoTimeline.Pieces(Whole, [new VideoSpeedSegment(4, 6, 2)], []);

        Assert.AreEqual(3, pieces.Count);
        Assert.AreEqual(4, pieces[0].OutputDuration, Tolerance);
        Assert.AreEqual(VideoPieceKind.Speed, pieces[1].Kind);
        Assert.AreEqual(1, pieces[1].OutputDuration, Tolerance);
        Assert.AreEqual(4, pieces[2].OutputDuration, Tolerance);
        Assert.AreEqual(9, VideoTimeline.TotalOutputSeconds(pieces), Tolerance);
    }

    /// <summary>
    /// A factor below 1 must lengthen rather than shorten. Slow motion is the case a
    /// tutorial actually uses, and a division written the other way up would silently
    /// speed up everything the user asked to slow down.
    /// </summary>
    [TestMethod]
    public void Pieces_LengthensTheOutputForAFactorBelowOne()
    {
        var pieces = VideoTimeline.Pieces(Whole, [new VideoSpeedSegment(0, 2, 0.5)], []);

        Assert.AreEqual(VideoPieceKind.Speed, pieces[0].Kind);
        Assert.AreEqual(4, pieces[0].OutputDuration, Tolerance);
        Assert.AreEqual(12, VideoTimeline.TotalOutputSeconds(pieces), Tolerance);
    }

    /// <summary>
    /// A freeze holds one instant, so its piece must consume no source at all. A piece
    /// that advanced the source clock during the hold would show the picture creeping
    /// forward, which is the opposite of what a freeze is.
    /// </summary>
    [TestMethod]
    public void Pieces_HoldsOneInstantForTheWholeOfAFreeze()
    {
        var pieces = VideoTimeline.Pieces(Whole, [], [new VideoFreezeSegment(4, 2)]);

        var freeze = pieces.Single(piece => piece.Kind is VideoPieceKind.Freeze);
        Assert.AreEqual(4, freeze.SourceStart, Tolerance);
        Assert.AreEqual(0, freeze.SourceDuration, Tolerance);
        Assert.AreEqual(2, freeze.OutputDuration, Tolerance);

        // The hold is added to the recording rather than taken out of it.
        Assert.AreEqual(12, VideoTimeline.TotalOutputSeconds(pieces), Tolerance);
    }

    /// <summary>
    /// A freeze whose instant a cut removed must not appear. macshot drops it silently
    /// too, and the alternative is worse: a hold on a frame that is no longer in the
    /// output would show whatever the decoder returned for a time nothing maps to.
    /// </summary>
    [TestMethod]
    public void Pieces_DropsAFreezeThatFallsInsideACut()
    {
        var kept = VideoCuts.KeptRanges(0, 10, [new VideoCutSegment(3, 6)]);
        var pieces = VideoTimeline.Pieces(kept, [], [new VideoFreezeSegment(4, 2)]);

        Assert.IsFalse(pieces.Any(piece => piece.Kind is VideoPieceKind.Freeze));
        Assert.AreEqual(7, VideoTimeline.TotalOutputSeconds(pieces), Tolerance);
    }

    /// <summary>
    /// Two speeds the user dragged across one another must not both claim the same
    /// source. The overlap is resolved in favour of the later one, and the earlier is
    /// truncated — the export's length would otherwise depend on which the loop tested
    /// first, which is a bug that only shows up as a file of the wrong duration.
    /// </summary>
    [TestMethod]
    public void Pieces_TruncatesTheEarlierOfTwoOverlappingSpeedsRatherThanCountingBoth()
    {
        var pieces = VideoTimeline.Pieces(
            Whole,
            [new VideoSpeedSegment(2, 6, 2), new VideoSpeedSegment(4, 8, 4)],
            []);

        var covered = pieces.Where(piece => piece.Kind is VideoPieceKind.Speed).ToList();
        Assert.AreEqual(2, covered.Count);
        Assert.AreEqual(4, covered[0].SourceEnd, Tolerance);
        Assert.AreEqual(4, covered[1].SourceStart, Tolerance);

        // Every piece laid end to end still covers the whole recording exactly once.
        Assert.AreEqual(10, pieces.Sum(piece => piece.SourceDuration), Tolerance);
    }

    /// <summary>
    /// Cuts, speeds and freezes have to compose, because the band lets a user place all
    /// three. This is the whole arithmetic in one: ten seconds, two cut out, two seconds
    /// of what is left at 2×, and a one-second hold.
    /// </summary>
    [TestMethod]
    public void Pieces_ComposesACutASpeedAndAFreezeIntoOneOutputLength()
    {
        var kept = VideoCuts.KeptRanges(0, 10, [new VideoCutSegment(8, 10)]);
        var pieces = VideoTimeline.Pieces(
            kept,
            [new VideoSpeedSegment(2, 4, 2)],
            [new VideoFreezeSegment(6, 1)]);

        // 8 kept, less 1 saved by the speed, plus 1 held.
        Assert.AreEqual(8, VideoTimeline.TotalOutputSeconds(pieces), Tolerance);
    }

    /// <summary>
    /// The time map is what every frame of the export is fetched through, so a moment
    /// after a cut must resolve to the source instant on the far side of it. Getting this
    /// wrong shows as the export playing the material the cut was supposed to remove.
    /// </summary>
    [TestMethod]
    public void SourceAt_LooksPastACutToTheMaterialThatSurvivedIt()
    {
        var kept = VideoCuts.KeptRanges(0, 10, [new VideoCutSegment(4, 6)]);
        var map = VideoTimeline.TimeMap(VideoTimeline.Pieces(kept, [], []));

        Assert.AreEqual(3, VideoTimeline.SourceAt(map, 3), Tolerance);
        Assert.AreEqual(6, VideoTimeline.SourceAt(map, 4), Tolerance);
        Assert.AreEqual(7, VideoTimeline.SourceAt(map, 5), Tolerance);
    }

    /// <summary>
    /// Inside a sped-up stretch the source has to advance faster than the output. Half a
    /// second into a 2× segment must be a whole second into the material — this is the
    /// single line that makes a speed segment look sped up rather than merely shorter.
    /// </summary>
    [TestMethod]
    public void SourceAt_AdvancesTheSourceByTheFactorInsideASpeedSegment()
    {
        var map = VideoTimeline.TimeMap(VideoTimeline.Pieces(Whole, [new VideoSpeedSegment(0, 4, 2)], []));

        Assert.AreEqual(0, VideoTimeline.SourceAt(map, 0), Tolerance);
        Assert.AreEqual(1, VideoTimeline.SourceAt(map, 0.5), Tolerance);
        Assert.AreEqual(3, VideoTimeline.SourceAt(map, 1.5), Tolerance);
    }

    /// <summary>
    /// Every moment of a freeze must resolve to the same source instant. If it did not,
    /// the hold would drift and the effect would read as very slow motion instead.
    /// </summary>
    [TestMethod]
    public void SourceAt_ReturnsTheSameInstantThroughoutAFreeze()
    {
        var map = VideoTimeline.TimeMap(VideoTimeline.Pieces(Whole, [], [new VideoFreezeSegment(4, 2)]));

        Assert.AreEqual(4, VideoTimeline.SourceAt(map, 4), Tolerance);
        Assert.AreEqual(4, VideoTimeline.SourceAt(map, 5), Tolerance);
        Assert.AreEqual(4, VideoTimeline.SourceAt(map, 5.999), Tolerance);
        Assert.AreEqual(4, VideoTimeline.SourceAt(map, 6), Tolerance);
    }

    /// <summary>
    /// The final frame of an export lands a rounding error past the end of the map. It
    /// must resolve to the end of the recording rather than fall back to the output
    /// clock, or a zoom or censor still running at the tail would snap off on the very
    /// last frame — which is exactly where it is most visible.
    /// </summary>
    [TestMethod]
    public void SourceAt_HoldsTheLastEntryPastTheEndRatherThanFallingBackToTheOutputClock()
    {
        var kept = VideoCuts.KeptRanges(0, 10, [new VideoCutSegment(0, 4)]);
        var map = VideoTimeline.TimeMap(VideoTimeline.Pieces(kept, [], []));

        Assert.AreEqual(10, VideoTimeline.SourceAt(map, 6.0001), Tolerance);
    }

    /// <summary>
    /// A recording nobody re-timed must yield exactly one audio run covering the trim.
    /// One run is one background track in the composition; a run per piece would be a
    /// decoder per piece, and the fix for the zoom export dropping its audio would have
    /// traded one bug for a slower one.
    /// </summary>
    [TestMethod]
    public void AudioRuns_CarriesAnUntouchedRecordingAsOneRun()
    {
        var runs = VideoTimeline.AudioRuns(VideoTimeline.Pieces([new VideoTimeRange(2, 8)], [], []));

        Assert.AreEqual(1, runs.Count);
        Assert.AreEqual(2, runs[0].SourceStart, Tolerance);
        Assert.AreEqual(8, runs[0].SourceEnd, Tolerance);
        Assert.AreEqual(0, runs[0].OutputStart, Tolerance);
    }

    /// <summary>
    /// A cut has to move the audio after it earlier by exactly what it removed, or the
    /// export drifts out of sync from the cut onwards — the failure a viewer notices
    /// immediately and cannot explain.
    /// </summary>
    [TestMethod]
    public void AudioRuns_PullsWhatFollowsACutForwardByWhatTheCutRemoved()
    {
        var kept = VideoCuts.KeptRanges(0, 10, [new VideoCutSegment(4, 6)]);
        var runs = VideoTimeline.AudioRuns(VideoTimeline.Pieces(kept, [], []));

        Assert.AreEqual(2, runs.Count);
        Assert.AreEqual(0, runs[0].OutputStart, Tolerance);
        Assert.AreEqual(6, runs[1].SourceStart, Tolerance);
        Assert.AreEqual(4, runs[1].OutputStart, Tolerance);
    }

    /// <summary>
    /// A freeze is silent, and the audio on the far side of it must be delayed by the
    /// hold. Both halves matter: sound over a held frame is a chirp, and sound that did
    /// not wait for the hold would put the whole rest of the recording out of sync.
    /// </summary>
    [TestMethod]
    public void AudioRuns_GoesSilentForAFreezeAndDelaysWhatFollowsItByTheHold()
    {
        var runs = VideoTimeline.AudioRuns(
            VideoTimeline.Pieces([new VideoTimeRange(0, 10)], [], [new VideoFreezeSegment(4, 2)]));

        Assert.AreEqual(2, runs.Count);
        Assert.AreEqual(4, runs[0].SourceEnd, Tolerance);
        Assert.AreEqual(4, runs[1].SourceStart, Tolerance);
        Assert.AreEqual(6, runs[1].OutputStart, Tolerance);
    }

    /// <summary>
    /// A sped-up stretch carries no audio, because Windows exposes no way to re-time a
    /// track. What must not happen is audio at 1× laid over it, which would run past the
    /// segment and desynchronise everything after — silence is the honest answer and this
    /// pins it, along with the timing of the run that follows.
    /// </summary>
    [TestMethod]
    public void AudioRuns_LeavesASpeedSegmentSilentRatherThanLettingItRunOnAtOneToOne()
    {
        var runs = VideoTimeline.AudioRuns(
            VideoTimeline.Pieces([new VideoTimeRange(0, 10)], [new VideoSpeedSegment(4, 8, 2)], []));

        Assert.AreEqual(2, runs.Count);
        Assert.AreEqual(4, runs[0].SourceEnd, Tolerance);
        Assert.AreEqual(8, runs[1].SourceStart, Tolerance);

        // Four seconds of source played in two, so what follows starts at six.
        Assert.AreEqual(6, runs[1].OutputStart, Tolerance);
    }
}
