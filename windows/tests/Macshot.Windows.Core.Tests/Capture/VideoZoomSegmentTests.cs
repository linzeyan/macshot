using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class VideoZoomSegmentTests
{
    private const double Tolerance = 1e-9;

    /// <summary>
    /// A zoom that snapped to its level on the first frame of the segment and back off it
    /// on the last would read as the picture being knocked rather than moved, which is the
    /// whole reason macshot ramps at all. This pins the ramp to the ends of the segment:
    /// no magnification at either edge, full magnification in the middle.
    /// </summary>
    [TestMethod]
    public void LevelAt_RampsFromNothingAtEachEdgeToTheLevelInTheMiddle()
    {
        var segment = Segment(1, 3, level: 3);

        Assert.AreEqual(1, segment.LevelAt(1), Tolerance);
        Assert.AreEqual(3, segment.LevelAt(2), Tolerance);
        Assert.AreEqual(1, segment.LevelAt(3), Tolerance);
    }

    /// <summary>
    /// Everything outside the segment must be left exactly alone, or the untouched parts
    /// of a recording would be re-sampled for no reason and come back softer than the
    /// source they were copied from.
    /// </summary>
    [TestMethod]
    public void LevelAt_LeavesEverythingOutsideTheSegmentAlone()
    {
        var segment = Segment(1, 3, level: 3);

        Assert.AreEqual(1, segment.LevelAt(0.999), Tolerance);
        Assert.AreEqual(1, segment.LevelAt(3.001), Tolerance);
    }

    /// <summary>
    /// The ramp is smoothstep rather than linear, and the difference is the whole point:
    /// a straight line starts and stops at full speed. Halfway through the ramp a linear
    /// curve would be at half the level; smoothstep is at half too, so the check that
    /// tells them apart is the quarter point, where smoothstep is well short of a quarter.
    /// </summary>
    [TestMethod]
    public void LevelAt_EasesRatherThanRampsStraight()
    {
        // A one-second segment: AutoFade caps a ramp at a fifth of it, so 0.2s each end.
        var segment = Segment(0, 1, level: 2);

        // A quarter into the ramp. Linear would give 1.25; smoothstep gives 1 + 0.15625.
        Assert.AreEqual(1.15625, segment.LevelAt(0.05), Tolerance);

        // Halfway is where the two curves agree, which is why it cannot stand alone.
        Assert.AreEqual(1.5, segment.LevelAt(0.1), Tolerance);
    }

    /// <summary>
    /// A segment shorter than twice its ramp would have the zoom ramping in and out at the
    /// same instant, and which one won would depend on the order the branches were tested
    /// rather than on anything the user set. The ramps must give way to the segment.
    /// </summary>
    [TestMethod]
    public void EffectiveFade_NeverLetsTheTwoRampsMeet()
    {
        var segment = new VideoZoomSegment(0, 0.4, 2, new CapturePoint(0.5, 0.5), 5, 5);

        Assert.IsTrue(segment.EffectiveFadeIn + segment.EffectiveFadeOut < segment.Duration);
        Assert.AreEqual(0.199, segment.EffectiveFadeIn, Tolerance);
    }

    /// <summary>
    /// macshot's rule for how long a ramp may be, and why it matters: two fifths of the
    /// segment is the most the transitions can take, so the middle three fifths always
    /// hold at the level asked for. A zoom that is all ramp reads as a wobble.
    /// </summary>
    [TestMethod]
    public void AutoFade_KeepsAPlateauNoMatterHowShortTheSegment()
    {
        Assert.AreEqual(VideoZoomSegment.DefaultFade, VideoZoomSegment.AutoFade(10), Tolerance);
        Assert.AreEqual(0.2, VideoZoomSegment.AutoFade(1), Tolerance);

        // Two fifths of the segment, and never more.
        Assert.IsTrue(VideoZoomSegment.AutoFade(0.5) * 2 <= 0.5 * 0.4 + Tolerance);
    }

    /// <summary>
    /// The rectangle is what the export actually crops, so it has to be the whole frame
    /// whenever nothing is being magnified. Anything else and every frame outside a zoom
    /// would be resampled, softening the parts of the recording the user did not touch.
    /// </summary>
    [TestMethod]
    public void SourceRectAt_IsTheWholeFrameWhereNothingIsMagnified()
    {
        var segment = Segment(1, 3, level: 3);
        var outside = segment.SourceRectAt(0, 1920, 1080);

        Assert.AreEqual(new CaptureRegion(0, 0, 1920, 1080), outside);
        Assert.AreEqual(new CaptureRegion(0, 0, 1920, 1080), segment.SourceRectAt(1, 1920, 1080));
    }

    /// <summary>
    /// A centred zoom must take an equal bite off each edge. Getting this wrong drifts the
    /// picture sideways as the ramp runs, which looks like a pan nobody asked for.
    /// </summary>
    [TestMethod]
    public void SourceRectAt_TakesTheMiddleForACentredZoom()
    {
        var segment = Segment(0, 2, level: 2);
        var rect = segment.SourceRectAt(1, 1000, 500);

        Assert.AreEqual(250, rect.X, Tolerance);
        Assert.AreEqual(125, rect.Y, Tolerance);
        Assert.AreEqual(500, rect.Width, Tolerance);
        Assert.AreEqual(250, rect.Height, Tolerance);
    }

    /// <summary>
    /// A zoom centred on a corner must slide back inside the frame rather than reach for
    /// pixels that do not exist. Unclamped it would leave a black wedge along two edges of
    /// the export, which is the failure macshot's translation clamp exists to prevent.
    /// </summary>
    [TestMethod]
    public void SourceRectAt_StaysInsideTheFrameForACornerZoom()
    {
        var corner = Segment(0, 2, level: 2) with { Center = new CapturePoint(0, 0) };
        var rect = corner.SourceRectAt(1, 1000, 500);

        Assert.AreEqual(new CaptureRegion(0, 0, 500, 250), rect);

        var far = corner with { Center = new CapturePoint(1, 1) };
        Assert.AreEqual(new CaptureRegion(500, 250, 500, 250), far.SourceRectAt(1, 1000, 500));
    }

    /// <summary>
    /// The rectangle shrinks in step with the level as the ramp runs, which is what makes
    /// the magnification continuous. A rectangle that jumped between sizes would show as
    /// the picture stepping rather than gliding.
    /// </summary>
    [TestMethod]
    public void SourceRectAt_ShrinksInStepWithTheRamp()
    {
        var segment = Segment(0, 2, level: 4);

        var early = segment.SourceRectAt(0.1, 800, 800);
        var middle = segment.SourceRectAt(1, 800, 800);

        Assert.IsTrue(early.Width > middle.Width);
        Assert.AreEqual(800 / segment.LevelAt(0.1), early.Width, Tolerance);
        Assert.AreEqual(200, middle.Width, Tolerance);
    }

    /// <summary>
    /// A zoom placed near either end of a recording must keep its full length rather than
    /// be shortened to fit, or the two-second default would silently become whatever room
    /// happened to be left and the zoom would be a different length every time.
    /// </summary>
    [TestMethod]
    public void Placed_PushesASegmentOffTheEndRatherThanShorteningIt()
    {
        var atStart = VideoZoomSegment.Placed(0.2, 10);
        Assert.AreEqual(0, atStart.Start, Tolerance);
        Assert.AreEqual(VideoZoomSegment.DefaultDuration, atStart.Duration, Tolerance);

        var atEnd = VideoZoomSegment.Placed(9.9, 10);
        Assert.AreEqual(10, atEnd.End, Tolerance);
        Assert.AreEqual(VideoZoomSegment.DefaultDuration, atEnd.Duration, Tolerance);
    }

    /// <summary>
    /// A recording shorter than the default segment still has to produce a usable one, or
    /// adding a zoom to a one-second clip would place a segment that runs past the end and
    /// the export would ask for frames that are not there.
    /// </summary>
    [TestMethod]
    public void Placed_FitsInsideARecordingShorterThanTheDefaultSegment()
    {
        var segment = VideoZoomSegment.Placed(0.4, 0.8);

        Assert.IsTrue(segment.Start >= 0);
        Assert.IsTrue(segment.End <= 0.8 + Tolerance);
        Assert.IsTrue(segment.Duration >= VideoZoomSegment.MinDuration);
    }

    /// <summary>
    /// Dragging an edge past the other one would invert the segment, and an inverted
    /// segment has a negative duration that every ramp calculation downstream divides by.
    /// </summary>
    [TestMethod]
    public void WithStartAndWithEnd_RefuseToInvertTheSegment()
    {
        var segment = Segment(2, 4, level: 2);

        Assert.AreEqual(4 - VideoZoomSegment.MinDuration, segment.WithStart(9, 10).Start, Tolerance);
        Assert.AreEqual(2 + VideoZoomSegment.MinDuration, segment.WithEnd(0, 10).End, Tolerance);
    }

    /// <summary>
    /// Dragging the pill along the band must not change how long the zoom lasts — that is
    /// what separates moving it from resizing it, and a move that also stretched would
    /// make the band impossible to use.
    /// </summary>
    [TestMethod]
    public void MovedTo_KeepsTheLengthAndStaysInsideTheRecording()
    {
        var segment = Segment(2, 4, level: 2);

        var moved = segment.MovedTo(7, 10);
        Assert.AreEqual(7, moved.Start, Tolerance);
        Assert.AreEqual(2, moved.Duration, Tolerance);

        var pastTheEnd = segment.MovedTo(9.5, 10);
        Assert.AreEqual(8, pastTheEnd.Start, Tolerance);
        Assert.AreEqual(2, pastTheEnd.Duration, Tolerance);
    }

    /// <summary>
    /// Setting the level re-scales the ramps, because the segment may have been dragged
    /// shorter since they were last set. Without it a half-second segment would keep
    /// macshot's default ramps and be nothing but ramp — the level asked for would never
    /// actually appear in the export.
    /// </summary>
    [TestMethod]
    public void WithLevel_RescalesTheRampsToWhateverLengthTheSegmentNowHas()
    {
        var shortened = new VideoZoomSegment(
            0,
            0.5,
            2,
            new CapturePoint(0.5, 0.5),
            VideoZoomSegment.DefaultFade,
            VideoZoomSegment.DefaultFade).WithLevel(3);

        Assert.AreEqual(VideoZoomSegment.AutoFade(0.5), shortened.FadeIn, Tolerance);
        Assert.AreEqual(3, shortened.LevelAt(0.25), Tolerance);
    }

    /// <summary>
    /// The level is held to the range macshot's own control offers. Below 1.2 the zoom is
    /// not visible enough to justify re-encoding the recording; above 5 a 1080p frame is
    /// being magnified from a 384-pixel-wide crop and the export is mush.
    /// </summary>
    [TestMethod]
    public void WithLevel_HoldsTheLevelToWhatIsWorthEncoding()
    {
        var segment = Segment(0, 2, level: 2);

        Assert.AreEqual(VideoZoomSegment.MinLevel, segment.WithLevel(1).Level, Tolerance);
        Assert.AreEqual(VideoZoomSegment.MaxLevel, segment.WithLevel(50).Level, Tolerance);
    }

    /// <summary>
    /// The whole reason macshot added <c>clampedCenter</c>: a zoom placed against an edge
    /// used to magnify somewhere other than where its rectangle was drawn, because the
    /// export's own clamp slid the crop and the preview never heard about it. Pulling the
    /// centre in first makes that clamp a no-op, so this asserts the equality rather than
    /// the clamp — the region the user drew is the region the export crops.
    /// </summary>
    [TestMethod]
    public void Window_IsExactlyWhatTheExportCropsEvenHardAgainstAFrameEdge()
    {
        // At level 4 only an eighth of the frame separates a legal centre from each edge,
        // and 0.95 is far outside that.
        var segment = Segment(0, 2, level: 4) with { Center = new CapturePoint(0.95, 0.05) };

        var window = segment.Window;
        var cropped = segment.SourceRectAt(1, 1000, 800);

        Assert.AreEqual(window.X * 1000, cropped.X, Tolerance);
        Assert.AreEqual(window.Y * 800, cropped.Y, Tolerance);
        Assert.AreEqual(window.Width * 1000, cropped.Width, Tolerance);
        Assert.AreEqual(window.Height * 800, cropped.Height, Tolerance);

        // And the two agreed because the centre was pulled in, not by accident: both axes
        // were outside the range and both came back to it.
        Assert.AreEqual(0.875, VideoZoomSegment.ClampedCenter(segment.Center, 4).X, Tolerance);
        Assert.AreEqual(0.125, VideoZoomSegment.ClampedCenter(segment.Center, 4).Y, Tolerance);
    }

    /// <summary>
    /// The editor draws a rectangle, the model stores a level and a centre, and the export
    /// crops a rectangle again. Every one of those hops has to be lossless or the region
    /// would creep across the picture over a drag, which is the failure a user reads as the
    /// preview fighting them.
    /// </summary>
    [TestMethod]
    public void WithWindow_TurnsADraggedRectangleIntoTheLevelThatCropsItBack()
    {
        var drawn = new CaptureRegion(0.1, 0.2, 0.4, 0.4);
        var segment = Segment(0, 2, level: 2).WithWindow(drawn);

        Assert.AreEqual(2.5, segment.Level, Tolerance);

        Assert.AreEqual(drawn.X, segment.Window.X, Tolerance);
        Assert.AreEqual(drawn.Y, segment.Window.Y, Tolerance);
        Assert.AreEqual(drawn.Width, segment.Window.Width, Tolerance);

        var cropped = segment.SourceRectAt(1, 800, 600);
        Assert.AreEqual(80, cropped.X, Tolerance);
        Assert.AreEqual(120, cropped.Y, Tolerance);
        Assert.AreEqual(320, cropped.Width, Tolerance);
        Assert.AreEqual(240, cropped.Height, Tolerance);
    }

    /// <summary>
    /// Dragging the region across the picture chooses what is magnified, not how much. A
    /// move that also changed the level would make the region impossible to place, and an
    /// edge is where it would happen: the centre is pulled in there, and letting the level
    /// follow it would shrink the region out from under the hand holding it.
    /// </summary>
    [TestMethod]
    public void WithWindow_ChoosesWhatIsMagnifiedWithoutChangingHowMuch()
    {
        var segment = Segment(0, 2, level: 4);
        var side = segment.Window.Width;

        var pushedOffTheEdge = segment.WithWindow(new CaptureRegion(0.9, 0.9, side, side));

        Assert.AreEqual(4, pushedOffTheEdge.Level, Tolerance);
        Assert.AreEqual(1 - side, pushedOffTheEdge.Window.X, Tolerance);
        Assert.AreEqual(1 - side, pushedOffTheEdge.Window.Y, Tolerance);
    }

    /// <summary>
    /// A zoom window is locked to the video's aspect — a zoom that squashed the picture
    /// would not be a zoom — so there is no such thing as widening one alone. This drags
    /// the corner straight sideways along the window's own bottom edge and requires the
    /// height to come with it, and pins how the two axes share the drag: by their length in
    /// frame pixels, so the longer one leads and a square frame splits it evenly.
    /// </summary>
    [TestMethod]
    public void ResizedWindow_MovesBothAxesTogetherWhicheverWayTheCornerIsDragged()
    {
        var window = new CaptureRegion(0.25, 0.25, 0.5, 0.5);
        var sideways = new CapturePoint(0.85, 0.75);

        var wide = VideoZoomSegment.ResizedWindow(window, sideways, 1920, 1080);

        Assert.AreEqual(wide.Width, wide.Height, Tolerance);
        Assert.IsTrue(wide.Height > 0.5, "a sideways drag has to carry the height with it");

        // Between what each axis asked for alone — 0.6 across, 0.5 down — and nearer the
        // long one, which is what "weighted by frame pixels" buys.
        Assert.IsTrue(wide.Width is > 0.55 and < 0.6);

        // Neither axis is longer on a square frame, so the two share the drag equally.
        var square = VideoZoomSegment.ResizedWindow(window, sideways, 1000, 1000);
        Assert.AreEqual(0.55, square.Width, Tolerance);

        // The corner opposite the handle stays put, which is what makes a resize read as
        // one rather than as the whole region jumping.
        Assert.AreEqual(0.25, wide.X, Tolerance);
        Assert.AreEqual(0.25, wide.Y, Tolerance);
    }

    /// <summary>
    /// The region is the only control macshot leaves for the zoom level, so its two extremes
    /// have to be the model's: dragged shut it must stop at <c>MaxLevel</c>, and dragged
    /// open at <c>MinLevel</c>, or the picture could be resized to a level the export then
    /// silently clamps to something else.
    /// </summary>
    [TestMethod]
    public void ResizedWindow_StopsAtTheLevelsWorthEncoding()
    {
        var window = new CaptureRegion(0.4, 0.4, 0.2, 0.2);
        var segment = Segment(0, 2, level: 2);

        // Dragged onto the anchored corner: the smallest window is one MaxLevel-th of the
        // frame, and it is still exactly where it was pinned.
        var tightest = VideoZoomSegment.ResizedWindow(window, new CapturePoint(0.4, 0.4), 1920, 1080);
        Assert.AreEqual(1 / VideoZoomSegment.MaxLevel, tightest.Width, Tolerance);
        Assert.AreEqual(VideoZoomSegment.MaxLevel, segment.WithWindow(tightest).Level, Tolerance);
        Assert.AreEqual(0.4, tightest.X, Tolerance);

        // Dragged far past the far corner: one MinLevel-th, and slid back inside the frame,
        // because the anchor cannot hold a window wider than the room left beside it.
        var loosest = VideoZoomSegment.ResizedWindow(window, new CapturePoint(9, 9), 1920, 1080);
        Assert.AreEqual(1 / VideoZoomSegment.MinLevel, loosest.Width, Tolerance);
        Assert.AreEqual(VideoZoomSegment.MinLevel, segment.WithWindow(loosest).Level, Tolerance);
        Assert.AreEqual(1 - (1 / VideoZoomSegment.MinLevel), loosest.X, Tolerance);
    }

    private static VideoZoomSegment Segment(double start, double end, double level)
    {
        var fade = VideoZoomSegment.AutoFade(end - start);
        return new VideoZoomSegment(start, end, level, new CapturePoint(0.5, 0.5), fade, fade);
    }
}
