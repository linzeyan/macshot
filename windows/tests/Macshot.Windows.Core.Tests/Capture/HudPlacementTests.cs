using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

/// <summary>
/// Where the recording panel lands around the region being recorded.
/// </summary>
[TestClass]
public sealed class HudPlacementTests
{
    private static readonly CaptureRegion WorkArea = new(0, 0, 1920, 1040);
    private static readonly CaptureRegion Size = new(0, 0, 164, 32);

    [TestMethod]
    public void ItSitsAboveTheRegionAndAgainstItsRightEdge()
    {
        var hud = HudPlacement.For(new CaptureRegion(600, 400, 800, 500), WorkArea, Size);

        Assert.AreEqual(400 - HudPlacement.Gap - 32, hud.Y);
        Assert.AreEqual(1400 - 164, hud.X);
    }

    [TestMethod]
    public void ARegionAgainstTheTopPutsThePanelUnderIt()
    {
        var hud = HudPlacement.For(new CaptureRegion(600, 0, 800, 500), WorkArea, Size);

        Assert.AreEqual(500 + HudPlacement.Gap, hud.Y);
    }

    [TestMethod]
    public void ARegionFillingTheScreenLeavesThePanelInsideTheWorkArea()
    {
        // Neither above nor below fits, and a panel behind the taskbar cannot be stopped.
        var hud = HudPlacement.For(WorkArea, WorkArea, Size);

        Assert.AreEqual(1040 - 32, hud.Y);
        Assert.AreEqual(1920 - 164, hud.X);
    }

    [TestMethod]
    public void ARegionAgainstTheLeftEdgeDoesNotPushThePanelOffScreen()
    {
        var hud = HudPlacement.For(new CaptureRegion(0, 400, 100, 200), WorkArea, Size);

        Assert.AreEqual(0, hud.X, "the region is narrower than the panel");
    }

    [TestMethod]
    public void ItFollowsADisplayLeftOfThePrimary()
    {
        var left = new CaptureRegion(-1920, 0, 1920, 1040);

        var hud = HudPlacement.For(new CaptureRegion(-1500, 400, 800, 200), left, Size);

        Assert.AreEqual(-700 - 164, hud.X);
        Assert.AreEqual(400 - HudPlacement.Gap - 32, hud.Y);
    }
}
