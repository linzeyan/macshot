using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

/// <summary>
/// Where a pinned capture opens, and where the wheel leaves it.
/// </summary>
/// <remarks>
/// The work area is 1920 × 1080 with a 40-pixel taskbar along the bottom, and offset by
/// −1920 on one test so a display left of the primary is not assumed away.
/// </remarks>
[TestClass]
public sealed class PinPlacementTests
{
    private static readonly CaptureRegion WorkArea = new(0, 0, 1920, 1040);

    [TestMethod]
    public void ACaptureSmallEnoughToFitOpensAtItsOwnPixels()
    {
        var opening = PinPlacement.Opening(400, 300, WorkArea);

        Assert.AreEqual(400, opening.Width);
        Assert.AreEqual(300, opening.Height);
    }

    [TestMethod]
    public void ItOpensCentredOnTheWorkArea()
    {
        var opening = PinPlacement.Opening(400, 300, WorkArea);

        Assert.AreEqual((1920 - 400) / 2, opening.X);
        Assert.AreEqual((1040 - 300) / 2, opening.Y);
    }

    [TestMethod]
    public void ADisplayLeftOfThePrimaryCentresOnItsOwnPixelsRatherThanTheOrigin()
    {
        var opening = PinPlacement.Opening(400, 300, new CaptureRegion(-1920, 0, 1920, 1040));

        Assert.AreEqual(-1920 + ((1920 - 400) / 2), opening.X);
    }

    [TestMethod]
    public void AFullScreenCaptureIsCappedAtFourFifthsOfTheWorkArea()
    {
        // Pinned at 1:1 it would be a second desktop, indistinguishable from the first.
        var opening = PinPlacement.Opening(1920, 1040, WorkArea);

        Assert.AreEqual(Math.Round(1920 * PinPlacement.OpeningFraction), opening.Width);
        Assert.AreEqual(Math.Round(1040 * PinPlacement.OpeningFraction), opening.Height);
    }

    [TestMethod]
    public void TheCapKeepsTheShapeOfTheCapture()
    {
        // A tall capture is limited by the height, and the width must follow it down
        // rather than being capped separately, which would squash the picture.
        var opening = PinPlacement.Opening(600, 2000, WorkArea);

        Assert.AreEqual(Math.Round(1040 * PinPlacement.OpeningFraction), opening.Height);
        Assert.AreEqual(Math.Round(600 * (1040 * PinPlacement.OpeningFraction / 2000)), opening.Width);
    }

    [TestMethod]
    public void TheWheelScalesAboutThePixelUnderThePointer()
    {
        var opening = new CaptureRegion(100, 100, 400, 300);
        var cursor = new CapturePoint(200, 175);

        var zoomed = PinPlacement.Zoomed(opening, opening, 2, cursor);

        Assert.AreEqual(800, zoomed.Width);
        Assert.AreEqual(600, zoomed.Height);

        // The pointer was a quarter across and a quarter down; it still is.
        Assert.AreEqual(0.25, (cursor.X - zoomed.X) / zoomed.Width, 0.001);
        Assert.AreEqual(0.25, (cursor.Y - zoomed.Y) / zoomed.Height, 0.001);
    }

    [TestMethod]
    public void ItWillNotBeScaledPastTheLimits()
    {
        var opening = new CaptureRegion(0, 0, 400, 300);

        var enlarged = PinPlacement.Zoomed(opening, opening, 100, new CapturePoint(200, 150));
        var shrunk = PinPlacement.Zoomed(opening, opening, 0.001, new CapturePoint(200, 150));

        Assert.AreEqual(400 * PinPlacement.MaxScale, enlarged.Width);
        Assert.AreEqual(400 * PinPlacement.MinScale, shrunk.Width);
    }

    [TestMethod]
    public void AWheelNotchAtTheLimitChangesNothing()
    {
        // Not merely clamped to the same size: the anchoring would still move the
        // window, so a wheel held down against the limit would walk the pin off screen.
        var opening = new CaptureRegion(0, 0, 400, 300);
        var atLimit = PinPlacement.Zoomed(opening, opening, PinPlacement.MaxScale, new CapturePoint(0, 0));

        var again = PinPlacement.Zoomed(atLimit, opening, 1.03, new CapturePoint(50, 50));

        Assert.AreEqual(atLimit, again);
    }

    [TestMethod]
    public void APointerOffTheWindowStillScalesAboutAnEdge()
    {
        var opening = new CaptureRegion(100, 100, 400, 300);

        var zoomed = PinPlacement.Zoomed(opening, opening, 2, new CapturePoint(-500, -500));

        Assert.AreEqual(100, zoomed.X, "clamped to the left edge rather than thrown off screen");
        Assert.AreEqual(100, zoomed.Y);
    }

    [TestMethod]
    public void RestoringKeepsTheCentreItWasScaledAbout()
    {
        var opening = new CaptureRegion(100, 100, 400, 300);
        var zoomed = PinPlacement.Zoomed(opening, opening, 2, new CapturePoint(300, 250));

        var restored = PinPlacement.Restored(zoomed, opening);

        Assert.AreEqual(opening.Width, restored.Width);
        Assert.AreEqual(zoomed.X + (zoomed.Width / 2), restored.X + (restored.Width / 2), 1);
        Assert.AreEqual(zoomed.Y + (zoomed.Height / 2), restored.Y + (restored.Height / 2), 1);
    }

    [TestMethod]
    public void ThePercentIsTheScaleTheLabelShows()
    {
        var opening = new CaptureRegion(0, 0, 400, 300);

        Assert.AreEqual(100, PinPlacement.Percent(opening, opening));
        Assert.AreEqual(200, PinPlacement.Percent(opening with { Width = 800, Height = 600 }, opening));
        Assert.AreEqual(50, PinPlacement.Percent(opening with { Width = 200, Height = 150 }, opening));
    }
}
