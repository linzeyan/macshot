using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class MonitorLayoutTests
{
    [TestMethod]
    public void PointerToFrame_UsesTheScaleOfTheDisplayThePointerIsOn()
    {
        // The whole reason overlays are per display: one window spanning a 100% and
        // a 200% monitor has a single rasterization scale, so half of its pointer
        // input would land on the wrong pixels.
        var layout = MixedDpiLayout();
        var standard = layout.Monitors[0];
        var highDpi = layout.Monitors[1];

        Assert.AreEqual(new CapturePoint(100, 100), layout.PointerToFrame(standard, 100, 100));
        Assert.AreEqual(new CapturePoint(2120, 200), layout.PointerToFrame(highDpi, 100, 100));
    }

    [TestMethod]
    public void PointerToFrame_KeepsFrameCoordinatesNonNegativeForDisplaysLeftOfPrimary()
    {
        // Virtual space puts the primary at the origin, so a display to its left has
        // negative coordinates. A captured buffer has no negative indices, so the
        // frame origin must shift to the virtual desktop's top-left corner.
        var left = new CaptureMonitor("left", new CaptureRegion(-1920, 0, 1920, 1080), 1);
        var primary = new CaptureMonitor("primary", new CaptureRegion(0, 0, 2560, 1440), 1.25, IsPrimary: true);
        var layout = new MonitorLayout([left, primary]);

        Assert.AreEqual(new CapturePoint(10, 10), layout.PointerToFrame(left, 10, 10));
        Assert.AreEqual(new CapturePoint(2045, 125), layout.PointerToFrame(primary, 100, 100));
    }

    [TestMethod]
    public void VirtualBounds_CoversEveryDisplay()
    {
        var layout = MixedDpiLayout();

        Assert.AreEqual(new CaptureRegion(0, 0, 5760, 2160), layout.VirtualBounds);
    }

    [TestMethod]
    public void FrameRegionOf_LocatesADisplayInsideAVirtualDesktopCapture()
    {
        var layout = MixedDpiLayout();

        Assert.AreEqual(new CaptureRegion(1920, 0, 3840, 2160), layout.FrameRegionOf(layout.Monitors[1]));
    }

    [TestMethod]
    public void MonitorAt_DoesNotLetAdjacentDisplaysBothClaimTheSharedEdge()
    {
        var layout = MixedDpiLayout();

        Assert.AreEqual("standard", layout.MonitorAt(new CapturePoint(1919, 0))?.DeviceName);
        Assert.AreEqual("highdpi", layout.MonitorAt(new CapturePoint(1920, 0))?.DeviceName);
    }

    [TestMethod]
    public void MonitorAt_ReturnsNullOffTheDesktop()
    {
        Assert.IsNull(MixedDpiLayout().MonitorAt(new CapturePoint(10_000, 10_000)));
    }

    [TestMethod]
    public void Primary_FallsBackToTheFirstDisplayWhenWindowsReportsNoPrimary()
    {
        var layout = MixedDpiLayout();

        Assert.AreEqual("standard", layout.Primary.DeviceName);
    }

    [TestMethod]
    public void Constructor_RejectsAnEmptyDisplaySet()
    {
        Assert.ThrowsException<ArgumentException>(() => new MonitorLayout([]));
    }

    [TestMethod]
    public void Constructor_RejectsANonPositiveScale()
    {
        // A zero scale would divide by zero when sizing the overlay window, and a
        // negative one would mirror every pointer mapping.
        var broken = new CaptureMonitor("broken", new CaptureRegion(0, 0, 800, 600), 0);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new MonitorLayout([broken]));
    }

    [TestMethod]
    public void PointerRoundTrip_ReturnsTheOriginalPosition()
    {
        var monitor = new CaptureMonitor("highdpi", new CaptureRegion(1920, 0, 3840, 2160), 2);

        var roundTripped = monitor.VirtualToPointer(monitor.PointerToVirtual(640, 360));

        Assert.AreEqual(640, roundTripped.X, 1e-9);
        Assert.AreEqual(360, roundTripped.Y, 1e-9);
    }

    [TestMethod]
    public void DipSize_IsWhatTheOverlayWindowMustBeSizedTo()
    {
        var monitor = new CaptureMonitor("highdpi", new CaptureRegion(1920, 0, 3840, 2160), 2);

        Assert.AreEqual(1920, monitor.DipWidth, 1e-9);
        Assert.AreEqual(1080, monitor.DipHeight, 1e-9);
    }

    /// <summary>A 1920x1080 display at 100% with a 4K display at 200% to its right.</summary>
    private static MonitorLayout MixedDpiLayout()
    {
        return new MonitorLayout(
        [
            new CaptureMonitor("standard", new CaptureRegion(0, 0, 1920, 1080), 1),
            new CaptureMonitor("highdpi", new CaptureRegion(1920, 0, 3840, 2160), 2),
        ]);
    }
}
