using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Annotations;

[TestClass]
public sealed class AnnotationTests
{
    [TestMethod]
    public void BoundingRect_CoversEveryFreeformSample()
    {
        var pencil = Annotation.CreateFreeform(
            AnnotationTool.Pencil,
            [new CapturePoint(10, 10), new CapturePoint(30, 5), new CapturePoint(20, 40)]);

        Assert.AreEqual(new CaptureRegion(10, 5, 20, 35), pencil.BoundingRect);
    }

    [TestMethod]
    public void HitTest_RectangleIgnoresItsHollowInterior()
    {
        // A hollow rectangle must let a click through to whatever it frames,
        // otherwise a rectangle drawn around content makes that content unselectable.
        var rectangle = Annotation.Create(
            AnnotationTool.Rectangle,
            new CapturePoint(10, 10),
            new CapturePoint(50, 30));

        Assert.IsTrue(rectangle.HitTest(new CapturePoint(10, 20)), "the outline must be grabbable");
        Assert.IsFalse(rectangle.HitTest(new CapturePoint(30, 20)), "the interior must not be grabbable");
    }

    [TestMethod]
    public void HitTest_CensorGrabsItsInterior()
    {
        // A redaction block is solid, so its whole area is the annotation.
        var redaction = Annotation.Create(
            AnnotationTool.Censor,
            new CapturePoint(10, 10),
            new CapturePoint(50, 30));

        Assert.IsTrue(redaction.HitTest(new CapturePoint(30, 20)));
    }

    [TestMethod]
    public void HitTest_SpotlightGrabsTheRegionItLights()
    {
        // The lit rectangle is the mark, so the whole of it grabs — as macshot's does.
        // Tested against the hairline instead it would take a pixel-accurate click to
        // pick up, and it used to be tested as the line from one corner to the other:
        // grabbable along the diagonal and nowhere else.
        var spotlight = Annotation.Create(
            AnnotationTool.Highlight,
            new CapturePoint(10, 10),
            new CapturePoint(50, 30));

        Assert.IsTrue(spotlight.HitTest(new CapturePoint(15, 25)));
        Assert.IsFalse(spotlight.HitTest(new CapturePoint(80, 20)));
    }

    [TestMethod]
    public void HitTest_EllipseUsesTheOutlineNotTheBoundingBox()
    {
        var ellipse = Annotation.Create(
            AnnotationTool.Ellipse,
            new CapturePoint(0, 0),
            new CapturePoint(100, 50));

        Assert.IsTrue(ellipse.HitTest(new CapturePoint(100, 25)), "a point on the outline must hit");
        Assert.IsFalse(ellipse.HitTest(new CapturePoint(50, 25)), "the center must not hit");
        Assert.IsFalse(ellipse.HitTest(new CapturePoint(2, 2)), "a bounding box corner must not hit");
    }

    [TestMethod]
    public void HitTest_ToleranceGrowsWithStrokeWidth()
    {
        var start = new CapturePoint(0, 0);
        var end = new CapturePoint(100, 0);
        var thin = Annotation.Create(AnnotationTool.Line, start, end, AnnotationStyle.Default with { StrokeWidth = 1 });
        var thick = Annotation.Create(AnnotationTool.Line, start, end, AnnotationStyle.Default with { StrokeWidth = 40 });

        // A visibly thick stroke must be grabbable anywhere it is actually painted.
        Assert.IsFalse(thin.HitTest(new CapturePoint(50, 18)));
        Assert.IsTrue(thick.HitTest(new CapturePoint(50, 18)));
    }

    [TestMethod]
    public void Translate_MovesFreeformSamplesWithTheAnnotation()
    {
        var pencil = Annotation.CreateFreeform(
            AnnotationTool.Pencil,
            [new CapturePoint(10, 10), new CapturePoint(20, 20)]);

        var moved = pencil.Translate(5, -3);

        Assert.AreEqual(new CapturePoint(15, 7), moved.Points[0]);
        Assert.AreEqual(new CapturePoint(25, 17), moved.Points[1]);
        Assert.AreEqual(new CapturePoint(10, 10), pencil.Points[0], "the original must stay untouched");
        Assert.AreEqual(pencil.Id, moved.Id, "moving must not change identity");
    }

