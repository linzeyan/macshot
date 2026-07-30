using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Annotations;

/// <summary>
/// Which swatch a flick of the pointer picks off the right-click wheel.
/// </summary>
[TestClass]
public sealed class ColorWheelTests
{
    private static readonly CapturePoint Center = new(500, 400);

    [TestMethod]
    public void TheWheelIsTwelveHuesAndFourNeutrals()
    {
        Assert.AreEqual(16, ColorWheel.Colors.Count);
        // Red, but not the pure one: the wheel is drawn over a photograph, and hues at
        // full saturation are hard to tell apart against one.
        Assert.AreEqual(new AnnotationColor(255, 38, 38), ColorWheel.Colors[0], "the first hue is red");
        Assert.AreEqual(new AnnotationColor(255, 255, 255), ColorWheel.Colors[12]);
        Assert.AreEqual(new AnnotationColor(0, 0, 0), ColorWheel.Colors[15]);
    }

    [TestMethod]
    public void TheFirstSwatchIsDirectlyBelowThePointer()
    {
        // Below in the sense the user sees, which is a larger y — the macOS original is
        // written the other way up and the port has to end at the same place on screen.
        var first = ColorWheel.SwatchAt(Center, 0);

        Assert.AreEqual(Center.X, first.X, 0.001);
        Assert.AreEqual(Center.Y + ColorWheel.Radius, first.Y, 0.001);
    }

    [TestMethod]
    public void EverySwatchIsPickedByPointingAtIt()
    {
        for (var index = 0; index < ColorWheel.Colors.Count; index++)
        {
            var swatch = ColorWheel.SwatchAt(Center, index);

            Assert.AreEqual(index, ColorWheel.IndexAt(Center, swatch), $"swatch {index}");
        }
    }

    [TestMethod]
    public void PointingPastTheRingStillPicks()
    {
        // Distance decides whether anything is picked at all; past that it is the angle
        // that answers. A wheel that had to be stopped at the right radius would be a
        // target rather than a gesture.
        var far = ColorWheel.SwatchAt(Center, 4);
        var further = new CapturePoint(
            Center.X + ((far.X - Center.X) * 3),
            Center.Y + ((far.Y - Center.Y) * 3));

        Assert.AreEqual(4, ColorWheel.IndexAt(Center, further));
    }

    [TestMethod]
    public void TheMiddleOfTheWheelPicksNothing()
    {
        // A right-click that opens the wheel and goes nowhere has to leave the colour
        // alone, or every stray right-click would repaint the next mark.
        Assert.AreEqual(-1, ColorWheel.IndexAt(Center, Center));
        Assert.IsNull(ColorWheel.ColorAt(-1));
        Assert.IsNull(ColorWheel.ColorAt(ColorWheel.Colors.Count));
    }
}
