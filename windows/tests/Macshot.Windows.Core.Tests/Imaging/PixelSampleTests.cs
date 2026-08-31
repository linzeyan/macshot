using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class PixelSampleTests
{
    /// <summary>A 2x2 frame: red, green / blue, white, in BGRA order.</summary>
    private static byte[] Frame() =>
    [
        0, 0, 255, 255,
        0, 255, 0, 255,
        255, 0, 0, 255,
        255, 255, 255, 255,
    ];

    [TestMethod]
    public void Sample_ReadsTheChannelsInTheOrderTheFrameHoldsThem()
    {
        // BGRA, not RGBA. Reading it the other way round is the mistake that shows up
        // as a colour picker that swaps red and blue and nothing else.
        Assert.AreEqual(new AnnotationColor(255, 0, 0), PixelEffects.Sample(Frame(), 2, 2, 0, 0));
        Assert.AreEqual(new AnnotationColor(0, 255, 0), PixelEffects.Sample(Frame(), 2, 2, 1, 0));
        Assert.AreEqual(new AnnotationColor(0, 0, 255), PixelEffects.Sample(Frame(), 2, 2, 0, 1));
    }

    [TestMethod]
    public void Sample_IsAlwaysOpaque()
    {
        // A screenshot's alpha says nothing about what is on screen: BitBlt leaves it
        // at zero. Carrying it through would sample the whole desktop as invisible.
        var transparent = new byte[] { 10, 20, 30, 0 };

        Assert.AreEqual(byte.MaxValue, PixelEffects.Sample(transparent, 1, 1, 0, 0).Alpha);
    }

    [TestMethod]
    public void Sample_ClampsAPointOutsideTheFrame()
    {
        // The sampler follows the pointer, which goes past the edge of the frame as a
        // matter of course. The nearest pixel is a better answer than an exception.
        Assert.AreEqual(new AnnotationColor(255, 0, 0), PixelEffects.Sample(Frame(), 2, 2, -5, -5));
        Assert.AreEqual(new AnnotationColor(255, 255, 255), PixelEffects.Sample(Frame(), 2, 2, 99, 99));
    }

    [TestMethod]
    public void Sample_TakesTheColourOfAMarkDrawnOverTheCapture()
    {
        // What the sampler is for once anything has been drawn: giving a second arrow the
        // first one's colour. Reading the capture underneath would report the wall the
        // mark was drawn on and there would be no way to pick the mark's own colour at all.
        var rendered = new RenderedRegion(Marked(), 2, 2, new CaptureRegion(1, 1, 2, 2));

        Assert.AreEqual(
            new AnnotationColor(9, 8, 7),
            PixelEffects.Sample(Capture(), 4, 4, 1, 1, rendered));
    }

    [TestMethod]
    public void Sample_FallsBackToTheCaptureOutsideTheRenderedRegion()
    {
        // Marks live inside the selection, so outside it there is nothing composited to
        // read. Half-open, because the region's far edge belongs to what is beyond it.
        var rendered = new RenderedRegion(Marked(), 2, 2, new CaptureRegion(1, 1, 2, 2));

        Assert.AreEqual(
            new AnnotationColor(1, 2, 3),
            PixelEffects.Sample(Capture(), 4, 4, 0, 0, rendered),
            "before the region");
        Assert.AreEqual(
            new AnnotationColor(1, 2, 3),
            PixelEffects.Sample(Capture(), 4, 4, 3, 3, rendered),
            "on the edge past it");
    }

    [TestMethod]
    public void Sample_ScalesIntoARenderedRegionThatIsNotTheSizeOfWhatItCovers()
    {
        // A snapped window is previewed from the window's own pixels, which need not be
        // the size of the rectangle it occupies on the desktop. Subtracting the origin and
        // stopping there would read the top-left corner of it wherever the pointer went.
        var rendered = new RenderedRegion(Striped(), 4, 1, new CaptureRegion(0, 0, 2, 1));

        Assert.AreEqual(
            new AnnotationColor(0, 0, 0),
            PixelEffects.Sample(Capture(), 4, 4, 0, 0, rendered),
            "the left half reads the buffer's first pixel");
        Assert.AreEqual(
            new AnnotationColor(40, 40, 40),
            PixelEffects.Sample(Capture(), 4, 4, 1, 0, rendered),
            "the right half reads its third, not its second");
    }

    /// <summary>A 4x4 frame, every pixel (1, 2, 3).</summary>
    private static byte[] Capture()
    {
        var frame = new byte[4 * 4 * 4];
        for (var offset = 0; offset < frame.Length; offset += 4)
        {
            frame[offset] = 3;
            frame[offset + 1] = 2;
            frame[offset + 2] = 1;
            frame[offset + 3] = byte.MaxValue;
        }

        return frame;
    }

    /// <summary>A 2x2 buffer, every pixel (9, 8, 7) — the capture with a mark on it.</summary>
    private static byte[] Marked()
    {
        var frame = new byte[2 * 2 * 4];
        for (var offset = 0; offset < frame.Length; offset += 4)
        {
            frame[offset] = 7;
            frame[offset + 1] = 8;
            frame[offset + 2] = 9;
            frame[offset + 3] = byte.MaxValue;
        }

        return frame;
    }

    /// <summary>A 4x1 buffer whose columns are 0, 20, 40, 60 grey.</summary>
    private static byte[] Striped()
    {
        var frame = new byte[4 * 4];
        for (var column = 0; column < 4; column++)
        {
            var offset = column * 4;
            frame[offset] = (byte)(column * 20);
            frame[offset + 1] = (byte)(column * 20);
            frame[offset + 2] = (byte)(column * 20);
            frame[offset + 3] = byte.MaxValue;
        }

        return frame;
    }
}
