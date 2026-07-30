using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Annotations;

[TestClass]
public sealed class StrokeSmoothingTests
{
    [TestMethod]
    public void Smoothing_KeepsBothEndsExactlyWhereTheyWere()
    {
        // The ends are the one thing the user aimed. A stroke that started somewhere
        // other than where the pointer went down would be smoothing away the aim.
        var stroke = Path((0, 0), (10, 0), (10, 10), (20, 10));

        var smoothed = StrokeSmoothing.Smooth(stroke);

        Assert.AreEqual(stroke[0], smoothed[0]);
        Assert.AreEqual(stroke[^1], smoothed[^1]);
    }

    [TestMethod]
    public void Smoothing_PullsACornerInTowardsTheCurve()
    {
        // A right angle at (10,0) becomes a bend: nothing in the result may sit on the
        // corner itself.
        var stroke = Path((0, 0), (10, 0), (10, 10));

        var smoothed = StrokeSmoothing.Smooth(stroke);

        Assert.IsFalse(
            smoothed.Any(point => point.X == 10 && point.Y == 0),
            "the corner itself must have been cut away");
    }

    [TestMethod]
    public void Smoothing_StaysInsideTheBoundsOfWhatWasDrawn()
    {
        // Corner cutting only ever interpolates, so a smoothed stroke cannot wander
        // outside the box the hand drew in — which is what keeps it on the pixels it was
        // drawn over.
        var stroke = Path((0, 0), (10, 0), (10, 10), (0, 10));

        var smoothed = StrokeSmoothing.Smooth(stroke);

        Assert.IsTrue(smoothed.All(point => point.X is >= 0 and <= 10 && point.Y is >= 0 and <= 10));
    }

    [TestMethod]
    public void AStraightLine_IsLeftAlone()
    {
        // Two points have no corner to cut, and a pass over them would pull both ends in.
        var stroke = Path((0, 0), (40, 40));

        var smoothed = StrokeSmoothing.Smooth(stroke);

        CollectionAssert.AreEqual(stroke.ToArray(), smoothed.ToArray());
    }

    [TestMethod]
    public void ADot_IsLeftAlone()
    {
        var stroke = Path((5, 5));

        Assert.AreEqual(1, StrokeSmoothing.Smooth(stroke).Count);
    }

    [TestMethod]
    public void ZeroPasses_ChangeNothing()
    {
        var stroke = Path((0, 0), (10, 0), (10, 10));

        CollectionAssert.AreEqual(stroke.ToArray(), StrokeSmoothing.Smooth(stroke, 0).ToArray());
    }

    [TestMethod]
    public void APencilStroke_IsSmoothedWhenItIsCommittedRatherThanWhileItIsDrawn()
    {
        // Rounding a path the user is still adding to would move ink already laid down,
        // which reads as the stroke lagging behind the pointer.
        var editor = new AnnotationEditor(new AnnotationDocument()) { Tool = AnnotationTool.Pencil };

        editor.PointerPressed(new CapturePoint(0, 0));
        editor.PointerMoved(new CapturePoint(10, 0));
        editor.PointerMoved(new CapturePoint(10, 10));
        Assert.AreEqual(3, editor.Draft?.Points.Count, "the live draft is the raw path");

        editor.PointerReleased(new CapturePoint(10, 10));

        var committed = editor.Document.Annotations[0];
        Assert.IsTrue(committed.Points.Count > 3, "the committed stroke is the smoothed one");
        Assert.AreEqual(new CapturePoint(0, 0), committed.Start);
        Assert.AreEqual(new CapturePoint(10, 10), committed.End);
    }

    [TestMethod]
    public void SmoothingOff_CommitsThePathAsItWasDrawn()
    {
        var editor = new AnnotationEditor(new AnnotationDocument())
        {
            Tool = AnnotationTool.Pencil,
            SmoothStrokes = false,
        };

        editor.PointerPressed(new CapturePoint(0, 0));
        editor.PointerMoved(new CapturePoint(10, 0));
        editor.PointerMoved(new CapturePoint(10, 10));
        editor.PointerReleased(new CapturePoint(10, 10));

        // Four samples, not three: releasing feeds the pointer in one last time, so the
        // final position is recorded twice. Harmless for a path stamped as discs, and the
        // point here is that the corner at (10,0) is still a corner.
        var committed = editor.Document.Annotations[0];
        Assert.AreEqual(4, committed.Points.Count);
        Assert.IsTrue(committed.Points.Any(point => point.X == 10 && point.Y == 0));
    }

    [TestMethod]
    public void SmoothingIsOn_UnlessTheUserTurnsItOff()
    {
        // A freehand stroke that looks drawn is what a first-time user expects; tracing
        // a shape point for point is the specialised case, so it is the one behind a
        // setting.
        Assert.IsTrue(CaptureSettings.Default.SmoothPencilStrokes);
    }

    private static IReadOnlyList<CapturePoint> Path(params (double X, double Y)[] points) =>
        [.. points.Select(point => new CapturePoint(point.X, point.Y))];
}
