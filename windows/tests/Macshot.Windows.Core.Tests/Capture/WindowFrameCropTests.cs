using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class WindowFrameCropTests
{
    [TestMethod]
    public void Resolve_TakesTheInvisibleResizeBorderOff()
    {
        // A typical top-level window: seven pixels of border on the left, right and
        // bottom, none along the top, which is where DWM leaves it.
        var windowRect = new CaptureRegion(93, 100, 814, 607);
        var visible = new CaptureRegion(100, 100, 800, 600);

        var crop = WindowFrameCrop.Resolve(windowRect, visible, 814, 607);

        Assert.AreEqual(new CaptureRegion(7, 0, 800, 600), crop);
    }

    [TestMethod]
    public void Resolve_KeepsTheWholeFrameWhenItIsNotTheSizeOfTheWindow()
    {
        var windowRect = new CaptureRegion(93, 100, 814, 607);
        var visible = new CaptureRegion(100, 100, 800, 600);

        // The frame is not what the window rectangle describes, so the offset
        // between the two rectangles says nothing about where the border sits in it.
        // Cropping on that would cut into the window; the border is the smaller harm.
        var crop = WindowFrameCrop.Resolve(windowRect, visible, 1628, 1214);

        Assert.AreEqual(new CaptureRegion(0, 0, 1628, 1214), crop);
    }

    [TestMethod]
    public void Resolve_KeepsTheWholeFrameWhenWindowsReportsNoRectangle()
    {
        var visible = new CaptureRegion(100, 100, 800, 600);

        var crop = WindowFrameCrop.Resolve(default, visible, 800, 600);

        Assert.AreEqual(new CaptureRegion(0, 0, 800, 600), crop);
    }

    [TestMethod]
    public void Resolve_KeepsTheWholeFrameWhenTheVisibleBoundsFallOutsideIt()
    {
        var windowRect = new CaptureRegion(0, 0, 800, 600);

        // Two rectangles measured a moment apart while the window moved. Their
        // difference is movement rather than border, and it points off the frame.
        var visible = new CaptureRegion(900, 900, 800, 600);

        var crop = WindowFrameCrop.Resolve(windowRect, visible, 800, 600);

        Assert.AreEqual(new CaptureRegion(0, 0, 800, 600), crop);
    }

    [TestMethod]
    public void Resolve_ClipsAVisibleRegionThatRunsPastTheFrame()
    {
        var windowRect = new CaptureRegion(0, 0, 800, 600);
        var visible = new CaptureRegion(10, 10, 800, 600);

        var crop = WindowFrameCrop.Resolve(windowRect, visible, 800, 600);

        // Never past the end of the buffer: the crop that follows reads real pixels.
        Assert.AreEqual(new CaptureRegion(10, 10, 790, 590), crop);
    }
}
