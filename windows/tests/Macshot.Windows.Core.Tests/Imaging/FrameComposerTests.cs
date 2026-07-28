using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class FrameComposerTests
{
    [TestMethod]
    public void Draw_PutsEachDisplayWhereItsBoundsSay()
    {
        var primary = new CaptureMonitor("primary", new CaptureRegion(0, 0, 8, 4), 1, IsPrimary: true);
        var secondary = new CaptureMonitor("secondary", new CaptureRegion(8, 0, 6, 4), 1);
        var composer = new FrameComposer(new MonitorLayout([primary, secondary]));

        composer.Draw(primary, 8, 4, Filled(8, 4, 10));
        composer.Draw(secondary, 6, 4, Filled(6, 4, 200));

        var image = composer.ToImage();
        Assert.AreEqual(14, composer.Width);
        Assert.AreEqual(10, BlueAt(image, composer.Width, 7, 3), "the primary owns everything left of the seam");
        Assert.AreEqual(200, BlueAt(image, composer.Width, 8, 3), "the secondary starts exactly at the seam");
    }

    [TestMethod]
    public void Draw_PlacesADisplayThatSitsLeftOfThePrimary()
    {
        // Virtual-screen coordinates go negative when a display is arranged left of
        // or above the primary. Composing straight from those coordinates would index
        // outside the buffer; the frame origin is what removes the sign.
        var primary = new CaptureMonitor("primary", new CaptureRegion(0, 0, 6, 4), 1, IsPrimary: true);
        var left = new CaptureMonitor("left", new CaptureRegion(-6, 0, 6, 4), 1);
        var composer = new FrameComposer(new MonitorLayout([primary, left]));

        composer.Draw(left, 6, 4, Filled(6, 4, 90));
        composer.Draw(primary, 6, 4, Filled(6, 4, 30));

        var image = composer.ToImage();
        Assert.AreEqual(-6, composer.VirtualX);
        Assert.AreEqual(90, BlueAt(image, composer.Width, 0, 0), "the left display belongs at the frame's origin");
        Assert.AreEqual(30, BlueAt(image, composer.Width, 6, 0));
    }

    [TestMethod]
    public void Constructor_LeavesAreaNoDisplayCoversOpaqueBlack()
    {
        // Two displays offset diagonally leave a corner nothing covers. It has to be
        // a colour, not whatever the allocation held, and it has to be opaque or the
        // preview would show it as a hole.
        var primary = new CaptureMonitor("primary", new CaptureRegion(0, 0, 4, 4), 1, IsPrimary: true);
        var lower = new CaptureMonitor("lower", new CaptureRegion(4, 4, 4, 4), 1);
        var composer = new FrameComposer(new MonitorLayout([primary, lower]));

        composer.Draw(primary, 4, 4, Filled(4, 4, 120));
        composer.Draw(lower, 4, 4, Filled(4, 4, 120));

        var image = composer.ToImage();
        var corner = 5 * 4;
        Assert.AreEqual(0, image[corner], "the uncovered corner must be black");
        Assert.AreEqual(byte.MaxValue, image[corner + 3], "and opaque, or it previews as a hole");
    }

    [TestMethod]
    public void Draw_ClipsACaptureLargerThanTheDisplayItReported()
    {
        // The reported bounds and the captured size can disagree by a pixel through
        // rounding on a scaled display. Losing the whole screenshot over that would
        // be a worse answer than an edge left at the background colour.
        var only = new CaptureMonitor("only", new CaptureRegion(0, 0, 4, 4), 1, IsPrimary: true);
        var composer = new FrameComposer(new MonitorLayout([only]));

        composer.Draw(only, 6, 6, Filled(6, 6, 77));

        Assert.AreEqual(77, BlueAt(composer.ToImage(), composer.Width, 3, 3));
    }

    [TestMethod]
    public void Draw_RejectsAPixelBufferThatDoesNotMatchItsDimensions()
    {
        var only = new CaptureMonitor("only", new CaptureRegion(0, 0, 4, 4), 1, IsPrimary: true);
        var composer = new FrameComposer(new MonitorLayout([only]));

        Assert.ThrowsException<ArgumentException>(() => composer.Draw(only, 4, 4, new byte[16]));
    }

    private static byte[] Filled(int width, int height, byte blue)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = blue;
            pixels[index + 3] = byte.MaxValue;
        }

        return pixels;
    }

    private static byte BlueAt(byte[] image, int width, int x, int y) => image[((y * width) + x) * 4];
}
