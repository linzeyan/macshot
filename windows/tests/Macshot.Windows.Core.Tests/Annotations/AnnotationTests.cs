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
    public void HitTest_FilledRectangleGrabsItsInterior()
    {
        // A redaction block is solid, so its whole area is the annotation.
        var redaction = Annotation.Create(
            AnnotationTool.FilledRectangle,
            new CapturePoint(10, 10),
            new CapturePoint(50, 30));

        Assert.IsTrue(redaction.HitTest(new CapturePoint(30, 20)));
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
}
