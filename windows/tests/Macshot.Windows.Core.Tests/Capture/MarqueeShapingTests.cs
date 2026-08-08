using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class MarqueeShapingTests
{
    [TestMethod]
    public void Corner_LeavesAFreeformDragExactlyWhereThePointerIs()
    {
        // Nothing held is the common case, and the drag has to land on the pixel the
        // pointer is over rather than on a rounded version of it.
        var moving = new CapturePoint(133, 71);

        var shaped = MarqueeShaping.Corner(new CapturePoint(10, 10), moving, null, square: false);

        Assert.AreEqual(moving.X, shaped.X, 1e-9);
        Assert.AreEqual(moving.Y, shaped.Y, 1e-9);
    }

    [TestMethod]
    public void Corner_SquaresFromTheShorterSideSoThePointerStaysOnTheCorner()
    {
        // Taken from the longer side the region would cover pixels the pointer never
        // passed over, which reads as the selection running away from the hand.
        var shaped = MarqueeShaping.Corner(
            new CapturePoint(10, 10),
            new CapturePoint(210, 60),
            aspect: null,
            square: true);

        Assert.AreEqual(60, shaped.X, 1e-9);
        Assert.AreEqual(60, shaped.Y, 1e-9);
    }

    [TestMethod]
    public void Corner_SquaresInWhicheverDirectionTheDragWent()
    {
        // A drag up and to the left is as ordinary as one down and to the right, and
        // squaring it must not flip it back through the anchor.
        var shaped = MarqueeShaping.Corner(
            new CapturePoint(300, 300),
            new CapturePoint(100, 250),
            aspect: null,
            square: true);

        Assert.AreEqual(250, shaped.X, 1e-9);
        Assert.AreEqual(250, shaped.Y, 1e-9);
    }

    [TestMethod]
    public void Corner_HoldsALockedRatioFromTheFirstDrag()
    {
        // The shape is chosen in the menu to outlast the capture, so the drag that starts
        // the next one is already that shape — not a freeform region that has to be
        // corrected by dragging a grip afterwards.
        var shaped = MarqueeShaping.Corner(
            new CapturePoint(0, 0),
            new CapturePoint(400, 400),
            aspect: 16d / 9d,
            square: false);

        Assert.AreEqual(400, shaped.X, 1e-9);
        Assert.AreEqual(225, shaped.Y, 1e-9);
    }

    [TestMethod]
    public void Corner_PrefersTheLockedRatioOverShift()
    {
        // The ratio was chosen deliberately and is meant to persist; Shift is held during
        // one drag. Letting the key win would silently throw the choice away.
        var shaped = MarqueeShaping.Corner(
            new CapturePoint(0, 0),
            new CapturePoint(400, 400),
            aspect: 2,
            square: true);

        Assert.AreEqual(400, shaped.X, 1e-9);
        Assert.AreEqual(200, shaped.Y, 1e-9);
    }

    [TestMethod]
    public void Corner_IgnoresARatioThatIsNotOne()
    {
        // Zero is how "no shape held" is stored in the settings file, and a ratio of zero
        // would divide the height away to nothing.
        var moving = new CapturePoint(90, 40);

        var shaped = MarqueeShaping.Corner(new CapturePoint(0, 0), moving, aspect: 0, square: false);

        Assert.AreEqual(moving.X, shaped.X, 1e-9);
        Assert.AreEqual(moving.Y, shaped.Y, 1e-9);
    }

    [TestMethod]
    public void FixedRegion_PutsTheChosenSizeUnderThePointerRatherThanBesideIt()
    {
        // The size came from a menu, so the press only says where. Anchored at a corner
        // instead, the user would be aiming a box whose middle is nowhere near the pointer.
        var region = MarqueeShaping.FixedRegion(
            new CapturePoint(960, 540), 1920, 1080, new CaptureRegion(0, 0, 3840, 2160));

        Assert.AreEqual(1920, region.Width, 1e-9);
        Assert.AreEqual(1080, region.Height, 1e-9);
        Assert.AreEqual(0, region.X, 1e-9);
        Assert.AreEqual(0, region.Y, 1e-9);
    }

    [TestMethod]
    public void FixedRegion_KeepsTheBoxOnTheDisplayWhenThePointerNearsAnEdge()
    {
        // Half of a preset hanging off the screen is half a capture: there are no pixels
        // out there to take. It slides in rather than being cropped, so the size the box
        // reports is still the size that is delivered.
        var screen = new CaptureRegion(0, 0, 1920, 1080);

        var region = MarqueeShaping.FixedRegion(new CapturePoint(1918, 2), 800, 600, screen);

        Assert.AreEqual(800, region.Width, 1e-9);
        Assert.AreEqual(600, region.Height, 1e-9);
        Assert.AreEqual(screen.Right - 800, region.X, 1e-9);
        Assert.AreEqual(0, region.Y, 1e-9);
    }

    [TestMethod]
    public void FixedRegion_TakesTheDisplaysOriginRatherThanAssumingZero()
    {
        // Regions are in the whole desktop's coordinates, so a second display starts
        // somewhere other than the origin. Clamping against zero would drag every box on
        // it back onto the primary.
        var second = new CaptureRegion(1920, 0, 1920, 1080);

        var region = MarqueeShaping.FixedRegion(new CapturePoint(1921, 540), 800, 600, second);

        Assert.AreEqual(second.X, region.X, 1e-9);
        Assert.AreEqual(240, region.Y, 1e-9);
    }

    [TestMethod]
    public void FixedRegion_ShrinksAPresetTallerThanTheDisplayInsteadOfCroppingIt()
    {
        // 1080 × 1920 on a 1080p screen. Cropped, the box would have edges off the screen
        // that cannot be seen or dragged; scaled, it keeps its shape and the size box says
        // honestly what it came to.
        var screen = new CaptureRegion(0, 0, 1920, 1080);

        var region = MarqueeShaping.FixedRegion(new CapturePoint(960, 540), 1080, 1920, screen);

        Assert.AreEqual(1080, region.Height, 1e-9);
        Assert.AreEqual(607.5, region.Width, 1e-9);
        Assert.IsTrue(region.Right <= screen.Right && region.Bottom <= screen.Bottom);
    }
}
