using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class BoundarySnappingTests
{
    private const double Radius = 4;

    [TestMethod]
    public void Build_RefusesAPictureWithNothingBetweenItsPixels()
    {
        // One pixel across has no seam in it, and an index of no seams would still cost
        // the caller a scan on every pointer move.
        Assert.IsNull(BoundarySnapIndex.Build(new byte[4], 1, 1, 0, 0));
    }

    [TestMethod]
    public void NearestVertical_FindsTheEdgeInThePicture()
    {
        var index = Striped(width: 40, height: 40, edgeAt: 20);

        var hit = index.NearestVertical(23, 0, 40, Radius);

        Assert.IsNotNull(hit);
        Assert.AreEqual(20, hit.Value.Position, 1e-9);
    }

    [TestMethod]
    public void NearestVertical_IgnoresAnEdgeTooFarToBeAimedAt()
    {
        var index = Striped(width: 40, height: 40, edgeAt: 20);

        Assert.IsNull(index.NearestVertical(26, 0, 40, Radius));
    }

    [TestMethod]
    public void NearestVertical_IgnoresAnEdgeThatDoesNotRunAlongTheDraggedSide()
    {
        // The line covers a tenth of the height, and the edge being dragged spans all of
        // it: a stray high-contrast run is not a border, which is what the support
        // fraction is for.
        var index = Striped(width: 40, height: 40, edgeAt: 20, rows: 4);

        Assert.IsNull(index.NearestVertical(22, 0, 40, Radius));
    }

    [TestMethod]
    public void NearestVertical_SeesThatSameEdgeWhenOnlyThatPartIsBeingDragged()
    {
        var index = Striped(width: 40, height: 40, edgeAt: 20, rows: 4);

        var hit = index.NearestVertical(22, 0, 4, Radius);

        Assert.IsNotNull(hit);
        Assert.AreEqual(20, hit.Value.Position, 1e-9);
    }

    [TestMethod]
    public void NearestHorizontal_FindsTheEdgeInThePicture()
    {
        var index = Banded(width: 40, height: 40, edgeAt: 12);

        var hit = index.NearestHorizontal(14, 0, 40, Radius);

        Assert.IsNotNull(hit);
        Assert.AreEqual(12, hit.Value.Position, 1e-9);
    }

    [TestMethod]
    public void Positions_AreWhereTheCaptureSitsOnTheDesktop()
    {
        // A second display starts somewhere other than the origin, and an edge reported in
        // the capture's own pixels would snap the selection onto a line on the wrong
        // screen.
        var index = Striped(width: 40, height: 40, edgeAt: 20, originX: 1920, originY: 200);

        var hit = index.NearestVertical(1943, 200, 240, Radius);

        Assert.IsNotNull(hit);
        Assert.AreEqual(1940, hit.Value.Position, 1e-9);
    }

    [TestMethod]
    public void Resize_TakesOnlyTheEdgeTheGripDrags()
    {
        var index = Striped(width: 40, height: 40, edgeAt: 20);
        var region = new CaptureRegion(17, 0, 6, 40);

        var snap = BoundarySnapping.Resize(region, SelectionHandle.Left, index, Radius);

        Assert.AreEqual(20, snap.Region.X, 1e-9);
        Assert.AreEqual(20, snap.GuideX ?? double.NaN, 1e-9);

        // The right edge was as near the line as the left one, and moving it would resize
        // the selection from a side the user is not touching.
        Assert.AreEqual(23, snap.Region.Right, 1e-9);
    }

    [TestMethod]
    public void Resize_WillNotCloseTheSelectionUp()
    {
        var index = Striped(width: 40, height: 40, edgeAt: 20);

        // Dragged until its left edge is all but on top of its right one, with the line
        // beyond that: taking it would turn the selection inside out.
        var snap = BoundarySnapping.Resize(
            new CaptureRegion(19, 0, 0.5, 40), SelectionHandle.Left, index, Radius);

        Assert.AreEqual(19, snap.Region.X, 1e-9);
        Assert.IsNull(snap.GuideX);
    }

    [TestMethod]
    public void Move_ShiftsTheWholeSelectionWithoutResizingIt()
    {
        var index = Striped(width: 40, height: 40, edgeAt: 20);
        var region = new CaptureRegion(22, 0, 10, 40);

        var snap = BoundarySnapping.Move(region, index, Radius);

        Assert.AreEqual(20, snap.Region.X, 1e-9);
        Assert.AreEqual(region.Width, snap.Region.Width, 1e-9);
        Assert.AreEqual(20, snap.GuideX ?? double.NaN, 1e-9);
    }

    [TestMethod]
    public void Corner_LeavesTheAnchorWhereThePressLanded()
    {
        var index = Striped(width: 40, height: 40, edgeAt: 20);

        var snap = BoundarySnapping.Corner(
            new CapturePoint(5, 0), new CapturePoint(22, 40), index, Radius);

        Assert.AreEqual(5, snap.Region.X, 1e-9);
        Assert.AreEqual(20, snap.Region.Right, 1e-9);
    }

    [TestMethod]
    public void Nothing_HappensWithoutAnIndex()
    {
        // The index is built off the UI thread and only when the setting is on, so every
        // entry point has to work before it exists, and go on working when it never does.
        var region = new CaptureRegion(10, 10, 20, 20);

        Assert.AreEqual(region, BoundarySnapping.Move(region, null, Radius).Region);
        Assert.AreEqual(region, BoundarySnapping.Resize(region, SelectionHandle.Left, null, Radius).Region);
        Assert.IsNull(BoundarySnapping.Corner(
            new CapturePoint(0, 0), new CapturePoint(10, 10), null, Radius).GuideX);
    }

    /// <summary>
    /// The whole point of the command: a rectangle thrown roughly around a panel comes back
    /// sitting on it. The four edges here are 30 and 40 pixels out, an order of magnitude
    /// past the drag snap's four, which is why this cannot just call <see cref="Resize"/>.
    /// </summary>
    [TestMethod]
    public void Fit_TakesARoughSelectionOutToTheBoxItWasDraggedAround()
    {
        var index = Painted(400, 300, new CaptureRegion(100, 80, 200, 140));

        var fit = BoundarySnapping.Fit(new CaptureRegion(60, 50, 280, 200), index, scale: 1);

        Assert.AreEqual(SelectionFitOutcome.Adjusted, fit.Outcome);
        Assert.AreEqual(new CaptureRegion(100, 80, 200, 140), fit.Region);
    }

    /// <summary>
    /// "Already aligned" and "nothing found" are different answers to the user, so a
    /// selection sitting exactly on a border must not be reported as an empty picture.
    /// </summary>
    [TestMethod]
    public void Fit_SaysASelectionAlreadyOnTheBorderIsAligned()
    {
        var index = Painted(400, 300, new CaptureRegion(100, 80, 200, 140));
        var region = new CaptureRegion(100, 80, 200, 140);

        var fit = BoundarySnapping.Fit(region, index, scale: 1);

        Assert.AreEqual(SelectionFitOutcome.AlreadyAligned, fit.Outcome);
        Assert.AreEqual(region, fit.Region);
    }

    [TestMethod]
    public void Fit_SaysSoWhenThereWasNothingToAimAt()
    {
        // A flat picture: every seam in it is zero, so no edge has anything to move onto.
        var index = Painted(400, 300);
        var region = new CaptureRegion(100, 80, 200, 140);

        var fit = BoundarySnapping.Fit(region, index, scale: 1);

        Assert.AreEqual(SelectionFitOutcome.NothingNearby, fit.Outcome);
        Assert.AreEqual(region, fit.Region);
    }

    /// <summary>
    /// A border that stops short at its rounded corners is still that border. Scored over
    /// the whole side it would fail the support fraction, which is what the span inset is
    /// there to prevent — and without it the command would refuse every window on screen.
    /// </summary>
    [TestMethod]
    public void Fit_FindsAnEdgeThatBreaksOffBeforeTheCorners()
    {
        // The upright sides exist over the middle half of the selection's height only:
        // 50% of the full span, under the 55% a seam needs, but 71% of the inset one.
        var index = Painted(400, 300, new CaptureRegion(100, 100, 200, 100));

        var fit = BoundarySnapping.Fit(new CaptureRegion(60, 50, 280, 200), index, scale: 1);

        Assert.AreEqual(SelectionFitOutcome.Adjusted, fit.Outcome);
        Assert.AreEqual(100, fit.Region.X, 1e-9);
        Assert.AreEqual(300, fit.Region.Right, 1e-9);
    }

    /// <summary>
    /// Both edges of a narrow selection can find the same line, and taking both would leave
    /// nothing selected at all — an auto-adjust that can delete the selection is worse than
    /// one that occasionally does nothing.
    /// </summary>
    [TestMethod]
    public void Fit_WillNotCloseTheSelectionOntoOneLine()
    {
        var index = Painted(100, 40, new CaptureRegion(50, 0, 50, 40));
        var region = new CaptureRegion(40, 0, 20, 40);

        var fit = BoundarySnapping.Fit(region, index, scale: 1);

        Assert.AreEqual(region, fit.Region);
        Assert.AreEqual(SelectionFitOutcome.AlreadyAligned, fit.Outcome);
    }

    /// <summary>
    /// The reach is written in layout units, so the same border is the same distance away
    /// to the eye on any display. Left in pixels it would be half as far on a 200% screen,
    /// which is where most of this port runs.
    /// </summary>
    [TestMethod]
    public void Fit_ReachesAsFarOnScreenWhateverTheDisplayIsRunningAt()
    {
        var index = Painted(300, 100, new CaptureRegion(40, 0, 220, 100));

        // 60 pixels out from both upright borders: past the 48-unit floor at 100%, inside
        // it at 200%, and too small a fraction of its own width to raise the floor either way.
        var region = new CaptureRegion(100, 20, 100, 60);

        Assert.AreEqual(
            SelectionFitOutcome.NothingNearby,
            BoundarySnapping.Fit(region, index, scale: 1).Outcome);

        var retina = BoundarySnapping.Fit(region, index, scale: 2);

        Assert.AreEqual(SelectionFitOutcome.Adjusted, retina.Outcome);
        Assert.AreEqual(40, retina.Region.X, 1e-9);
        Assert.AreEqual(260, retina.Region.Right, 1e-9);
    }

    /// <summary>
    /// Nothing is said about a selection that is barely a selection: a few pixels across is
    /// a drag that misfired, and moving its edges would be guessing at what was meant.
    /// </summary>
    [TestMethod]
    public void Fit_LeavesASelectionTooSmallToHaveBeenAimed()
    {
        var index = Painted(400, 300, new CaptureRegion(100, 80, 200, 140));
        var region = new CaptureRegion(99, 79, 3, 3);

        var fit = BoundarySnapping.Fit(region, index, scale: 1);

        Assert.AreEqual(SelectionFitOutcome.TooSmall, fit.Outcome);
        Assert.AreEqual(region, fit.Region);
    }

    /// <summary>A black picture with white rectangles painted into it.</summary>
    private static BoundarySnapIndex Painted(int width, int height, params CaptureRegion[] shapes)
    {
        var pixels = new byte[width * height * 4];

        foreach (var shape in shapes)
        {
            for (var y = (int)shape.Y; y < (int)shape.Bottom; y++)
            {
                for (var x = (int)shape.X; x < (int)shape.Right; x++)
                {
                    var at = ((y * width) + x) * 4;
                    pixels[at] = 255;
                    pixels[at + 1] = 255;
                    pixels[at + 2] = 255;
                }
            }
        }

        return BoundarySnapIndex.Build(pixels, width, height, 0, 0)
            ?? throw new InvalidOperationException("the fixture must produce an index");
    }

    /// <summary>A picture that is black to the left of <paramref name="edgeAt"/> and white to its right.</summary>
    /// <param name="rows">How far down the picture that edge runs, all the way by default.</param>
    private static BoundarySnapIndex Striped(
        int width,
        int height,
        int edgeAt,
        int rows = int.MaxValue,
        int originX = 0,
        int originY = 0)
    {
        var pixels = new byte[width * height * 4];

        for (var y = 0; y < Math.Min(rows, height); y++)
        {
            for (var x = edgeAt; x < width; x++)
            {
                var at = ((y * width) + x) * 4;
                pixels[at] = 255;
                pixels[at + 1] = 255;
                pixels[at + 2] = 255;
            }
        }

        return BoundarySnapIndex.Build(pixels, width, height, originX, originY)
            ?? throw new InvalidOperationException("the fixture must produce an index");
    }

    /// <summary>The same picture turned on its side: dark above <paramref name="edgeAt"/>, light below.</summary>
    private static BoundarySnapIndex Banded(int width, int height, int edgeAt)
    {
        var pixels = new byte[width * height * 4];

        for (var y = edgeAt; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = ((y * width) + x) * 4;
                pixels[at] = 255;
                pixels[at + 1] = 255;
                pixels[at + 2] = 255;
            }
        }

        return BoundarySnapIndex.Build(pixels, width, height, 0, 0)
            ?? throw new InvalidOperationException("the fixture must produce an index");
    }
}