    [TestMethod]
    public void IsMovable_ExcludesToolsThatDescribeAnInteraction()
    {
        Assert.IsFalse(Annotation.Create(AnnotationTool.Crop, default, default).IsMovable);
        Assert.IsFalse(Annotation.Create(AnnotationTool.ColorSampler, default, default).IsMovable);
        Assert.IsTrue(Annotation.Create(AnnotationTool.Arrow, default, default).IsMovable);
    }

    [TestMethod]
    public void CreateFreeform_RejectsAnEmptyStroke()
    {
        Assert.ThrowsException<ArgumentException>(
            () => Annotation.CreateFreeform(AnnotationTool.Pencil, []));
    }

    [TestMethod]
    public void CreateSprite_TakesItsBoundsFromTheSprite()
    {
        // The sprite is composited one to one, so bounds that disagreed with it would
        // hit test an area the mark does not cover.
        var badge = Annotation.CreateSprite(
            AnnotationTool.Number,
            new CapturePoint(40, 25),
            SpriteOf(12, 9));

        Assert.AreEqual(new CaptureRegion(40, 25, 12, 9), badge.BoundingRect);
        Assert.IsTrue(badge.HitTest(new CapturePoint(46, 29)), "the badge must be grabbable over its pixels");
    }

    [TestMethod]
    public void Translate_KeepsTheSprite()
    {
        // Sprite is an ordinary record member so that copying carries it. The macOS
        // product hand-writes clone() and loses whatever nobody remembered to add.
        var badge = Annotation.CreateSprite(AnnotationTool.Number, new CapturePoint(0, 0), SpriteOf(10, 10));

        var moved = badge.Translate(7, 4);

        Assert.AreSame(badge.Sprite, moved.Sprite);
        Assert.AreEqual(new CaptureRegion(7, 4, 10, 10), moved.BoundingRect);
    }

    [TestMethod]
    public void CreateSprite_RejectsAToolThatIsDrawnFromGeometry()
    {
        // An arrow carrying a sprite would be drawn twice over, once as geometry and
        // once as pixels. The two sets of tools have to stay disjoint.
        Assert.ThrowsException<ArgumentException>(
            () => Annotation.CreateSprite(AnnotationTool.Arrow, default, SpriteOf(4, 4)));
    }

    private static AnnotationSprite SpriteOf(int width, int height) =>
        new(width, height, new byte[width * height * 4]);

    [TestMethod]
    public void Span_IsTheStraightDistanceBetweenTheEnds()
    {
        // What the ruler reports, and the length a bend is a fraction of.
        var line = Annotation.Create(AnnotationTool.Line, new CapturePoint(10, 20), new CapturePoint(13, 24));

        Assert.AreEqual(5, line.Span, 1e-9);
    }

    /// <summary>
    /// The − and + walk a point at a time and stop dead at each end.
    /// </summary>
    /// <remarks>
    /// Both buttons repeat while they are held, so the size is walked rather than nudged —
    /// which means the end of the range is reached constantly and by accident. Clamped
    /// here rather than on the way out through the style, so a size held past 200 does not
    /// climb into a number the row keeps showing while the label stops growing, leaving the
    /// user to press − a hundred times before anything moves.
    /// </remarks>
    [TestMethod]
    public void StepFontSize_StopsAtEachEndRatherThanWalkingPastIt()
    {
        Assert.AreEqual(21, AnnotationStyle.StepFontSize(20, 1));
        Assert.AreEqual(19, AnnotationStyle.StepFontSize(20, -1));

        Assert.AreEqual(AnnotationStyle.MinFontSize, AnnotationStyle.StepFontSize(AnnotationStyle.MinFontSize, -1));
        Assert.AreEqual(AnnotationStyle.MaxFontSize, AnnotationStyle.StepFontSize(AnnotationStyle.MaxFontSize, 1));

        // A settings file that has never held a label size hands back nothing at all, and
        // a first press of + must land on a size rather than on NaN — which no clamp
        // repairs, since every comparison against it is false.
        Assert.AreEqual(AnnotationStyle.DefaultFontSize + 1, AnnotationStyle.StepFontSize(double.NaN, 1));
    }

