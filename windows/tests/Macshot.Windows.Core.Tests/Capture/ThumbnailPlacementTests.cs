using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

/// <summary>
/// Where the panel after a capture goes, in whichever corner was asked for.
/// </summary>
/// <remarks>
/// A panel is only useful where it is out of the way, and which corner that is depends on
/// where the taskbar and the user's own windows are. The work area rather than the display
/// is what it is placed inside, so the taskbar never covers the buttons.
/// </remarks>
[TestClass]
public sealed class ThumbnailPlacementTests
{
    // A 1080p display with a 40-tall taskbar along the bottom.
    private static readonly CaptureRegion WorkArea = new(0, 0, 1920, 1040);

    [TestMethod]
    public void TheDefaultCornerIsTheOneWindowsPutsItsOwnNoticesIn()
    {
        var (x, y, width, height) = Place(ThumbnailCorner.BottomRight, 0);

        Assert.AreEqual(1920 - 240 - 16, x);
        Assert.AreEqual(1040 - 160 - 16, y);
        Assert.AreEqual(240, width);
        Assert.AreEqual(160, height);
    }

    [TestMethod]
    public void EachCornerIsItsOwnMarginIn()
    {
        Assert.AreEqual(16, Place(ThumbnailCorner.BottomLeft, 0).X);
        Assert.AreEqual(16, Place(ThumbnailCorner.TopLeft, 0).X);
        Assert.AreEqual(16, Place(ThumbnailCorner.TopLeft, 0).Y);
        Assert.AreEqual(16, Place(ThumbnailCorner.TopRight, 0).Y);
    }

    /// <summary>
    /// The column has to grow into the screen rather than off it, which means the
    /// direction depends on the corner.
    /// </summary>
    [TestMethod]
    public void TheColumnGrowsAwayFromTheEdgeItIsAgainst()
    {
        Assert.AreEqual(
            Place(ThumbnailCorner.BottomRight, 0).Y - 168,
            Place(ThumbnailCorner.BottomRight, 1).Y,
            "a bottom corner stacks upward");

        Assert.AreEqual(
            Place(ThumbnailCorner.TopRight, 0).Y + 168,
            Place(ThumbnailCorner.TopRight, 1).Y,
            "and a top corner downward");
    }

    /// <summary>
    /// The panel takes the preference and the margin does not. A margin that grew with it
    /// would push a double-size panel further off the screen it is tucked into.
    /// </summary>
    [TestMethod]
    public void ThePreviewSizeChangesThePanelAndNotItsMargin()
    {
        var (x, y, width, height) = ThumbnailPlacement.For(
            ThumbnailCorner.BottomRight,
            WorkArea,
            previewScale: 2,
            displayScale: 1,
            stackIndex: 0);

        Assert.AreEqual(480, width);
        Assert.AreEqual(320, height);
        Assert.AreEqual(1920 - 480 - 16, x);
        Assert.AreEqual(1040 - 320 - 16, y);
    }

    [TestMethod]
    public void EverythingTakesTheDisplaysOwnScale()
    {
        var (x, _, width, height) = ThumbnailPlacement.For(
            ThumbnailCorner.BottomLeft,
            WorkArea,
            previewScale: 1,
            displayScale: 2,
            stackIndex: 0);

        Assert.AreEqual(480, width);
        Assert.AreEqual(320, height);
        Assert.AreEqual(32, x, "including the margin, which is 16 layout units either way");
    }

    /// <summary>
    /// A preference read back from a file somebody edited is not a preference this can
    /// trust. Zero would be a panel with no pixels in it.
    /// </summary>
    [TestMethod]
    public void AnImpossiblePreviewSizeComesBackInsideTheSlidersRange()
    {
        Assert.AreEqual(ThumbnailPlacement.MinPreviewScale, ThumbnailPlacement.SanePreviewScale(0));
        Assert.AreEqual(ThumbnailPlacement.MaxPreviewScale, ThumbnailPlacement.SanePreviewScale(40));
        Assert.AreEqual(1, ThumbnailPlacement.SanePreviewScale(double.NaN));
    }

    private static (int X, int Y, int Width, int Height) Place(ThumbnailCorner corner, int stackIndex) =>
        ThumbnailPlacement.For(corner, WorkArea, previewScale: 1, displayScale: 1, stackIndex);

    /// <summary>
    /// A flick has to go out through the edge the panel is sitting against — flicking a
    /// bottom-left thumbnail to the right would push it across the desk it came from.
    /// </summary>
    [TestMethod]
    public void AFlickLeavesByTheNearestEdge()
    {
        Assert.AreEqual(1, ThumbnailPlacement.DismissDirection(ThumbnailCorner.BottomRight));
        Assert.AreEqual(1, ThumbnailPlacement.DismissDirection(ThumbnailCorner.TopRight));
        Assert.AreEqual(-1, ThumbnailPlacement.DismissDirection(ThumbnailCorner.BottomLeft));
        Assert.AreEqual(-1, ThumbnailPlacement.DismissDirection(ThumbnailCorner.TopLeft));
    }

    [TestMethod]
    public void TheThresholdIsASmallFlickAndNeverAWholeDrag()
    {
        // macshot's twentieth of the width, held between 8 and 16: 12 on the 240 panel,
        // and still reachable when the preview size has been turned right down.
        Assert.AreEqual(12, ThumbnailPlacement.DismissThreshold(240));
        Assert.AreEqual(8, ThumbnailPlacement.DismissThreshold(60));
        Assert.AreEqual(16, ThumbnailPlacement.DismissThreshold(2000));
    }

    [TestMethod]
    public void ThePanelIsStillVisibleAtTheMomentItCommits()
    {
        // The whole point of fading over 1.4 times the threshold rather than over the
        // threshold itself: there is something left to see being thrown away.
        Assert.AreEqual(1, ThumbnailPlacement.DismissOpacity(0, 240));
        Assert.IsTrue(ThumbnailPlacement.DismissOpacity(ThumbnailPlacement.DismissThreshold(240), 240) > 0.6);
        Assert.AreEqual(0.55, ThumbnailPlacement.DismissOpacity(1000, 240), 0.0001);
    }
}
