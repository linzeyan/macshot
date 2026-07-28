using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class WindowSnapperTests
{
    private static readonly CaptureRegion Desktop = new(0, 0, 1920, 1080);

    [TestMethod]
    public void Snap_PrefersTheFrontWindowOverTheOneItCovers()
    {
        var front = Window(1, 100, 100, 400, 300);
        var behind = Window(2, 0, 0, 800, 600);

        var snapped = WindowSnapper.Snap([front, behind], new CapturePoint(200, 200), Desktop);

        // The one the user can see, not the larger one underneath: z-order is the
        // only evidence of which window a click at that point meant.
        Assert.AreEqual(front, snapped);
    }

    [TestMethod]
    public void Snap_FallsThroughToTheWindowBelowWhenTheFrontOneMissesThePoint()
    {
        var front = Window(1, 100, 100, 400, 300);
        var behind = Window(2, 0, 0, 800, 600);

        var snapped = WindowSnapper.Snap([front, behind], new CapturePoint(50, 50), Desktop);

        Assert.AreEqual(behind, snapped);
    }

    [TestMethod]
    public void Snap_ClipsAWindowHangingOffTheDesktop()
    {
        var window = Window(7, -200, 500, 600, 400);

        var snapped = WindowSnapper.Snap([window], new CapturePoint(100, 600), Desktop);

        // Selecting pixels that were never captured would crop past the end of the
        // frame, so what is offered is the part that is actually there.
        Assert.AreEqual(new CaptureRegion(0, 500, 400, 400), snapped?.Bounds);
    }

    [TestMethod]
    public void Snap_KeepsTheWindowIdentityThroughTheClip()
    {
        var window = Window(7, -200, 500, 600, 400);

        var snapped = WindowSnapper.Snap([window], new CapturePoint(100, 600), Desktop);

        // Clipping answers where to draw the highlight. Capturing the window itself
        // has to know which window it was, and hanging off the edge of the desktop
        // does not make it a different one.
        Assert.AreEqual(7, snapped?.Id);
    }

    [TestMethod]
    public void Snap_SkipsASliverInFrontOfARealWindow()
    {
        var sliver = Window(1, -1910, 200, 1920, 300);
        var window = Window(2, 0, 0, 800, 600);

        var snapped = WindowSnapper.Snap([sliver, window], new CapturePoint(5, 300), Desktop);

        // The sliver leaves ten visible pixels, which is not something anyone aimed
        // at; it must not shadow the window the user can see behind it.
        Assert.AreEqual(window, snapped);
    }

    [TestMethod]
    public void Snap_ReturnsNothingOverBareDesktop()
    {
        var window = Window(1, 100, 100, 400, 300);

        Assert.IsNull(WindowSnapper.Snap([window], new CapturePoint(1000, 900), Desktop));
    }

    [TestMethod]
    public void Snap_TreatsTheFarEdgesAsOutsideTheWindow()
    {
        var window = Window(1, 100, 100, 400, 300);

        // Half-open containment, matching CaptureRegion: a point on the far edge
        // belongs to whatever is beyond it, not to this window.
        Assert.IsNull(WindowSnapper.Snap([window], new CapturePoint(500, 200), Desktop));
        Assert.AreEqual(window, WindowSnapper.Snap([window], new CapturePoint(100, 100), Desktop));
    }

    private static CaptureWindow Window(long id, double x, double y, double width, double height) =>
        new(id, new CaptureRegion(x, y, width, height));
}
