using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class WindowSnapperTests
{
    private static readonly CaptureRegion Desktop = new(0, 0, 1920, 1080);

    [TestMethod]
    public void Snap_PrefersTheFrontWindowOverTheOneItCovers()
    {
        var front = new CaptureRegion(100, 100, 400, 300);
        var behind = new CaptureRegion(0, 0, 800, 600);

        var snapped = WindowSnapper.Snap([front, behind], new CapturePoint(200, 200), Desktop);

        // The one the user can see, not the larger one underneath: z-order is the
        // only evidence of which window a click at that point meant.
        Assert.AreEqual(front, snapped);
    }

    [TestMethod]
    public void Snap_FallsThroughToTheWindowBelowWhenTheFrontOneMissesThePoint()
    {
        var front = new CaptureRegion(100, 100, 400, 300);
        var behind = new CaptureRegion(0, 0, 800, 600);

        var snapped = WindowSnapper.Snap([front, behind], new CapturePoint(50, 50), Desktop);

        Assert.AreEqual(behind, snapped);
    }

    [TestMethod]
    public void Snap_ClipsAWindowHangingOffTheDesktop()
    {
        var window = new CaptureRegion(-200, 500, 600, 400);

        var snapped = WindowSnapper.Snap([window], new CapturePoint(100, 600), Desktop);

        // Selecting pixels that were never captured would crop past the end of the
        // frame, so what is offered is the part that is actually there.
        Assert.AreEqual(new CaptureRegion(0, 500, 400, 400), snapped);
    }

    [TestMethod]
    public void Snap_SkipsASliverInFrontOfARealWindow()
    {
        var sliver = new CaptureRegion(-1910, 200, 1920, 300);
        var window = new CaptureRegion(0, 0, 800, 600);

        var snapped = WindowSnapper.Snap([sliver, window], new CapturePoint(5, 300), Desktop);

        // The sliver leaves ten visible pixels, which is not something anyone aimed
        // at; it must not shadow the window the user can see behind it.
        Assert.AreEqual(window, snapped);
    }

    [TestMethod]
    public void Snap_ReturnsNothingOverBareDesktop()
    {
        var window = new CaptureRegion(100, 100, 400, 300);

        Assert.IsNull(WindowSnapper.Snap([window], new CapturePoint(1000, 900), Desktop));
    }

    [TestMethod]
    public void Snap_TreatsTheFarEdgesAsOutsideTheWindow()
    {
        var window = new CaptureRegion(100, 100, 400, 300);

        // Half-open containment, matching CaptureRegion: a point on the far edge
        // belongs to whatever is beyond it, not to this window.
        Assert.IsNull(WindowSnapper.Snap([window], new CapturePoint(500, 200), Desktop));
        Assert.AreEqual(window, WindowSnapper.Snap([window], new CapturePoint(100, 100), Desktop));
    }
}
