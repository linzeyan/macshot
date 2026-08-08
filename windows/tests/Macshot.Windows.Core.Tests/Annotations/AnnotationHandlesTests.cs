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
    public void Spotlight_OffersItsCornersButNoRotation()
    {
        // An area, not a line: it used to offer the two ends of a stroke, which on a
        // region that has four corners to adjust is two handles in the wrong places. The
        // rotation is left off because the rasterizer punches the lit region out of the
        // frame's own rows and columns — a turned spotlight would show its ring at one
        // angle and its light at another.
        var spotlight = Shape(AnnotationTool.Highlight, 10, 10, 60, 40);

        var kinds = AnnotationHandles.For(spotlight).Select(handle => handle.Kind).ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                AnnotationHandleKind.TopLeft,
                AnnotationHandleKind.TopRight,
                AnnotationHandleKind.BottomLeft,
                AnnotationHandleKind.BottomRight,
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
    public void BendHandle_SitsOnTheMiddleOfALineThatHasNotBeenBent()
    {
        // macshot falls back to the midpoint of the two ends when there is no control
        // point yet (OverlayView.swift:4370-4378), so the grip is where a user reaches
        // for it before they know the line can be bent at all.
        var line = Shape(AnnotationTool.Line, 10, 20, 50, 20);

        Assert.AreEqual(new CapturePoint(30, 20), HandleAt(line, AnnotationHandleKind.Bend));
    }

    [TestMethod]
    public void BendHandle_SitsOnTheControlPointRatherThanOnTheCurve()
    {
        // Beside the curve, not on it: macshot draws the grip at controlPoint itself and
        // runs dashed arms out to it. A cubic reaches only three quarters of the way
        // towards its control, so the curve passes well inside the grip — and a handle
        // that lagged the pointer to stay on the line would read as a broken drag.
        var line = Shape(AnnotationTool.Line, 0, 0, 40, 0) with { Bend = 0.25 };

        var bend = HandleAt(line, AnnotationHandleKind.Bend);

        Assert.AreEqual(20, bend.X, 1e-9);
        Assert.AreEqual(10, bend.Y, 1e-9);
    }

    [TestMethod]
    public void DraggingTheBendHandleAlongTheLine_MovesWhereTheBulgeIs()
    {
        // macshot stores the pointer where it is (OverlayView.swift:6011), so the control
        // point slides along the line as well as across it. Confined to the perpendicular
        // the bow could only ever be symmetric, and the bulge that clears an obstacle near
        // one end of an arrow was unreachable however far the handle was dragged.
        var line = Shape(AnnotationTool.Line, 0, 0, 40, 0);

        var dragged = AnnotationHandles.Drag(line, AnnotationHandleKind.Bend, new CapturePoint(30, 10));

        Assert.AreEqual(0.25, dragged.Bend, 1e-9, "ten across a line forty long");
        Assert.AreEqual(0.25, dragged.BendAlong, 1e-9, "and ten past its middle");
    }

    [TestMethod]
    public void TheBendHandleIsNotClamped()
    {
        // The clamp existed because past a certain bow the grip stopped following the
        // pointer and the drag read as broken. Once the grip is the very point being set
        // it always follows, so the clamp has nothing left to prevent — and macshot has
        // none.
        var line = Shape(AnnotationTool.Line, 0, 0, 40, 0);

        var dragged = AnnotationHandles.Drag(line, AnnotationHandleKind.Bend, new CapturePoint(20, 4000));

        Assert.AreEqual(100, dragged.Bend, 1e-9);
    }

    [TestMethod]
    public void ABowKeepsItsShapeWhenTheLineIsDraggedLonger()
    {
        // Stored as fractions of the length rather than as macshot's screen point, which
        // is the one place this port knowingly differs: an absolute control point stays
        // put while the line grows past it, so the bow the user drew flattens out.
        var line = Shape(AnnotationTool.Line, 0, 0, 40, 0) with { Bend = 0.25, BendAlong = 0.1 };

        var longer = AnnotationHandles.Drag(line, AnnotationHandleKind.End, new CapturePoint(80, 0));

        Assert.AreEqual(0.25, longer.Bend, 1e-9);
        Assert.AreEqual(0.1, longer.BendAlong, 1e-9);
    }

    [TestMethod]
    public void Differ_SeesABulgeSlidAlongALineThatLeftEveryPointWhereItWas()
    {
        // Without this a drag that only slid the control point along the line would read
        // as "nothing happened" and the curve would snap back to what it was.
        var line = Shape(AnnotationTool.Line, 10, 10, 60, 10);

        Assert.IsTrue(AnnotationHandles.Differ(line with { BendAlong = 0.2 }, line));
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
    public void SlidingTheBendHandleAlongTheLine_LeavesItStraight()
    {
        // The bulge moves towards an end without appearing: a control point on the line
        // it belongs to describes no curve at all, whatever it does along it.
        var line = Shape(AnnotationTool.Line, 0, 0, 40, 0);

        var dragged = AnnotationHandles.Drag(line, AnnotationHandleKind.Bend, new CapturePoint(35, 0));

        Assert.AreEqual(0, dragged.Bend, 1e-9);
        Assert.AreEqual(0.375, dragged.BendAlong, 1e-9);
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

    [TestMethod]
    public void ALabelledRuler_StillOffersItsEnds()
    {
        // A ruler carries a sprite, but only as its reading — the mark is the line, and
        // the line has to stay reshapable. Treating any sprite as "this mark is pixels"
        // would take the handles off the one tool whose number depends on them.
        var ruler = Shape(AnnotationTool.Measure, 10, 10, 50, 10)
            with { Sprite = new AnnotationSprite(2, 2, new byte[2 * 2 * 4]) };

        var kinds = AnnotationHandles.For(ruler).Select(handle => handle.Kind).ToArray();

        CollectionAssert.AreEquivalent(new[] { AnnotationHandleKind.Start, AnnotationHandleKind.End }, kinds);
    }

    [TestMethod]
    public void ReshapingARuler_DropsTheReadingItNoLongerMatches()
    {
        // Otherwise a ruler dragged from 40 pixels to 90 keeps insisting it is 40.
        var ruler = Shape(AnnotationTool.Measure, 0, 0, 40, 0)
            with { Sprite = new AnnotationSprite(2, 2, new byte[2 * 2 * 4]) };

        var dragged = AnnotationHandles.Drag(ruler, AnnotationHandleKind.End, new CapturePoint(90, 0));

        Assert.IsNull(dragged.Sprite);
        Assert.AreEqual(90, dragged.Span, 1e-9);
    }

    [TestMethod]
    public void RotationHandle_KeepsItsDistanceFromTheShapeAsTheDisplayScales()
    {
        // The reach is a distance the user's hand covers, so on a 200% display it has to
        // be twice as many capture pixels to look the same and be as easy to grab. Left
        // in frame pixels it would sit half as far off the shape, and the tether drawn to
        // it would halve with it.
        var rectangle = Shape(AnnotationTool.Rectangle, 10, 10, 60, 40);

        var reach = 10 - AnnotationHandles.For(rectangle, 2)
            .Single(handle => handle.Kind == AnnotationHandleKind.Rotate).Position.Y;

        Assert.AreEqual(AnnotationHandles.RotateReach * 2, reach, 1e-9);
    }

    [TestMethod]
    public void GrabbingAHandle_IsNoHarderOnAScaledDisplay()
    {
        // Ten layout units of slack, which on a 200% display is twenty capture pixels.
        // Unscaled, the same-looking handle would take twice the accuracy to hit.
        var rectangle = Shape(AnnotationTool.Rectangle, 100, 100, 300, 200);
        var nearTheCorner = new CapturePoint(115, 100);

        Assert.IsNull(
            AnnotationHandles.At(rectangle, nearTheCorner),
            "fifteen pixels out is beyond the ten a 100% display allows");
        Assert.AreEqual(
            AnnotationHandleKind.TopLeft,
            AnnotationHandles.At(rectangle, nearTheCorner, 2)?.Kind);
    }

    [TestMethod]
    public void BentMark_OffersAGripPerAnchorAndNoLongerOffersItsBend()
    {
        // Anchors and a bend describe the same shape, and once a mark has anchors only the
        // anchors are drawn. A bend grip left on offer would follow the pointer and change
        // nothing on screen, which reads as a broken handle rather than as an unused one.
        var arrow = Bent(AnnotationTool.Arrow, new CapturePoint(30, 20), new CapturePoint(60, -10));

        var handles = AnnotationHandles.For(arrow);

        CollectionAssert.AreEquivalent(
            new[]
            {
                AnnotationHandleKind.Start,
                AnnotationHandleKind.End,
                AnnotationHandleKind.Waypoint,
                AnnotationHandleKind.Waypoint,
            },
            handles.Select(handle => handle.Kind).ToArray());

        CollectionAssert.AreEqual(
            new[] { new CapturePoint(30, 20), new CapturePoint(60, -10) },
            handles.Where(handle => handle.Kind == AnnotationHandleKind.Waypoint)
                .OrderBy(handle => handle.Index)
                .Select(handle => handle.Position)
                .ToArray());
    }

    [TestMethod]
    public void DraggingAnAnchor_MovesThatOneAndLeavesTheOthersWhereTheyAre()
    {
        // The grips are told apart only by index, so a drag that ignored it would move
        // whichever anchor happened to come first however carefully the user aimed.
        var arrow = Bent(AnnotationTool.Arrow, new CapturePoint(30, 20), new CapturePoint(60, -10));

        var dragged = AnnotationHandles.Drag(
            arrow,
            AnnotationHandleKind.Waypoint,
            new CapturePoint(35, 90),
            EditorModifiers.None,
            1);

        CollectionAssert.AreEqual(
            new[] { new CapturePoint(30, 20), new CapturePoint(35, 90) },
            dragged.Waypoints.ToArray());
    }

    [TestMethod]
    public void DraggingAnAnchorThatIsNotThere_LeavesTheMarkAlone()
    {
        // A drag can outlive the shape it started on — undo while the button is down. The
        // index would be out of range, and losing the mark over it would be worse than the
        // drag doing nothing.
        var arrow = Bent(AnnotationTool.Arrow, new CapturePoint(30, 20));

        var dragged = AnnotationHandles.Drag(
            arrow,
            AnnotationHandleKind.Waypoint,
            new CapturePoint(35, 90),
            EditorModifiers.None,
            4);

        Assert.AreEqual(arrow, dragged);
    }

    [TestMethod]
    public void DraggingARulersAnchor_DropsTheReadingItNoLongerMatches()
    {
        // A ruler's length runs through its anchors, so moving one changes the number the
        // sprite already claims — the same reason moving an end drops it.
        var ruler = Bent(AnnotationTool.Measure, new CapturePoint(50, 10))
            with { Sprite = new AnnotationSprite(2, 2, new byte[2 * 2 * 4]) };

        var dragged = AnnotationHandles.Drag(
            ruler,
            AnnotationHandleKind.Waypoint,
            new CapturePoint(50, 60),
            EditorModifiers.None,
            0);

        Assert.IsNull(dragged.Sprite);
    }

    [TestMethod]
    public void Differ_SeesAnAnchorMoveAsAnEdit()
    {
        // This is what decides whether a released drag is committed and becomes an undo
        // step. Blind to the anchors, a whole reshape would be silently thrown away on
        // mouse-up.
        var arrow = Bent(AnnotationTool.Arrow, new CapturePoint(30, 20));
        var moved = arrow with { Waypoints = new[] { new CapturePoint(30, 21) } };

        Assert.IsTrue(AnnotationHandles.Differ(arrow, moved));
        Assert.IsFalse(AnnotationHandles.Differ(arrow, arrow with { Waypoints = new[] { new CapturePoint(30, 20) } }));
    }

    [TestMethod]
    public void SelectionOutline_ReachesTheAnchorsAMarkIsBentThrough()
    {
        // The outline is drawn from the bounding rectangle, so a mark bent well clear of
        // its ends would otherwise be framed by a box that misses most of it.
        var line = Bent(AnnotationTool.Line, new CapturePoint(50, 80));

        var outline = AnnotationHandles.Outline(line);

        Assert.AreEqual(80, outline.Max(corner => corner.Y), 1e-9);
    }

    private static Annotation Shape(AnnotationTool tool, double startX, double startY, double endX, double endY) =>
        Annotation.Create(tool, new CapturePoint(startX, startY), new CapturePoint(endX, endY));

    /// <summary>A mark from (10,10) to (110,10) bent through the given anchors.</summary>
    private static Annotation Bent(AnnotationTool tool, params CapturePoint[] anchors) =>
        Shape(tool, 10, 10, 110, 10) with { Waypoints = anchors };

    private static CapturePoint HandleAt(Annotation annotation, AnnotationHandleKind kind) =>
        AnnotationHandles.For(annotation).Single(handle => handle.Kind == kind).Position;
}