    /// <summary>
    /// The line round the glyphs is a fraction of the label's size, with a floor.
    /// </summary>
    /// <remarks>
    /// Its whole job is holding a label apart from what is behind it, which is a job that
    /// scales: a fixed width would be invisible at 72 point and would close up the counters
    /// of an 8-point label until it read as a smudge. The floor is there because the
    /// fraction alone puts the smallest labels under a whole unit, where an outline is an
    /// antialiasing artefact rather than an edge.
    /// </remarks>
    [TestMethod]
    public void GlyphStrokeWidth_FollowsTheLabelsSizeAndNeverThinsToNothing()
    {
        Assert.AreEqual(
            AnnotationStyle.GlyphStrokeWidth(100),
            AnnotationStyle.GlyphStrokeWidth(50) * 2,
            1e-9);

        Assert.AreEqual(1, AnnotationStyle.GlyphStrokeWidth(AnnotationStyle.MinFontSize));
    }

    [TestMethod]
    public void AnchorPath_PutsTheEndsRoundTheAnchorsInOrder()
    {
        // The chain is derived rather than stored, which is what stops Start and the first
        // anchor from drifting apart. macOS stores the whole chain and keeps its two ends
        // agreeing with it by hand in five places; this is the reason the port does not.
        var arrow = Bent(AnnotationTool.Arrow, new CapturePoint(50, 40), new CapturePoint(70, 10));

        CollectionAssert.AreEqual(
            new[]
            {
                new CapturePoint(0, 0),
                new CapturePoint(50, 40),
                new CapturePoint(70, 10),
                new CapturePoint(100, 0),
            },
            arrow.AnchorPath.ToArray());
    }

    [TestMethod]
    public void AnchorPath_IsTheTwoEndsWhenNothingHasBeenAnchored()
    {
        // Every caller reads the chain rather than branching on whether there is one, so
        // an unbent mark has to answer with a chain of two.
        var line = Annotation.Create(AnnotationTool.Line, new CapturePoint(1, 2), new CapturePoint(3, 4));

        CollectionAssert.AreEqual(
            new[] { new CapturePoint(1, 2), new CapturePoint(3, 4) },
            line.AnchorPath.ToArray());
        Assert.IsFalse(line.HasWaypoints);
    }

    [TestMethod]
    public void Span_ReportsTheLengthDrawnRatherThanTheDistanceBetweenTheEnds()
    {
        // A ruler bent up and over reports what the rule covers, not the chord it happens
        // to share its ends with. This is the number written on the capture, so a stale
        // straight-line answer would be a measurement that is simply wrong.
        var ruler = Bent(AnnotationTool.Measure, new CapturePoint(50, 60));

        Assert.IsTrue(ruler.Span > 120, $"a rule bent 60 pixels off a 100 pixel chord read {ruler.Span}");
    }

    [TestMethod]
    public void HitTest_FollowsTheCurveRatherThanTheChordOnceAnchorsAreAdded()
    {
        // Both halves matter. Grabbing along the chord would answer to clicks on empty
        // canvas, and not grabbing along the curve would leave the mark on screen with no
        // way to select, restyle or delete it.
        var arrow = Bent(AnnotationTool.Arrow, new CapturePoint(50, 60));

        Assert.IsTrue(arrow.HitTest(new CapturePoint(50, 60)), "the anchor itself must grab");
        Assert.IsFalse(arrow.HitTest(new CapturePoint(50, 0)), "the chord must no longer grab");
    }

    [TestMethod]
    public void BoundingRect_ReachesTheAnchorsAndNotJustTheEnds()
    {
        // The selection outline and the handle frame are both drawn from these bounds. Left
        // at the ends, the chrome would sit beside the mark it belongs to.
        var line = Bent(AnnotationTool.Line, new CapturePoint(50, 80));

        Assert.AreEqual(new CaptureRegion(0, 0, 100, 80), line.BoundingRect);
    }

