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
