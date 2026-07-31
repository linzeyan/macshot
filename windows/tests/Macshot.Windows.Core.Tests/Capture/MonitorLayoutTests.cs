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
    public void FrameToPointer_ReturnsAnnotationsToTheDisplayTheyWereDrawnOn()
    {
        // An annotation is stored once, in frame space, but is drawn on whichever
        // overlay it sits on. If the return trip did not use that display's scale,
        // a mark drawn on the 200% monitor would render at half its size and in the
        // wrong place, so this is a round trip rather than a fixed expectation.
        var layout = MixedDpiLayout();

        foreach (var monitor in layout.Monitors)
        {
            var frame = layout.PointerToFrame(monitor, 320, 180);
            var pointer = layout.FrameToPointer(monitor, frame);

            Assert.AreEqual(new CapturePoint(320, 180), pointer, $"round trip on {monitor.DeviceName}");
        }
    }

    [TestMethod]
    public void FrameToPointer_MapsTheSameFramePointDifferentlyPerDisplay()
    {
        // Guards against an implementation that ignores the monitor argument and
        // still passes the round trip above by cancelling its own error out.
        var layout = MixedDpiLayout();
        var highDpi = layout.Monitors[1];

        Assert.AreEqual(new CapturePoint(100, 100), layout.FrameToPointer(layout.Monitors[0], new CapturePoint(100, 100)));
        Assert.AreEqual(new CapturePoint(50, 50), layout.FrameToPointer(highDpi, new CapturePoint(2020, 100)));
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

    [TestMethod]
    public void ARegionRoundTripsBackToTheDesktopItWasChosenOn()
    {
        // What a recording and a scroll capture both need: the overlay chose the region
        // in frame space, and a display or a window only knows where it is on the
        // desktop. A layout with a display left of the primary is the case that catches
        // a missing offset, because frame space starts there and virtual space does not.
        var layout = new MonitorLayout(
        [
            new CaptureMonitor("left", new CaptureRegion(-1920, 0, 1920, 1080), 1),
            new CaptureMonitor("primary", new CaptureRegion(0, 0, 1920, 1080), 1, IsPrimary: true),
        ]);
        var chosen = new CaptureRegion(-1600, 200, 400, 300);

        var roundTripped = layout.FrameToVirtual(layout.VirtualToFrame(chosen));

        Assert.AreEqual(chosen, roundTripped);
    }

    [TestMethod]
    public void ARegionOnADisplay_IsOffsetToThatDisplaysOwnPixels()
    {
        // A capture item is one display, so a crop of it starts at the display's corner
        // rather than at the virtual desktop's — which for a display right of the
        // primary is 1920 pixels away.
        var monitor = new CaptureMonitor("right", new CaptureRegion(1920, 0, 1920, 1080), 1);

        var local = monitor.VirtualToLocal(new CaptureRegion(2020, 100, 400, 300));

        Assert.AreEqual(new CaptureRegion(100, 100, 400, 300), local);
    }

    [TestMethod]
    public void ARegionHangingOffTheDisplay_IsClippedToIt()
    {
        // The caller is about to index a buffer with it. A rectangle that overhangs the
        // display would read past the end of the frame.
        var monitor = new CaptureMonitor("only", new CaptureRegion(0, 0, 1920, 1080), 1);

        Assert.AreEqual(
            new CaptureRegion(1820, 980, 100, 100),
            monitor.VirtualToLocal(new CaptureRegion(1820, 980, 400, 300)));
        Assert.IsTrue(
            monitor.VirtualToLocal(new CaptureRegion(4000, 0, 100, 100)).IsEmpty,
            "a region on another display is not on this one at all");
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
