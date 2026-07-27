using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class PixelEffectsTests
{
    private const int Width = 32;
    private const int Height = 32;

    [TestMethod]
    public void Pixelate_ReplacesABlockWithASingleAverageColor()
    {
        // Redaction only holds if no original detail survives inside a block.
        var frame = SplitFrame();

        PixelEffects.Pixelate(frame, Width, Height, new CaptureRegion(8, 8, 16, 16), blockSize: 16);

        var reference = BlueAt(frame, 8, 8);
        for (var y = 8; y < 24; y++)
        {
            for (var x = 8; x < 24; x++)
            {
                Assert.AreEqual(reference, BlueAt(frame, x, y), $"({x},{y}) kept detail the block should have erased");
            }
        }

        Assert.AreNotEqual(0, reference, "averaging black and white must not return either input");
        Assert.AreNotEqual(255, reference);
    }

    [TestMethod]
    public void Pixelate_LeavesPixelsOutsideTheRegionUntouched()
    {
        var frame = SplitFrame();
        var original = (byte[])frame.Clone();

        PixelEffects.Pixelate(frame, Width, Height, new CaptureRegion(8, 8, 8, 8), blockSize: 4);

        Assert.AreEqual(BlueAt(original, 2, 2), BlueAt(frame, 2, 2));
        Assert.AreEqual(BlueAt(original, 28, 28), BlueAt(frame, 28, 28));
    }

    [TestMethod]
    public void Pixelate_RejectsABlockSizeThatWouldNotRedactAnything()
    {
        var frame = new byte[Width * Height * 4];
        Array.Fill(frame, byte.MaxValue);
        FillRect(frame, 9, 8, 1, 16, 0);

        PixelEffects.Pixelate(frame, Width, Height, new CaptureRegion(8, 8, 16, 16), blockSize: 1);

        // A one pixel block would copy every pixel onto itself and leave the
        // content perfectly readable, so the block size is floored to two.
        Assert.AreNotEqual(0, BlueAt(frame, 9, 12), "the black column survived the redaction");
        Assert.AreEqual(BlueAt(frame, 8, 12), BlueAt(frame, 9, 12), "the block must end up uniform");
    }

    [TestMethod]
    public void Blur_SoftensTheEdgeInsideTheRegion()
    {
        var frame = SplitFrame();

        PixelEffects.Blur(frame, Width, Height, new CaptureRegion(4, 4, 24, 24), radius: 3);

        var atEdge = BlueAt(frame, 16, 16);
        Assert.IsTrue(atEdge is > 0 and < 255, $"the edge must become intermediate, got {atEdge}");
    }

    [TestMethod]
    public void Blur_DoesNotSampleContentFromOutsideTheRegion()
    {
        // A redaction that pulls in the sharp pixels next to it would leak exactly
        // the content the user is hiding.
        var frame = new byte[Width * Height * 4];
        Array.Fill(frame, byte.MaxValue);
        FillRect(frame, 0, 0, Width, 8, 0);

        PixelEffects.Blur(frame, Width, Height, new CaptureRegion(0, 12, Width, 8), radius: 4);

        for (var y = 12; y < 20; y++)
        {
            Assert.AreEqual(255, BlueAt(frame, 16, y), $"row {y} picked up the black band outside the region");
        }
    }

    [TestMethod]
    public void Blur_RejectsANonPositiveRadius()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => PixelEffects.Blur(SplitFrame(), Width, Height, new CaptureRegion(0, 0, 8, 8), radius: 0));
    }

    [TestMethod]
    public void Effects_RejectABufferThatDoesNotMatchTheFrame()
    {
        Assert.ThrowsException<ArgumentException>(
            () => PixelEffects.Pixelate(new byte[16], Width, Height, new CaptureRegion(0, 0, 4, 4), blockSize: 4));
    }

    /// <summary>A frame whose left half is black and right half is white.</summary>
    private static byte[] SplitFrame()
    {
        var frame = new byte[Width * Height * 4];
        Array.Fill(frame, byte.MaxValue);
        FillRect(frame, 0, 0, Width / 2, Height, 0);
        return frame;
    }

    private static void FillRect(byte[] frame, int left, int top, int width, int height, byte value)
    {
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                var offset = (y * Width + x) * 4;
                frame[offset] = value;
                frame[offset + 1] = value;
                frame[offset + 2] = value;
                frame[offset + 3] = byte.MaxValue;
            }
        }
    }

    private static byte BlueAt(byte[] frame, int x, int y) => frame[(y * Width + x) * 4];
}
