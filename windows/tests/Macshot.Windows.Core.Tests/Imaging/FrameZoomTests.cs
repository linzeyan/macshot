using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class FrameZoomTests
{
    /// <summary>
    /// Every frame outside a zoom segment goes through this, so the untouched parts of a
    /// recording must come back byte for byte. Resampling them at 1:1 would soften the
    /// whole recording to pay for a zoom in two seconds of it.
    /// </summary>
    [TestMethod]
    public void Sample_HandsTheFrameStraightBackWhenNothingIsMagnified()
    {
        var frame = Gradient(8, 4);
        var same = FrameZoom.Sample(frame, 8, 4, new CaptureRegion(0, 0, 8, 4), 8, 4);

        Assert.AreSame(frame, same);
    }

    /// <summary>
    /// The rectangle is what decides which pixels the viewer sees, so an exact crop has to
    /// land on exactly those pixels. Off by one column and a zoom would show the wrong part
    /// of the screen — the failure the user would report as "it zoomed into the wrong
    /// thing".
    /// </summary>
    [TestMethod]
    public void Sample_TakesExactlyTheRectangleItWasGiven()
    {
        var frame = Gradient(4, 4);
        var quarter = FrameZoom.Sample(frame, 4, 4, new CaptureRegion(2, 2, 2, 2), 2, 2);

        // Bottom-right quarter: the source pixels at (2,2), (3,2), (2,3), (3,3).
        Assert.AreEqual(Blue(frame, 4, 2, 2), Blue(quarter, 2, 0, 0));
        Assert.AreEqual(Blue(frame, 4, 3, 2), Blue(quarter, 2, 1, 0));
        Assert.AreEqual(Blue(frame, 4, 2, 3), Blue(quarter, 2, 0, 1));
        Assert.AreEqual(Blue(frame, 4, 3, 3), Blue(quarter, 2, 1, 1));
    }

    /// <summary>
    /// A whole-number magnification samples each source pixel more than once, and the
    /// corners of the output land exactly on source pixels. Getting the pixel-centre
    /// mapping wrong shifts the picture half an output pixel up and left, which across a
    /// ramp shows as the frame sliding diagonally while it grows.
    /// </summary>
    [TestMethod]
    public void Sample_KeepsTheCornersWhereTheyWereWhenMagnifying()
    {
        var frame = Gradient(4, 4);
        var doubled = FrameZoom.Sample(frame, 4, 4, new CaptureRegion(0, 0, 4, 4), 8, 8);

        Assert.AreEqual(Blue(frame, 4, 0, 0), Blue(doubled, 8, 0, 0));
        Assert.AreEqual(Blue(frame, 4, 3, 0), Blue(doubled, 8, 7, 0));
        Assert.AreEqual(Blue(frame, 4, 0, 3), Blue(doubled, 8, 0, 7));
        Assert.AreEqual(Blue(frame, 4, 3, 3), Blue(doubled, 8, 7, 7));
    }

    /// <summary>
    /// Bilinear, not nearest — the reason this exists beside <c>FrameScaler</c>. Between
    /// two source pixels the output has to carry an intermediate value; picking the nearest
    /// instead turns every one-pixel line on a magnified desktop into a staircase that
    /// crawls as the ramp moves.
    /// </summary>
    [TestMethod]
    public void Sample_BlendsBetweenSourcePixelsRatherThanPickingTheNearest()
    {
        // Two pixels: black on the left, white on the right.
        var frame = new byte[] { 0, 0, 0, 255, 255, 255, 255, 255 };
        var stretched = FrameZoom.Sample(frame, 2, 1, new CaptureRegion(0, 0, 2, 1), 4, 1);

        Assert.AreEqual(0, Blue(stretched, 4, 0, 0));
        Assert.AreEqual(255, Blue(stretched, 4, 3, 0));

        // The two in between must actually be in between, and in order.
        var second = Blue(stretched, 4, 1, 0);
        var third = Blue(stretched, 4, 2, 0);
        Assert.IsTrue(second is > 0 and < 255, $"expected a blend, got {second}");
        Assert.IsTrue(third > second, $"expected {third} to be brighter than {second}");
    }

    /// <summary>
    /// A rectangle whose edge falls on the last row or column must not read past the frame.
    /// The clamp is what keeps it in bounds, and without it a zoom held at the right edge
    /// of the screen would throw partway through an export that had already written half a
    /// file.
    /// </summary>
    [TestMethod]
    public void Sample_StaysInsideTheFrameAtItsFarEdges()
    {
        var frame = Gradient(4, 4);
        var edge = FrameZoom.Sample(frame, 4, 4, new CaptureRegion(2, 2, 2, 2), 16, 16);

        Assert.AreEqual(16 * 16 * 4, edge.Length);
        Assert.AreEqual(Blue(frame, 4, 3, 3), Blue(edge, 16, 15, 15));
    }

    /// <summary>
    /// The zoom rectangle comes from a curve evaluated in floating point, so it is almost
    /// never on a pixel boundary. A resampler that only worked on whole pixels would leave
    /// the magnification stepping between integers instead of gliding.
    /// </summary>
    [TestMethod]
    public void Sample_AcceptsARectangleThatIsNotOnAPixelBoundary()
    {
        var frame = Gradient(16, 16);
        var fractional = FrameZoom.Sample(frame, 16, 16, new CaptureRegion(3.4, 2.7, 6.25, 6.25), 16, 16);

        Assert.AreEqual(16 * 16 * 4, fractional.Length);
    }

    /// <summary>
    /// A frame shorter than the width and height claim would be read past the end, and the
    /// bytes that came back would be whatever else was in memory. Refused up front, because
    /// the alternative is an export that succeeds and is wrong.
    /// </summary>
    [TestMethod]
    public void Sample_RefusesAFrameSmallerThanItsDeclaredSize()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            FrameZoom.Sample(new byte[16], 4, 4, new CaptureRegion(0, 0, 4, 4), 4, 4));
    }

    /// <summary>
    /// An empty rectangle would divide by zero on the way to the source coordinate and fill
    /// the output with whatever that produced, which is a black or garbage frame rather
    /// than a failure anyone could act on.
    /// </summary>
    [TestMethod]
    public void Sample_RefusesAnEmptyRectangle()
    {
        var frame = Gradient(4, 4);

        Assert.ThrowsException<ArgumentException>(() =>
            FrameZoom.Sample(frame, 4, 4, new CaptureRegion(1, 1, 0, 2), 4, 4));
    }

    /// <summary>A BGRA frame whose blue channel is unique per pixel.</summary>
    private static byte[] Gradient(int width, int height)
    {
        var pixels = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = ((y * width) + x) * 4;
                pixels[at] = (byte)(((y * width) + x) * 3);
                pixels[at + 1] = (byte)x;
                pixels[at + 2] = (byte)y;
                pixels[at + 3] = 255;
            }
        }

        return pixels;
    }

    private static byte Blue(byte[] pixels, int width, int x, int y) => pixels[(((y * width) + x) * 4)];
}
