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
}
