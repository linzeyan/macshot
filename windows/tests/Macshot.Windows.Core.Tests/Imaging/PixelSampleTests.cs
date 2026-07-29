using Macshot.Windows.Core.Annotations;
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
}
