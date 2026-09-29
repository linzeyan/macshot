using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class RecordedRegionFrameTests
{
    private static readonly CaptureRegion Display = new(0, 0, 1920, 1080);

    private static readonly double[] Scales = [1, 1.25, 1.5, 1.75, 2];

    /// <summary>
    /// The frame exists to say what is being recorded without being recorded itself. The
    /// one it replaced drew 10 pixels inside the region at 175%, and every recording on
    /// every Windows carried a pale strip across its top. Fractional edges are the case
    /// that would slip a bar one pixel in, whichever way the crop rounds them.
    /// </summary>
    [TestMethod]
    public void NoBarReachesIntoTheRegionHoweverItsEdgesRound()
    {
        var region = new CaptureRegion(100.4, 200.6, 300.3, 150.5);
        var covered = new CaptureRegion(100, 200, 301, 152);

        foreach (var scale in Scales)
        {
            foreach (var (x, y, width, height) in RecordedRegionFrame.Edges(region, Display, scale))
            {
                Assert.IsTrue(
                    new CaptureRegion(x, y, width, height).Intersect(covered).IsEmpty,
                    $"a bar at {x},{y} {width}x{height} overlaps the region at {scale}");
            }
        }
    }

    /// <summary>
    /// A frame with a gap on one side, or with its corners missing, reads as something
    /// other than the edge of the recording.
    /// </summary>
    [TestMethod]
    public void TheBarsCloseRightRoundTheRegionCornersIncluded()
    {
        var region = new CaptureRegion(500, 300, 202, 150);
        var edges = RecordedRegionFrame.Edges(region, Display, 1);

        bool Covered(int px, int py) =>
            edges.Any(edge => px >= edge.X && px < edge.X + edge.Width && py >= edge.Y && py < edge.Y + edge.Height);

        Assert.AreEqual(4, edges.Count);

        for (var x = 499; x <= 702; x++)
        {
            Assert.IsTrue(Covered(x, 299), $"above, at x {x}");
            Assert.IsTrue(Covered(x, 450), $"below, at x {x}");
        }

        for (var y = 299; y <= 450; y++)
        {
            Assert.IsTrue(Covered(499, y), $"left, at y {y}");
            Assert.IsTrue(Covered(702, y), $"right, at y {y}");
        }
    }

    /// <summary>
    /// macOS draws the frame in a window the size of the screen, so a stroke laid outside
    /// the whole screen is nowhere. Here it would be a window on whichever monitor is next
    /// door — a frame round nothing, on a display that is not being recorded.
    /// </summary>
    [TestMethod]
    public void AWholeDisplayRecordingHasNoFrame()
    {
        Assert.AreEqual(0, RecordedRegionFrame.Edges(Display, Display, 1.5).Count);

        var second = new CaptureRegion(1920, 0, 2560, 1440);
        Assert.AreEqual(0, RecordedRegionFrame.Edges(second, second, 1).Count);
    }

    /// <summary>
    /// A region against an edge keeps the three sides that are on its own display, and
    /// the side that would have crossed onto the neighbour's is dropped rather than drawn
    /// there.
    /// </summary>
    [TestMethod]
    public void ARegionAgainstAnEdgeKeepsOnlyTheSidesOnItsOwnDisplay()
    {
        var second = new CaptureRegion(1920, 0, 2560, 1440);
        var region = new CaptureRegion(1920, 400, 300, 200);

        var edges = RecordedRegionFrame.Edges(region, second, 2);

        Assert.AreEqual(3, edges.Count);
        Assert.IsTrue(edges.All(edge => edge.X >= 1920), "a bar crossed onto the display to the left");
    }
}
