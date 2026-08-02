using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Annotations;

[TestClass]
public sealed class AnnotationSnappingTests
{
    private static readonly CaptureRegion Region = new(0, 0, 200, 100);

    [TestMethod]
    public void ForMove_LinesUpWithAnotherMarksEdge()
    {
        var there = Rect(50, 10, 20, 20);
        var moved = new CaptureRegion(53, 60, 20, 20);

        var snap = AnnotationSnapping.ForMove(moved, Region, [there]);

        Assert.AreEqual(-3, snap.Dx, 1e-9);
        Assert.AreEqual(50, snap.GuideX ?? double.NaN, 1e-9);

        // Nothing vertical was near, and a guide drawn there would be a line pointing at
        // nothing.
        Assert.AreEqual(0, snap.Dy, 1e-9);
        Assert.IsNull(snap.GuideY);
    }

    [TestMethod]
    public void ForMove_LeavesAMarkThatIsNotNearAnythingWhereItIs()
    {
        var there = Rect(50, 10, 20, 20);
        var moved = new CaptureRegion(85, 62, 6, 6);

        var snap = AnnotationSnapping.ForMove(moved, Region, [there]);

        Assert.AreEqual(SnapResult.None, snap, "a mark six pixels off is a mark placed six pixels off");
    }

    [TestMethod]
    public void ForMove_LinesUpWithTheRegionsOwnCentre()
    {
        // Nothing else on the capture: a mark centred in the picture is as deliberate as
        // one centred under another mark, so the region has to be a target by itself.
        var moved = new CaptureRegion(98, 20, 0, 0);

        var snap = AnnotationSnapping.ForMove(moved, Region, []);

        Assert.AreEqual(2, snap.Dx, 1e-9);
        Assert.AreEqual(100, snap.GuideX ?? double.NaN, 1e-9);
    }

    [TestMethod]
    public void ForMove_TakesTheNearerOfTwoTargets()
    {
        var near = Rect(60, 10, 10, 10);
        var far = Rect(64, 10, 10, 10);

        var snap = AnnotationSnapping.ForMove(new CaptureRegion(61, 60, 10, 10), Region, [near, far]);

        Assert.AreEqual(-1, snap.Dx, 1e-9);
        Assert.AreEqual(60, snap.GuideX ?? double.NaN, 1e-9);
    }

    [TestMethod]
    public void ForMove_IgnoresAMarkWithNoExtent()
    {
        // A click that placed nothing is a point, and a point is not an edge anything can
        // be lined up against — it would pull marks towards a mark that is not visible.
        var point = Rect(50, 50, 0, 0);

        // Two pixels from the point's X, and clear of everything the region offers.
        var snap = AnnotationSnapping.ForMove(new CaptureRegion(52, 72, 10, 10), Region, [point]);

        Assert.AreEqual(SnapResult.None, snap);
    }

    [TestMethod]
    public void Editor_SnapsAMarkBeingDrawnToTheOneBesideIt()
    {
        var editor = Drawn(out var first);

        editor.Tool = AnnotationTool.Rectangle;
        editor.PointerPressed(new CapturePoint(10, 60));
        editor.PointerMoved(new CapturePoint(72, 80));

        Assert.AreEqual(
            first.BoundingRect.Right,
            editor.Draft?.End.X ?? double.NaN,
            1e-9,
            "the second rectangle must end where the first one does");

        Assert.AreEqual(first.BoundingRect.Right, editor.Snap.GuideX ?? double.NaN, 1e-9);
    }

    [TestMethod]
    public void Editor_DoesNotSnapWhenTheUserHoldsShift()
    {
        var editor = Drawn(out _);

        editor.Tool = AnnotationTool.Rectangle;
        editor.PointerPressed(new CapturePoint(10, 60), EditorModifiers.Constrain);
        editor.PointerMoved(new CapturePoint(72, 80), EditorModifiers.Constrain);

        // Shift already means "the exact shape I asked for" — here a square, whose corner
        // is 62 across and 62 down from the press. A nudge afterwards would take that
        // away again, so Shift is also the way out when a mark belongs three pixels from
        // another one.
        Assert.AreEqual(72, editor.Draft?.End.X ?? double.NaN, 1e-9);
        Assert.AreEqual(SnapResult.None, editor.Snap);
    }

    [TestMethod]
    public void Editor_DoesNotSnapWhenTheSettingIsOff()
    {
        var editor = Drawn(out _);
        editor.SnapGuides = false;

        editor.Tool = AnnotationTool.Rectangle;
        editor.PointerPressed(new CapturePoint(10, 60));
        editor.PointerMoved(new CapturePoint(72, 80));

        Assert.AreEqual(72, editor.Draft?.End.X ?? double.NaN, 1e-9);
        Assert.AreEqual(SnapResult.None, editor.Snap);
    }

    [TestMethod]
    public void Editor_DoesNotLineAMarkUpWithWhereItUsedToBe()
    {
        var editor = Drawn(out var first);

        // Dragged three pixels: within the threshold of its own old edges, and it must
        // move all three rather than be held where it was.
        editor.Tool = AnnotationTool.Select;
        editor.PointerPressed(new CapturePoint(20, 20));
        editor.PointerReleased(new CapturePoint(20, 20));
        editor.PointerPressed(new CapturePoint(20, 20));
        editor.PointerMoved(new CapturePoint(23, 23));

        Assert.AreEqual(
            first.BoundingRect.X + 3,
            editor.Draft?.BoundingRect.X ?? double.NaN,
            1e-9);
    }

    [TestMethod]
    public void Editor_ForgetsTheGuideOnceTheGestureEnds()
    {
        var editor = Drawn(out _);

        editor.Tool = AnnotationTool.Rectangle;
        editor.PointerPressed(new CapturePoint(10, 60));
        editor.PointerMoved(new CapturePoint(72, 80));
        editor.PointerReleased(new CapturePoint(72, 80));

        // A guide left on screen is a line the user has to work out the meaning of.
        Assert.AreEqual(SnapResult.None, editor.Snap);
    }

    /// <summary>An editor with one rectangle on it, from (20,20) to (70,40).</summary>
    private static AnnotationEditor Drawn(out Annotation first)
    {
        var editor = new AnnotationEditor(new AnnotationDocument())
        {
            Tool = AnnotationTool.Rectangle,
            SnapRegion = Region,
        };

        editor.PointerPressed(new CapturePoint(20, 20));
        editor.PointerMoved(new CapturePoint(70, 40));
        editor.PointerReleased(new CapturePoint(70, 40));

        first = editor.Document.Annotations.Single();
        return editor;
    }

    private static Annotation Rect(double x, double y, double width, double height) =>
        Annotation.Create(
            AnnotationTool.Rectangle,
            new CapturePoint(x, y),
            new CapturePoint(x + width, y + height),
            AnnotationStyle.Default);
}
