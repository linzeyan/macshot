using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class VideoSpeedSegmentTests
{
    private const double Tolerance = 1e-9;

    /// <summary>
    /// The floor and the ceiling exist because outside them a speed segment stops being
    /// one: slower than a quarter is a slideshow and faster than ten collapses any
    /// segment worth having below a frame or two. A factor from a settings file or a
    /// stale menu must be brought back inside rather than accepted.
    /// </summary>
    [TestMethod]
    public void ClampFactor_KeepsAFactorInsideTheRangeAPlayerCanShow()
    {
        Assert.AreEqual(VideoSpeedSegment.MinFactor, VideoSpeedSegment.ClampFactor(0.01), Tolerance);
        Assert.AreEqual(VideoSpeedSegment.MaxFactor, VideoSpeedSegment.ClampFactor(100), Tolerance);
        Assert.AreEqual(2, VideoSpeedSegment.ClampFactor(2), Tolerance);
    }

    /// <summary>
    /// 1× is deliberately not offered. A segment that says it re-times and does not is a
    /// pill on the band the user will drag and adjust wondering why the export never
    /// changes; the band's Delete is how a speed is removed.
    /// </summary>
    [TestMethod]
    public void PresetFactors_DoesNotOfferOneWhichWouldBeASegmentThatDoesNothing()
    {
        Assert.IsFalse(VideoSpeedSegment.PresetFactors.Any(factor => Math.Abs(factor - 1) < 0.001));
    }

    /// <summary>
    /// The minimum is measured on the output rather than on the source, which is the only
    /// side that matters: a fifth of a second at 10× is two hundredths of a second of
    /// export, which is one frame. The floor has to grow with the factor.
    /// </summary>
    [TestMethod]
    public void MinSourceDuration_GrowsWithTheFactorSoTheOutputIsAlwaysVisible()
    {
        Assert.AreEqual(
            VideoSpeedSegment.MinOutputDuration * 10,
            VideoSpeedSegment.MinSourceDuration(10),
            Tolerance);

        var segment = VideoSpeedSegment.Placed(5, new VideoTimeRange(0, 30), factor: 10);
        Assert.IsTrue(segment.OutputDuration >= VideoSpeedSegment.MinOutputDuration);
    }

    /// <summary>
    /// Raising the factor on a segment already near the floor must widen it rather than
    /// leave an export the user cannot see. macshot's menu allows the invisible result;
    /// this port grows the segment so the trade shows on the band instead of showing
    /// nowhere.
    /// </summary>
    [TestMethod]
    public void WithFactor_WidensASegmentTooShortToShowTheNewFactor()
    {
        var segment = new VideoSpeedSegment(5, 5.3, 2).WithFactor(10, totalSeconds: 30);

        Assert.AreEqual(10, segment.Factor, Tolerance);
        Assert.IsTrue(segment.OutputDuration >= VideoSpeedSegment.MinOutputDuration);
        Assert.AreEqual(5.15, (segment.Start + segment.End) / 2, 1e-6);
    }

    /// <summary>
    /// A segment already long enough must keep the length the user dragged it to. A
    /// change of factor that also resized the pill would undo work the user did
    /// deliberately.
    /// </summary>
    [TestMethod]
    public void WithFactor_LeavesASegmentThatIsAlreadyLongEnoughWhereItWas()
    {
        var segment = new VideoSpeedSegment(2, 6, 2).WithFactor(3, totalSeconds: 30);

        Assert.AreEqual(2, segment.Start, Tolerance);
        Assert.AreEqual(6, segment.End, Tolerance);
        Assert.AreEqual(3, segment.Factor, Tolerance);
    }

    /// <summary>
    /// The band refuses to place a speed over another one, because two claims on the same
    /// source cannot both be honoured. A gap is offered where there is one and nothing
    /// where the click landed inside an existing segment.
    /// </summary>
    [TestMethod]
    public void GapAround_OffersTheRoomBetweenSegmentsAndNothingInsideOne()
    {
        var taken = new[] { new VideoTimeRange(2, 4), new VideoTimeRange(8, 9) };

        var between = VideoSegmentSpan.GapAround(6, 20, taken);
        Assert.IsNotNull(between);
        Assert.AreEqual(4, between.Value.Start, Tolerance);
        Assert.AreEqual(8, between.Value.End, Tolerance);

        Assert.IsNull(VideoSegmentSpan.GapAround(3, 20, taken));

        var after = VideoSegmentSpan.GapAround(12, 20, taken);
        Assert.IsNotNull(after);
        Assert.AreEqual(9, after.Value.Start, Tolerance);
        Assert.AreEqual(20, after.Value.End, Tolerance);
    }

    /// <summary>
    /// A speed placed in a gap must stay inside it. One that overran would overlap the
    /// neighbour it was placed beside, and the export would then truncate one of them —
    /// silently changing a segment the user had already set.
    /// </summary>
    [TestMethod]
    public void Placed_StaysInsideTheGapItWasOfferedRatherThanOverrunningItsNeighbour()
    {
        var segment = VideoSpeedSegment.Placed(4.9, new VideoTimeRange(4, 5));

        Assert.IsTrue(segment.Start >= 4 - Tolerance);
        Assert.IsTrue(segment.End <= 5 + Tolerance);
    }
}

[TestClass]
public sealed class VideoFreezeSegmentTests
{
    private const double Tolerance = 1e-9;

    /// <summary>
    /// A freeze placed at exactly the start or the end of a recording falls in no kept
    /// range and would be dropped by the export without saying anything. Nudging it
    /// inside is what makes a freeze placed at the very beginning do something.
    /// </summary>
    [TestMethod]
    public void Placed_KeepsAFreezeOffBothEndsWhereItWouldOtherwiseBeDroppedSilently()
    {
        Assert.AreEqual(VideoFreezeSegment.EdgeMargin, VideoFreezeSegment.Placed(0, 10).At, Tolerance);
        Assert.AreEqual(10 - VideoFreezeSegment.EdgeMargin, VideoFreezeSegment.Placed(10, 10).At, Tolerance);

        var kept = new[] { new VideoTimeRange(0, 10) };
        var pieces = VideoTimeline.Pieces(kept, [], [VideoFreezeSegment.Placed(0, 10)]);
        Assert.IsTrue(pieces.Any(piece => piece.Kind is VideoPieceKind.Freeze));
    }

    /// <summary>
    /// The hold has both a floor and a ceiling: below the floor nothing is visible, and
    /// past the ceiling the export is a still image with a soundtrack rather than a
    /// recording that pauses.
    /// </summary>
    [TestMethod]
    public void ClampHold_KeepsTheHoldBetweenInvisibleAndAStillImage()
    {
        Assert.AreEqual(VideoFreezeSegment.MinHold, VideoFreezeSegment.ClampHold(0), Tolerance);
        Assert.AreEqual(VideoFreezeSegment.MaxHold, VideoFreezeSegment.ClampHold(1000), Tolerance);
    }
}
