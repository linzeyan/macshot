using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

/// <summary>
/// Where the width × height box lands around the selection.
/// </summary>
/// <remarks>
/// The screen is 1920x1080 at the origin and the box is 150x24, close to the real one:
/// two number fields, a × between them, and a presets button hanging off the right.
/// </remarks>
[TestClass]
public sealed class ResolutionBoxPlacementTests
{
    private static readonly CaptureRegion Screen = new(0, 0, 1920, 1080);
    private static readonly CaptureRegion Size = new(0, 0, 150, 24);

    [TestMethod]
    public void TheBoxSitsAboveTheSelectionWhenThereIsRoom()
    {
        var box = ResolutionBoxPlacement.For(new CaptureRegion(700, 400, 400, 300), Screen, Size);

        Assert.AreEqual(400 - 24 - ResolutionBoxPlacement.EdgeGap, box.Y);
        Assert.AreEqual(700 + ((400 - 150) / 2d), box.X, "centred on the selection");
    }

    [TestMethod]
    public void ASelectionAgainstTheTopPutsTheBoxBelowIt()
    {
        var box = ResolutionBoxPlacement.For(new CaptureRegion(700, 0, 400, 300), Screen, Size);

        Assert.AreEqual(300 + ResolutionBoxPlacement.EdgeGap, box.Y);
    }

    [TestMethod]
    public void TheNumbersAreCentredOnTheSelectionRatherThanTheWholeBox()
    {
        // The presets button hangs off the right. Centring the whole box would leave the
        // numbers visibly off to one side of the region they are describing.
        var box = ResolutionBoxPlacement.For(
            new CaptureRegion(700, 400, 400, 300),
            Screen,
            Size,
            dimensionsCenter: 60);

        Assert.AreEqual(700 + 200 - 60, box.X);
    }

    [TestMethod]
    public void TheBoxStaysOnTheScreenBesideASelectionAtTheEdge()
    {
        var box = ResolutionBoxPlacement.For(new CaptureRegion(1850, 400, 70, 300), Screen, Size);

        Assert.IsTrue(box.Right <= Screen.Right, $"ran off the right at {box.Right}");
        Assert.IsTrue(box.X >= Screen.X, $"ran off the left at {box.X}");
    }

    [TestMethod]
    public void TheBoxMovesOutOfTheToolbarsWay()
    {
        // The tools sit under the selection, so the space below is taken and the box
        // belongs above — where it would have gone anyway. Put something above it too and
        // it has to find somewhere else entirely.
        var selection = new CaptureRegion(700, 400, 400, 300);
        var above = new CaptureRegion(700, 340, 400, 60);
        var below = new CaptureRegion(700, 700, 400, 60);

        var box = ResolutionBoxPlacement.For(selection, Screen, Size, [above, below]);

        Assert.IsTrue(
            box.Intersect(above).IsEmpty && box.Intersect(below).IsEmpty,
            $"the box at {box.Y} is still under one of them");

        Assert.IsTrue(
            box.Y >= selection.Y && box.Bottom <= selection.Bottom,
            "with both edges taken it goes inside the selection");
    }

    [TestMethod]
    public void ABoxWithNowhereClearTakesTheLeastCoveredPlace()
    {
        // Half a box behind the toolbar still says more than a box off the edge of the
        // screen. The selection is too short to hold it, so inside is not an option.
        var selection = new CaptureRegion(700, 400, 400, 20);
        var wholeScreen = new[] { new CaptureRegion(0, 0, 1920, 1080) };

        var box = ResolutionBoxPlacement.For(selection, Screen, Size, wholeScreen);

        Assert.IsTrue(box.Y >= Screen.Y && box.Bottom <= Screen.Bottom, $"off the screen at {box.Y}");
    }

    [TestMethod]
    public void ASelectionTallerThanTheScreenStillGetsABoxOnIt()
    {
        var box = ResolutionBoxPlacement.For(new CaptureRegion(700, -200, 400, 1400), Screen, Size);

        Assert.IsTrue(box.Y >= Screen.Y && box.Bottom <= Screen.Bottom, $"off the screen at {box.Y}");
    }
}
