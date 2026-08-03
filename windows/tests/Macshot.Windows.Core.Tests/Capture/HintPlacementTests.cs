using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

/// <summary>
/// Where the overlay's hint pill lands around the region it describes.
/// </summary>
/// <remarks>
/// The screen is 1920x1080 at the origin and the pill is 300x26, about what a sentence of
/// instructions measures at 12pt.
/// </remarks>
[TestClass]
public sealed class HintPlacementTests
{
    private static readonly CaptureRegion Screen = new(0, 0, 1920, 1080);
    private static readonly CaptureRegion Size = new(0, 0, 300, 26);

    /// <summary>
    /// The side matters rather than merely being deterministic: above belongs to the size
    /// box, which is drawn on a later layer and would cover anything put there.
    /// </summary>
    [TestMethod]
    public void ThePillSitsBelowTheRegionRatherThanAboveIt()
    {
        var pill = HintPlacement.For(new CaptureRegion(700, 400, 400, 300), Screen, Size);

        Assert.AreEqual(700 + HintPlacement.EdgeGap, pill.Y, "below the region's bottom edge");
        Assert.AreEqual(700 + ((400 - 300) / 2d), pill.X, "centred on the region");
    }

    /// <summary>
    /// A region against the bottom leaves no room underneath, and a pill off the screen
    /// says nothing at all.
    /// </summary>
    [TestMethod]
    public void ARegionAgainstTheBottomPutsThePillAboveIt()
    {
        var pill = HintPlacement.For(new CaptureRegion(700, 700, 400, 380), Screen, Size);

        Assert.AreEqual(700 - 26 - HintPlacement.EdgeGap, pill.Y);
    }

    /// <summary>
    /// The pill and the toolbar both hang off the bottom edge. Keeping the side and moving
    /// past what is on it reads as a line under the toolbar; changing sides would put it
    /// back under the size box, which is the bug this placement exists for.
    /// </summary>
    [TestMethod]
    public void ThePillSlidesPastTheToolbarInsteadOfChangingSides()
    {
        var toolbar = new CaptureRegion(700, 712, 400, 44);

        var pill = HintPlacement.For(
            new CaptureRegion(700, 400, 400, 300), Screen, Size, [toolbar]);

        Assert.IsTrue(pill.Y >= toolbar.Bottom, "clear of the toolbar, and still below the region");
    }

    /// <summary>
    /// The toolbar is two strips and an options row, so clearing the first can land the
    /// pill on the second. One pass would leave it covered by whichever came later.
    /// </summary>
    [TestMethod]
    public void ThePillClearsEveryStripAndNotOnlyTheFirstOneItHits()
    {
        CaptureRegion[] strips =
        [
            new(700, 712, 400, 44),
            new(700, 760, 400, 44),
            new(700, 808, 400, 30),
        ];

        var pill = HintPlacement.For(
            new CaptureRegion(700, 400, 400, 300), Screen, Size, strips);

        foreach (var strip in strips)
        {
            Assert.AreEqual(0, pill.Intersect(strip).Height * pill.Intersect(strip).Width);
        }
    }

    /// <summary>
    /// Being pushed clear must not push the pill off the screen — at that point it has to
    /// take the other side, covered or not.
    /// </summary>
    [TestMethod]
    public void APushThatWouldLeaveTheScreenTakesTheOtherSideInstead()
    {
        var toolbar = new CaptureRegion(700, 1000, 400, 76);

        var pill = HintPlacement.For(
            new CaptureRegion(700, 600, 400, 380), Screen, Size, [toolbar]);

        Assert.IsTrue(pill.Bottom <= Screen.Bottom, "on the screen");
        Assert.IsTrue(pill.Y < 600, "above the region");
    }

    /// <summary>
    /// A sentence about a region in the corner is wider than the room beside it, and half
    /// a sentence hanging off the screen is worse than one that is not quite centred.
    /// </summary>
    [TestMethod]
    public void AWideSentenceStaysOnTheScreenRatherThanCentredOnTheRegion()
    {
        var pill = HintPlacement.For(new CaptureRegion(0, 400, 60, 300), Screen, Size);

        Assert.IsTrue(pill.X >= Screen.X, "not off the left edge");
    }

    /// <summary>
    /// The size box is above and the pill below, so an empty rectangle for chrome that is
    /// not on screen must not be treated as something standing in the way.
    /// </summary>
    [TestMethod]
    public void ChromeThatIsNotOnScreenDoesNotMoveThePill()
    {
        var pill = HintPlacement.For(
            new CaptureRegion(700, 400, 400, 300), Screen, Size, [default]);

        Assert.AreEqual(700 + HintPlacement.EdgeGap, pill.Y);
    }
}
