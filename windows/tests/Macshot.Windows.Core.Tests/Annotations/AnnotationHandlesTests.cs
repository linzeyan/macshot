using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Annotations;

[TestClass]
public sealed class AnnotationHandlesTests
{
    [TestMethod]
    public void LinearMark_OffersItsTwoEndsAndItsBend()
    {
        var line = Shape(AnnotationTool.Line, 10, 10, 50, 30);

        var kinds = AnnotationHandles.For(line).Select(handle => handle.Kind).ToArray();

        CollectionAssert.AreEquivalent(
            new[] { AnnotationHandleKind.Start, AnnotationHandleKind.End, AnnotationHandleKind.Bend },
            kinds);
    }

    [TestMethod]
    public void Ruler_OffersNoBendBecauseTheRasterizerDrawsItStraight()
    {
        // A bowed ruler would report a distance it no longer spans, and the rasterizer
        // would draw it straight anyway, so the handle would do nothing visible.
        var measure = Shape(AnnotationTool.Measure, 10, 10, 50, 10);

        var kinds = AnnotationHandles.For(measure).Select(handle => handle.Kind).ToArray();

        CollectionAssert.DoesNotContain(kinds, AnnotationHandleKind.Bend);
    }

    [TestMethod]
    public void AreaShape_OffersFourCornersAndARotation()
    {
        var rectangle = Shape(AnnotationTool.Rectangle, 10, 10, 60, 40);

        var kinds = AnnotationHandles.For(rectangle).Select(handle => handle.Kind).ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                AnnotationHandleKind.TopLeft,
                AnnotationHandleKind.TopRight,
                AnnotationHandleKind.BottomLeft,
                AnnotationHandleKind.BottomRight,
                AnnotationHandleKind.Rotate,
            },
            kinds);
    }

    [TestMethod]
    public void FreeformStroke_OffersNoHandlesBecauseNoTwoPointsDescribeIt()
    {
        var pencil = Annotation.CreateFreeform(
            AnnotationTool.Pencil,
            [new CapturePoint(0, 0), new CapturePoint(5, 9), new CapturePoint(12, 3)]);

        Assert.AreEqual(0, AnnotationHandles.For(pencil).Count);
    }

    [TestMethod]
    public void SpriteMark_OffersNoHandlesBecauseItsPixelsAreCompositedOneToOne()
    {
        var stamp = Annotation.CreateSprite(
            AnnotationTool.Stamp,
            new CapturePoint(20, 20),
            new AnnotationSprite(2, 2, new byte[2 * 2 * 4]));

        Assert.AreEqual(0, AnnotationHandles.For(stamp).Count);
    }

    [TestMethod]
    public void CornerHandles_SitOnTheCornersOfTheUprightBounds()
    {
        // Drawn bottom-right to top-left, so the handles must follow the bounds rather
        // than the order the drag happened in.
        var rectangle = Shape(AnnotationTool.Rectangle, 60, 40, 10, 10);

        var topLeft = HandleAt(rectangle, AnnotationHandleKind.TopLeft);

        Assert.AreEqual(new CapturePoint(10, 10), topLeft);
    }

    [TestMethod]
    public void RotationHandle_FloatsClearOfTheTopEdge()
    {
        var rectangle = Shape(AnnotationTool.Rectangle, 10, 10, 60, 40);

        var rotate = HandleAt(rectangle, AnnotationHandleKind.Rotate);

        Assert.AreEqual(35, rotate.X, 1e-9, "it must sit over the middle of the shape");
        Assert.AreEqual(10 - AnnotationHandles.RotateReach, rotate.Y, 1e-9);
    }

    [TestMethod]
    public void HandlesOfATurnedShape_AreTurnedWithIt()
    {
        // A quarter turn about the centre swings the top-left corner round to where the
        // bottom-left was. Handles that stayed upright would float off the shape.
        var rectangle = Shape(AnnotationTool.Rectangle, 10, 10, 50, 30) with { Rotation = Math.PI / 2 };

        var topLeft = HandleAt(rectangle, AnnotationHandleKind.TopLeft);

        Assert.AreEqual(40, topLeft.X, 1e-9);
        Assert.AreEqual(0, topLeft.Y, 1e-9);
    }

    [TestMethod]
    public void BendHandle_SitsOnTheStraightLineWhileItIsStraight()
    {
        var line = Shape(AnnotationTool.Line, 10, 20, 50, 20);

        var bend = HandleAt(line, AnnotationHandleKind.Bend);

        Assert.AreEqual(new CapturePoint(30, 20), bend);
    }

    [TestMethod]
    public void BendHandle_FollowsTheCurveOnceItIsBowed()
    {
        var line = Shape(AnnotationTool.Line, 0, 0, 40, 0) with { Bend = 0.25 };

        var bend = HandleAt(line, AnnotationHandleKind.Bend);

        // The rasterizer doubles the bend into a quadratic control point, so the curve
        // itself passes through the fraction of the length the handle was dragged to.
        Assert.AreEqual(20, bend.X, 1e-9);
        Assert.AreEqual(10, bend.Y, 1e-9);
    }

    [TestMethod]
    public void At_FindsNothingAwayFromEveryHandle()
    {
        var rectangle = Shape(AnnotationTool.Rectangle, 10, 10, 60, 40);

        Assert.IsNull(AnnotationHandles.At(rectangle, new CapturePoint(35, 25)));
    }

    [TestMethod]
    public void At_PrefersTheNearerOfTwoHandlesInReach()
    {
        // A mark small enough that both corners answer the same press: the one the
        // pointer is actually on has to win, or one side of the shape is unreachable.
        var rectangle = Shape(AnnotationTool.Rectangle, 0, 0, 6, 6);

        var grabbed = AnnotationHandles.At(rectangle, new CapturePoint(6, 1));

        Assert.AreEqual(AnnotationHandleKind.TopRight, grabbed?.Kind);
    }

    [TestMethod]
    public void DraggingAnEnd_MovesThatEndAndLeavesTheOther()
    {
        var line = Shape(AnnotationTool.Line, 10, 10, 50, 10);

        var dragged = AnnotationHandles.Drag(line, AnnotationHandleKind.End, new CapturePoint(80, 40));

        Assert.AreEqual(new CapturePoint(10, 10), dragged.Start);
        Assert.AreEqual(new CapturePoint(80, 40), dragged.End);
    }

    [TestMethod]
    public void DraggingAnEndWithShift_SnapsAboutTheOtherEnd()
    {
        var line = Shape(AnnotationTool.Line, 0, 0, 40, 0);

        var dragged = AnnotationHandles.Drag(
            line,
            AnnotationHandleKind.End,
            new CapturePoint(100, 10),
            EditorModifiers.Constrain);

        Assert.AreEqual(0, dragged.End.Y, 1e-9);
        Assert.AreEqual(Math.Sqrt((100 * 100) + (10 * 10)), dragged.End.X, 1e-9);
    }

    [TestMethod]
    public void DraggingACorner_AnchorsTheOppositeCorner()
    {
        var rectangle = Shape(AnnotationTool.Rectangle, 10, 10, 60, 40);

        var dragged = AnnotationHandles.Drag(rectangle, AnnotationHandleKind.TopLeft, new CapturePoint(20, 25));

        var bounds = dragged.BoundingRect;
        Assert.AreEqual(20, bounds.X, 1e-9);
        Assert.AreEqual(25, bounds.Y, 1e-9);
        Assert.AreEqual(60, bounds.Right, 1e-9, "the far corner must not move");
        Assert.AreEqual(40, bounds.Bottom, 1e-9, "the far corner must not move");
    }

    [TestMethod]
    public void DraggingACornerWithShift_KeepsTheShapeSquare()
    {
        var rectangle = Shape(AnnotationTool.Rectangle, 0, 0, 40, 40);

        var dragged = AnnotationHandles.Drag(
            rectangle,
            AnnotationHandleKind.BottomRight,
            new CapturePoint(70, 30),
            EditorModifiers.Constrain);

        Assert.AreEqual(dragged.BoundingRect.Width, dragged.BoundingRect.Height, 1e-9);
    }

    [TestMethod]
    public void DraggingACornerOfATurnedShape_ReadsThePointerInTheShapesOwnFrame()
    {
        // The pointer is over the turned shape; the corner it moves is the upright one.
        // Without the inverse turn the resize would fight the rotation and the shape
        // would jump away from the pointer.
        var rectangle = Shape(AnnotationTool.Rectangle, 0, 0, 40, 20) with { Rotation = Math.PI / 2 };

        var dragged = AnnotationHandles.Drag(
            rectangle,
            AnnotationHandleKind.TopLeft,
            HandleAt(rectangle, AnnotationHandleKind.TopLeft));

        Assert.AreEqual(0, dragged.BoundingRect.X, 1e-9, "the shape must not move under an unmoved handle");
        Assert.AreEqual(0, dragged.BoundingRect.Y, 1e-9);
    }

    [TestMethod]
    public void DraggingTheRotationHandleStraightUp_LeavesTheShapeUpright()
    {
        var rectangle = Shape(AnnotationTool.Rectangle, 10, 10, 50, 30);

        var dragged = AnnotationHandles.Drag(
            rectangle,
            AnnotationHandleKind.Rotate,
            HandleAt(rectangle, AnnotationHandleKind.Rotate));

        Assert.AreEqual(0, dragged.Rotation, 1e-9);
    }

    [TestMethod]
    public void DraggingTheRotationHandleToTheRight_TurnsAQuarterClockwise()
    {
        var rectangle = Shape(AnnotationTool.Rectangle, 0, 0, 40, 40);

        var dragged = AnnotationHandles.Drag(rectangle, AnnotationHandleKind.Rotate, new CapturePoint(100, 20));

        Assert.AreEqual(Math.PI / 2, dragged.Rotation, 1e-9);
    }

    [TestMethod]
    public void DraggingTheRotationHandleWithShift_SnapsTo45Degrees()
    {
        var rectangle = Shape(AnnotationTool.Rectangle, 0, 0, 40, 40);

        var dragged = AnnotationHandles.Drag(
            rectangle,
            AnnotationHandleKind.Rotate,
            new CapturePoint(100, 25),
            EditorModifiers.Constrain);

        Assert.AreEqual(Math.PI / 2, dragged.Rotation, 1e-9);
    }

    [TestMethod]
    public void DraggingTheBendHandle_BowsTheLineByTheFractionItWasPulled()
    {
        var line = Shape(AnnotationTool.Line, 0, 0, 40, 0);

        var dragged = AnnotationHandles.Drag(line, AnnotationHandleKind.Bend, new CapturePoint(20, 10));

        Assert.AreEqual(0.25, dragged.Bend, 1e-9);
        Assert.AreEqual(new CapturePoint(0, 0), dragged.Start, "bending must not move the ends");
        Assert.AreEqual(new CapturePoint(40, 0), dragged.End, "bending must not move the ends");
    }

    [TestMethod]
    public void DraggingTheBendHandleAlongTheLine_ChangesNothing()
    {
        // Only the sideways component bows a line. Sliding the handle towards an end
        // would otherwise bow it by whatever rounding the projection left behind.
        var line = Shape(AnnotationTool.Line, 0, 0, 40, 0);

        var dragged = AnnotationHandles.Drag(line, AnnotationHandleKind.Bend, new CapturePoint(35, 0));

        Assert.AreEqual(0, dragged.Bend, 1e-9);
    }

    [TestMethod]
    public void BendIsClampedSoTheCurveCannotDoubleBackOnItself()
    {
        var line = Shape(AnnotationTool.Line, 0, 0, 40, 0);

        var dragged = AnnotationHandles.Drag(line, AnnotationHandleKind.Bend, new CapturePoint(20, 4000));

        Assert.AreEqual(AnnotationHandles.MaximumBend, dragged.Bend, 1e-9);
    }

    [TestMethod]
    public void DraggingAHandleAToolDoesNotOffer_LeavesTheMarkAlone()
    {
        var rectangle = Shape(AnnotationTool.Rectangle, 10, 10, 60, 40);

        var dragged = AnnotationHandles.Drag(rectangle, AnnotationHandleKind.Bend, new CapturePoint(0, 0));

        Assert.AreEqual(0, dragged.Bend, "an area shape has no bend to set");
    }

    [TestMethod]
    public void Differ_SeesARotationThatLeftEveryPointWhereItWas()
    {
        // Start and End are untouched by a rotation, so comparing only those would
        // discard a turn as "nothing happened" and the drag would snap back.
        var rectangle = Shape(AnnotationTool.Rectangle, 10, 10, 60, 40);

        Assert.IsTrue(AnnotationHandles.Differ(rectangle with { Rotation = 0.4 }, rectangle));
    }

    [TestMethod]
    public void Differ_SeesABendThatLeftEveryPointWhereItWas()
    {
        var line = Shape(AnnotationTool.Line, 10, 10, 60, 10);

        Assert.IsTrue(AnnotationHandles.Differ(line with { Bend = 0.2 }, line));
    }

    [TestMethod]
    public void Differ_SaysNoWhenNothingMoved()
    {
        var line = Shape(AnnotationTool.Line, 10, 10, 60, 10);

        Assert.IsFalse(AnnotationHandles.Differ(line, line));
    }

    private static Annotation Shape(AnnotationTool tool, double startX, double startY, double endX, double endY) =>
        Annotation.Create(tool, new CapturePoint(startX, startY), new CapturePoint(endX, endY));

    private static CapturePoint HandleAt(Annotation annotation, AnnotationHandleKind kind) =>
        AnnotationHandles.For(annotation).Single(handle => handle.Kind == kind).Position;
}