    [TestMethod]
    public void Translate_CarriesTheAnchorsWithTheMark()
    {
        // Dragging a bent arrow has to move the whole shape. Anchors left behind would
        // stretch it into something the user never drew, with each drag distorting it more.
        var moved = Bent(AnnotationTool.Line, new CapturePoint(50, 40)).Translate(10, -5);

        CollectionAssert.AreEqual(new[] { new CapturePoint(60, 35) }, moved.Waypoints.ToArray());
        Assert.AreEqual(new CapturePoint(10, -5), moved.Start);
    }

    [TestMethod]
    public void WithAnchorAt_InsertsIntoTheSpanItWasAimedAtRatherThanAppending()
    {
        // Appended, every anchor after the first would land at the far end and the mark
        // would fold back over itself — which is why macshot searches for the nearest span.
        var arrow = Bent(AnnotationTool.Arrow, new CapturePoint(80, 40))
            .WithAnchorAt(new CapturePoint(20, 5));

        Assert.AreEqual(2, arrow.Waypoints.Count);
        Assert.IsTrue(arrow.Waypoints[0].X < arrow.Waypoints[1].X, "the new anchor belongs before the old one");
    }

    [TestMethod]
    public void WithAnchorAt_KeepsTheNewAnchorClearOfTheOnesEitherSideOfIt()
    {
        // An anchor landing on top of its neighbour gives the spline a span of no length,
        // which draws as a kink instead of the curve that was asked for. macshot holds it a
        // twentieth clear of both ends for the same reason.
        var line = Annotation.Create(AnnotationTool.Line, new CapturePoint(0, 0), new CapturePoint(100, 0))
            .WithAnchorAt(new CapturePoint(-40, 0));

        Assert.AreEqual(5, line.Waypoints[0].X, 1e-9);
    }

    [TestMethod]
    public void WithAnchorAt_ClearsTheBendTheAnchorsHaveJustReplaced()
    {
        // Two ways of bowing the same mark, and only the anchors are drawn. A bend left set
        // would sit under a grip the toolbar no longer offers, and reopening the capture
        // would restore a curve nobody could see or edit.
        var line = Annotation.Create(AnnotationTool.Line, new CapturePoint(0, 0), new CapturePoint(100, 0))
            with { Bend = 0.3, BendAlong = 0.1 };

        var bent = line.WithAnchorAt(new CapturePoint(50, 20));

        Assert.AreEqual(0, bent.Bend);
        Assert.AreEqual(0, bent.BendAlong);
    }

    [TestMethod]
    public void WithAnchorAt_DropsARulersReadingBecauseTheLengthHasChanged()
    {
        // The sprite is a number about a distance this call has just made longer. Kept, the
        // rule would go on insisting it is as long as it was before it was bent.
        var ruler = Annotation.Create(AnnotationTool.Measure, new CapturePoint(0, 0), new CapturePoint(100, 0))
            with { Sprite = new AnnotationSprite(2, 2, new byte[2 * 2 * 4]) };

        Assert.IsNull(ruler.WithAnchorAt(new CapturePoint(50, 30)).Sprite);
    }

    [TestMethod]
    public void WithAnchorAt_LeavesAloneTheToolsThatHaveNowhereToPutAnAnchor()
    {
        // A shape is its bounding rectangle and a stroke is already a path: an anchor on
        // either would be state nothing draws, nothing grabs and nothing can take back off.
        var rectangle = Annotation.Create(
            AnnotationTool.Rectangle,
            new CapturePoint(0, 0),
            new CapturePoint(100, 50));

        Assert.AreEqual(0, rectangle.WithAnchorAt(new CapturePoint(50, 0)).Waypoints.Count);
    }

    /// <summary>A mark from (0,0) to (100,0) bent through the given anchors.</summary>
    private static Annotation Bent(AnnotationTool tool, params CapturePoint[] anchors) =>
        Annotation.Create(tool, new CapturePoint(0, 0), new CapturePoint(100, 0)) with { Waypoints = anchors };
}
