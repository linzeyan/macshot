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
    public void NoSmoothing_ChangesNothing()
    {
        var stroke = Path((0, 0), (10, 0), (10, 10));

        CollectionAssert.AreEqual(
            stroke.ToArray(),
            StrokeSmoothing.Smooth(stroke, PencilSmoothing.None).ToArray());
    }

    [TestMethod]
    public void Refined_KeepsBothEndsExactlyWhereTheyWere()
    {
        // The averaging trails the pointer, so without the padding the stroke would stop
        // short of where the hand let go — by most of a window's worth of samples.
        var stroke = Wobble();

        var refined = StrokeSmoothing.Smooth(stroke, PencilSmoothing.Refined);

        Assert.AreEqual(stroke[0], refined[0]);
        Assert.AreEqual(stroke[^1], refined[^1]);
    }

    [TestMethod]
    public void Refined_TakesOutAShakeThatCuttingCornersLeavesIn()
    {
        // The reason the mode exists. Corner cutting shortens a tremor; it does not
        // remove it, because each cut is between two shaky samples. Measured as how far
        // the result strays from the straight line the hand was trying to draw.
        var stroke = Wobble();

        Assert.IsTrue(
            Wander(StrokeSmoothing.Smooth(stroke, PencilSmoothing.Refined))
                < Wander(StrokeSmoothing.Smooth(stroke, PencilSmoothing.Smooth)) / 2,
            "refined must be markedly straighter than smoothed, not merely different");
    }

    [TestMethod]
    public void Refined_StaysInsideTheBoundsOfWhatWasDrawn()
    {
        // Both stages only ever average points that were drawn, so neither can put ink
        // anywhere the hand did not go.
        var stroke = Path((0, 0), (10, 0), (10, 10), (0, 10), (0, 0));

        var refined = StrokeSmoothing.Smooth(stroke, PencilSmoothing.Refined);

        Assert.IsTrue(refined.All(point => point.X is >= 0 and <= 10 && point.Y is >= 0 and <= 10));
    }

    [TestMethod]
    public void Refined_LeavesAStrokeTooShortToAverageAlone()
    {
        var stroke = Path((0, 0), (40, 40));

        CollectionAssert.AreEqual(
            stroke.ToArray(),
            StrokeSmoothing.Smooth(stroke, PencilSmoothing.Refined).ToArray());
    }

    [TestMethod]
    public void APencilStrokeIsRefined_WhenThatIsWhatTheToolbarAsksFor()
    {
        var editor = new AnnotationEditor(new AnnotationDocument())
        {
            Tool = AnnotationTool.Pencil,
            Smoothing = PencilSmoothing.Refined,
        };

        editor.PointerPressed(new CapturePoint(0, 0));
        foreach (var point in Wobble().Skip(1))
        {
            editor.PointerMoved(point);
        }

        editor.PointerReleased(new CapturePoint(60, 0));

        var committed = editor.Document.Annotations[0];
        Assert.AreEqual(new CapturePoint(0, 0), committed.Start);
        Assert.AreEqual(new CapturePoint(60, 0), committed.End);
        Assert.IsTrue(
            Wander(committed.Points) < Wander(Wobble()) / 4,
            "the shake is gone from what was committed, not only from what Core returns");
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
            Smoothing = PencilSmoothing.None,
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
    public void SmoothingIsTheMiddleSetting_UnlessTheUserChangesIt()
    {
        // A freehand stroke that looks drawn is what a first-time user expects; tracing
        // a shape point for point is the specialised case, and so is a stroke averaged
        // far enough to leave the pixels it was drawn over.
        Assert.AreEqual(PencilSmoothing.Smooth, CaptureSettings.Default.PencilSmoothing);
    }

    private static IReadOnlyList<CapturePoint> Path(params (double X, double Y)[] points) =>
        [.. points.Select(point => new CapturePoint(point.X, point.Y))];

    /// <summary>
    /// A hand trying to draw a straight line and not managing it: sixty pixels of travel
    /// with a pixel and a half of shake on every other sample, and both ends on the line.
    /// </summary>
    private static IReadOnlyList<CapturePoint> Wobble() =>
        [.. Enumerable.Range(0, 31).Select(step => new CapturePoint(step * 2, step % 2 == 0 ? 0 : 1.5))];

    /// <summary>
    /// How much further the stroke travels than the straight line between its ends. It is
    /// what shake costs: a hand that wandered draws a longer path than the one it meant.
    /// </summary>
    private static double Wander(IReadOnlyList<CapturePoint> points)
    {
        double travelled = 0;
        for (var index = 1; index < points.Count; index++)
        {
            travelled += Math.Sqrt(
                Math.Pow(points[index].X - points[index - 1].X, 2)
                + Math.Pow(points[index].Y - points[index - 1].Y, 2));
        }

        return travelled - Math.Sqrt(
            Math.Pow(points[^1].X - points[0].X, 2)
            + Math.Pow(points[^1].Y - points[0].Y, 2));
    }
}
