using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

/// <summary>
/// What typing a size into the box, or picking one from the menu, does to the selection.
/// </summary>
[TestClass]
public sealed class SelectionSizingTests
{
    private static readonly CaptureRegion Screen = new(0, 0, 1920, 1080);

    [TestMethod]
    public void ATypedSizeGrowsAroundTheMiddleOfWhatIsAlreadyThere()
    {
        // The region under the pointer is what the user is looking at. Pinning a corner
        // would slide it out from under them as it grew.
        var resized = SelectionSizing.Resize(new CaptureRegion(800, 400, 200, 100), 400, 200, Screen);

        Assert.AreEqual(new CaptureRegion(700, 350, 400, 200), resized);
    }

    [TestMethod]
    public void ASizeThatWouldRunOffTheScreenIsPushedBackOn()
    {
        var resized = SelectionSizing.Resize(new CaptureRegion(1900, 1060, 10, 10), 200, 100, Screen);

        Assert.AreEqual(1920 - 200, resized.X);
        Assert.AreEqual(1080 - 100, resized.Y);
        Assert.AreEqual(200, resized.Width, "still the size that was asked for");
    }

    [TestMethod]
    public void ASizeBiggerThanTheScreenKeepsItsShape()
    {
        // A preset called 16 : 9 that came back as something else is the preset not doing
        // what its name says.
        var resized = SelectionSizing.Resize(new CaptureRegion(0, 0, 100, 100), 3840, 2160, Screen);

        Assert.AreEqual(1920, resized.Width);
        Assert.AreEqual(1080, resized.Height);
    }

    [TestMethod]
    public void AnImpossibleSizeLeavesTheSelectionAlone()
    {
        var selection = new CaptureRegion(10, 20, 30, 40);

        Assert.AreEqual(selection, SelectionSizing.Resize(selection, 0, 100, Screen));
        Assert.AreEqual(selection, SelectionSizing.Resize(selection, 100, -1, Screen));
    }

    [TestMethod]
    public void WithNoSelectionYetTheSizeLandsInTheMiddleOfTheScreen()
    {
        var resized = SelectionSizing.Resize(default, 400, 200, Screen);

        Assert.AreEqual(new CaptureRegion((1920 - 400) / 2d, (1080 - 200) / 2d, 400, 200), resized);
    }

    [TestMethod]
    public void ALockedRatioWorksOutTheNumberTheUserDidNotType()
    {
        // Typing a width is the whole reason to have locked the shape first. A lock that
        // only applied when both numbers were retyped would be a lock that does nothing.
        var byWidth = SelectionSizing.Resize(
            new CaptureRegion(0, 0, 100, 100),
            1600,
            999,
            Screen,
            16.0 / 9.0,
            SizedDimension.Width);

        Assert.AreEqual(1600, byWidth.Width);
        Assert.AreEqual(900, byWidth.Height);

        var byHeight = SelectionSizing.Resize(
            new CaptureRegion(0, 0, 100, 100),
            999,
            900,
            Screen,
            16.0 / 9.0,
            SizedDimension.Height);

        Assert.AreEqual(1600, byHeight.Width);
        Assert.AreEqual(900, byHeight.Height);
    }

    [TestMethod]
    public void RetypingBothNumbersIgnoresTheLock()
    {
        var resized = SelectionSizing.Resize(
            new CaptureRegion(0, 0, 100, 100),
            300,
            300,
            Screen,
            16.0 / 9.0,
            SizedDimension.Both);

        Assert.AreEqual(300, resized.Width);
        Assert.AreEqual(300, resized.Height);
    }

    [TestMethod]
    public void LockingARatioReshapesTheSelectionNow()
    {
        var reshaped = SelectionSizing.ApplyAspect(new CaptureRegion(800, 400, 320, 320), 16.0 / 9.0, Screen);

        Assert.AreEqual(320, reshaped.Width, "the width the user dragged out is kept");
        Assert.AreEqual(180, reshaped.Height);
        Assert.AreEqual(800 + 160, reshaped.X + (reshaped.Width / 2), "still centred where it was");
    }

    [TestMethod]
    public void ADraggedGripKeepsTheLockedShapeWithTheOppositeCornerStill()
    {
        // A lock that only applied to typed numbers would come apart the first time
        // anyone touched a grip, which is how the region is adjusted the rest of the time.
        var held = SelectionSizing.ConstrainToAspect(
            new CaptureRegion(100, 100, 320, 40),
            16.0 / 9.0,
            SelectionHandle.BottomRight,
            Screen);

        Assert.AreEqual(100, held.X, "the corner opposite the grip has not moved");
        Assert.AreEqual(100, held.Y);
        Assert.AreEqual(320, held.Width, "the axis dragged furthest drives");
        Assert.AreEqual(180, held.Height);
    }

    [TestMethod]
    public void AnEdgeGripDrivesItsOwnAxisAndCentresTheOther()
    {
        var held = SelectionSizing.ConstrainToAspect(
            new CaptureRegion(100, 100, 400, 200),
            1,
            SelectionHandle.Bottom,
            Screen);

        Assert.AreEqual(200, held.Width, "the height it was dragged to drives the width");
        Assert.AreEqual(200, held.Height);
        Assert.AreEqual(300, held.X + (held.Width / 2), "the derived width stays centred");
        Assert.AreEqual(100, held.Y, "the top edge, opposite the grip, has not moved");
    }

    [TestMethod]
    public void ALockedShapeSurvivesBeingDraggedOffTheScreen()
    {
        var held = SelectionSizing.ConstrainToAspect(
            new CaptureRegion(0, 0, 1920, 400),
            1,
            SelectionHandle.BottomRight,
            Screen);

        Assert.AreEqual(held.Width, held.Height, "still square");
        Assert.IsTrue(held.Bottom <= Screen.Bottom, $"ran off the bottom at {held.Bottom}");
    }

    [TestMethod]
    public void EveryRatioPresetIsAShapeAndEverySizePresetIsAMeasurement()
    {
        // The menu is split into two sections that do different things, and a preset in
        // the wrong one either resizes when it should reshape or the other way round.
        Assert.IsTrue(ResolutionPresets.Ratios.Skip(1).All(preset => preset.Aspect > 0 && !preset.IsExact));
        Assert.IsTrue(ResolutionPresets.Sizes.All(preset => preset.IsExact && preset.Aspect is null));
        Assert.AreEqual(ResolutionPresets.Freeform, ResolutionPresets.Ratios[0]);
        Assert.IsNull(ResolutionPresets.Freeform.Aspect, "freeform is the absence of a shape");
    }
}
