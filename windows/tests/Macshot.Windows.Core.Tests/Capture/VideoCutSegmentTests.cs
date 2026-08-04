using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class VideoCutSegmentTests
{
    private const double Tolerance = 1e-9;

    /// <summary>
    /// A cut is the one effect that removes rather than transforms, so the whole pipeline
    /// downstream is written against what survives it. This pins the complement: a cut in
    /// the middle of a recording leaves the part in front of it and the part behind it,
    /// and nothing of the cut itself.
    /// </summary>
    [TestMethod]
    public void KeptRanges_LeavesWhatIsInFrontOfACutAndWhatIsBehindIt()
    {
        var kept = VideoCuts.KeptRanges(0, 10, [new VideoCutSegment(4, 6)]);

        Assert.AreEqual(2, kept.Count);
        Assert.AreEqual(0, kept[0].Start, Tolerance);
        Assert.AreEqual(4, kept[0].End, Tolerance);
        Assert.AreEqual(6, kept[1].Start, Tolerance);
        Assert.AreEqual(10, kept[1].End, Tolerance);
    }

    /// <summary>
    /// Two cuts the user dragged until they met must produce one gap, not two with a
    /// sliver between them. The sliver would be a single stray frame of exactly the
    /// material that was supposed to go, which is the failure a redaction cannot afford.
    /// </summary>
    [TestMethod]
    public void KeptRanges_TreatsTwoCutsThatMeetAsOne()
    {
        var kept = VideoCuts.KeptRanges(0, 10, [new VideoCutSegment(2, 5), new VideoCutSegment(5, 8)]);

        Assert.AreEqual(2, kept.Count);
        Assert.AreEqual(2, kept[0].End, Tolerance);
        Assert.AreEqual(8, kept[1].Start, Tolerance);
    }

    /// <summary>
    /// The band drags cuts in pixels, so two that were meant to meet land a fraction
    /// apart. Without the slack the export would emit a kept range far shorter than a
    /// frame between them, and the encoder would either refuse it or write a duplicate
    /// frame. A gap of half a millisecond must still count as no gap.
    /// </summary>
    [TestMethod]
    public void KeptRanges_IgnoresAGapTooSmallToHoldAFrame()
    {
        var kept = VideoCuts.KeptRanges(0, 10, [new VideoCutSegment(2, 5), new VideoCutSegment(5.0005, 8)]);

        Assert.AreEqual(2, kept.Count);
        Assert.AreEqual(2, kept[0].End, Tolerance);
        Assert.AreEqual(8, kept[1].Start, Tolerance);
    }

    /// <summary>
    /// A cut is placed on the source clock so it survives the trim handles moving under
    /// it, which means a handle can be dragged past one. The part of a cut outside the
    /// trim must simply not apply — treating it as applying would remove material the
    /// trim already excludes and shorten the export twice over.
    /// </summary>
    [TestMethod]
    public void KeptRanges_ClipsACutTheTrimHandleWasDraggedAcross()
    {
        var kept = VideoCuts.KeptRanges(3, 10, [new VideoCutSegment(1, 5)]);

        Assert.AreEqual(1, kept.Count);
        Assert.AreEqual(5, kept[0].Start, Tolerance);
        Assert.AreEqual(10, kept[0].End, Tolerance);
    }

    /// <summary>
    /// A cut covering everything the trim keeps must leave nothing rather than leave a
    /// zero-length range. A range of no length reaches the encoder as a file with no
    /// frames, which every player reports as corrupt rather than as empty.
    /// </summary>
    [TestMethod]
    public void KeptRanges_LeavesNothingWhenACutCoversTheWholeTrim()
    {
        var kept = VideoCuts.KeptRanges(2, 8, [new VideoCutSegment(0, 20)]);

        Assert.AreEqual(0, kept.Count);
    }

    /// <summary>
    /// Overlapping cuts are one cut. Emitting the complement of each separately would
    /// produce kept ranges that overlap each other, and the export would then write the
    /// same stretch of recording twice.
    /// </summary>
    [TestMethod]
    public void KeptRanges_MergesCutsThatOverlapRatherThanRepeatingWhatIsBetweenThem()
    {
        var kept = VideoCuts.KeptRanges(0, 10, [new VideoCutSegment(2, 6), new VideoCutSegment(4, 8)]);

        Assert.AreEqual(2, kept.Count);
        Assert.AreEqual(2, kept[0].End, Tolerance);
        Assert.AreEqual(8, kept[1].Start, Tolerance);
        Assert.AreEqual(4, VideoCuts.TotalSeconds(kept), Tolerance);
    }

    /// <summary>
    /// Cuts arriving in the order the user happened to place them must give the same
    /// answer as cuts in time order. The band appends, so the list is in placement order
    /// essentially always, and an algorithm that only worked on a sorted list would fail
    /// on the ordinary case rather than on an exotic one.
    /// </summary>
    [TestMethod]
    public void KeptRanges_DoesNotDependOnTheOrderTheCutsWerePlacedIn()
    {
        var placed = VideoCuts.KeptRanges(0, 10, [new VideoCutSegment(7, 8), new VideoCutSegment(2, 3)]);
        var sorted = VideoCuts.KeptRanges(0, 10, [new VideoCutSegment(2, 3), new VideoCutSegment(7, 8)]);

        CollectionAssert.AreEqual(placed.ToArray(), sorted.ToArray());
    }

    /// <summary>
    /// The export's length is what the bottom bar's estimate and the encoder's stream
    /// duration are both computed from. A recording with two seconds cut out of ten must
    /// report eight, or the file ends before its own declared duration and players show
    /// a stalled scrubber at the end.
    /// </summary>
    [TestMethod]
    public void TotalSeconds_ReportsTheRecordingLessWhatWasCut()
    {
        var kept = VideoCuts.KeptRanges(0, 10, [new VideoCutSegment(4, 6)]);

        Assert.AreEqual(8, VideoCuts.TotalSeconds(kept), Tolerance);
    }

    /// <summary>
    /// A cut placed near the beginning must be its full length rather than squashed
    /// against zero, which is macshot's rule for every effect: a cut placed at 0.2s on a
    /// long recording is still the second that every other cut is.
    /// </summary>
    [TestMethod]
    public void Placed_KeepsItsFullLengthNearTheStartRatherThanBeingShortened()
    {
        var cut = VideoCutSegment.Placed(0.2, totalSeconds: 30);

        Assert.AreEqual(VideoCutSegment.DefaultDuration, cut.Duration, Tolerance);
        Assert.AreEqual(0, cut.Start, Tolerance);
    }

    /// <summary>
    /// Dragging the head of a cut past its own tail would invert it, and an inverted
    /// segment removes nothing while still drawing a pill. The head must stop a minimum
    /// duration short of the tail.
    /// </summary>
    [TestMethod]
    public void WithStart_StopsShortOfItsOwnEndRatherThanInvertingTheCut()
    {
        var cut = new VideoCutSegment(2, 4).WithStart(9, totalSeconds: 30);

        Assert.AreEqual(4 - VideoCutSegment.MinDuration, cut.Start, Tolerance);
        Assert.IsTrue(cut.Duration >= VideoCutSegment.MinDuration);
    }

    /// <summary>
    /// Dragging the body of a cut off the end of the recording must slide it back rather
    /// than stretch or shrink it — the length is what the user set, and a drag that
    /// silently changed it would undo work.
    /// </summary>
    [TestMethod]
    public void MovedTo_KeepsItsLengthWhenDraggedPastTheEndOfTheRecording()
    {
        var cut = new VideoCutSegment(2, 4).MovedTo(29, totalSeconds: 30);

        Assert.AreEqual(2, cut.Duration, Tolerance);
        Assert.AreEqual(28, cut.Start, Tolerance);
        Assert.AreEqual(30, cut.End, Tolerance);
    }
}
