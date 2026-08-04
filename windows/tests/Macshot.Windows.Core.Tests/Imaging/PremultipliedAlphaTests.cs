using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class PremultipliedAlphaTests
{
    /// <summary>
    /// The whole point of the conversion: a pixel the cut-out made transparent must carry
    /// no colour into whatever is drawn behind it. Left unscaled it shows as a halo of the
    /// subject's own colour around its edge.
    /// </summary>
    [TestMethod]
    public void From_ScalesTheColourBytesByTheirOwnAlpha()
    {
        byte[] pixels = [200, 100, 50, 128];

        var premultiplied = PremultipliedAlpha.From(pixels);

        Assert.AreEqual(100, premultiplied[0], "blue at half alpha");
        Assert.AreEqual(50, premultiplied[1], "green at half alpha");
        Assert.AreEqual(25, premultiplied[2], "red at half alpha");
    }

    /// <summary>
    /// Alpha itself is not scaled — it is what everything else was scaled by. Scaling it
    /// too would square the transparency and fade the subject away over repeated passes.
    /// </summary>
    [TestMethod]
    public void From_LeavesTheAlphaByteAsItWas()
    {
        var premultiplied = PremultipliedAlpha.From([200, 100, 50, 128]);

        Assert.AreEqual(128, premultiplied[3]);
    }

    /// <summary>
    /// An opaque pixel must come through byte for byte. Almost every pixel of a cut-out
    /// subject is opaque, so an off-by-one here is a visible darkening of the whole image
    /// rather than an edge artefact.
    /// </summary>
    [TestMethod]
    public void From_LeavesAFullyOpaquePixelUnchanged()
    {
        byte[] pixels = [200, 100, 50, 255];

        CollectionAssert.AreEqual(pixels, PremultipliedAlpha.From(pixels));
    }

    /// <summary>
    /// Rounded rather than truncated. Truncation loses up to a whole level on every
    /// channel of every partially transparent pixel, always downwards, which reads as a
    /// grey rim along the fringe where a cut-out's alpha ramps.
    /// </summary>
    [TestMethod]
    public void From_RoundsRatherThanTruncating()
    {
        // 255 × 128 / 255 = 128 exactly; truncating the accumulated fraction gives 127.
        var premultiplied = PremultipliedAlpha.From([255, 255, 255, 128]);

        Assert.AreEqual(128, premultiplied[0]);
    }

    /// <summary>
    /// A fully transparent pixel must carry nothing at all, whatever colour it happened to
    /// hold — the cut-out leaves the subject's discarded surroundings in the colour bytes.
    /// </summary>
    [TestMethod]
    public void From_ErasesTheColourOfAFullyTransparentPixel()
    {
        var premultiplied = PremultipliedAlpha.From([200, 100, 50, 0]);

        Assert.AreEqual(0, premultiplied[0]);
        Assert.AreEqual(0, premultiplied[1]);
        Assert.AreEqual(0, premultiplied[2]);
    }

    /// <summary>
    /// The source buffer is the capture itself and other things still read it — the
    /// encoder, the OCR pass, the segmentation model — every one of which needs the
    /// straight alpha this conversion is undoing.
    /// </summary>
    [TestMethod]
    public void From_DoesNotDisturbTheBufferItWasGiven()
    {
        byte[] pixels = [200, 100, 50, 128];

        PremultipliedAlpha.From(pixels);

        CollectionAssert.AreEqual(new byte[] { 200, 100, 50, 128 }, pixels);
    }

    /// <summary>
    /// A buffer that is not four bytes to the pixel is a caller error, not a picture to
    /// draw: read on, it would shear the colours against the alphas.
    /// </summary>
    [TestMethod]
    public void From_RefusesABufferThatIsNotWholePixels()
    {
        Assert.ThrowsException<ArgumentException>(() => PremultipliedAlpha.From(new byte[6]));
    }
}
