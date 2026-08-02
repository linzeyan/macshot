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
}
