using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

/// <summary>
/// The magnified patch the colour sampler shows under the pointer.
/// </summary>
[TestClass]
public sealed class MagnifiedPatchTests
{
    private const int Size = 16;

    [TestMethod]
    public void TheMiddleOfThePatch_IsThePixelBeingPointedAt()
    {
        // The whole promise of the sampler's loupe: what is in the middle is what a
        // click would take.
        var frame = Frame((6, 6, 10, 20, 30));

        var patch = PixelEffects.MagnifiedPatch(frame, Size, Size, 6, 6, 8, zoom: 2);

        Assert.AreEqual((10, 20, 30), PatchPixel(patch, 8, 4, 4));
    }

    [TestMethod]
    public void OnePixel_CoversAsManyAsTheZoomSaysItShould()
    {
        var frame = Frame((6, 6, 10, 20, 30));

        var patch = PixelEffects.MagnifiedPatch(frame, Size, Size, 6, 6, 8, zoom: 4);

        // At four times, the pixel under the pointer fills four patch pixels across,
        // so its neighbours are still two away.
        Assert.AreEqual((10, 20, 30), PatchPixel(patch, 8, 5, 4));
        Assert.AreEqual((10, 20, 30), PatchPixel(patch, 8, 4, 5));
    }

    [TestMethod]
    public void TheCorners_ComeBackTransparentSoThePatchReadsAsACircle()
    {
        var patch = PixelEffects.MagnifiedPatch(Frame(), Size, Size, 8, 8, 8, zoom: 2);

        Assert.AreEqual(0, patch[3], "the top-left corner is outside the circle");
        Assert.AreEqual(byte.MaxValue, patch[(((4 * 8) + 4) * 4) + 3], "the middle is not");
    }

    [TestMethod]
    public void PointingAtTheEdgeOfTheScreen_RepeatsTheEdgeRatherThanLeavingAHole()
    {
        // The sampler has to work in the very corner of a display, where half the
        // magnified view is off the frame.
        var frame = Frame((0, 0, 200, 100, 50));

        var patch = PixelEffects.MagnifiedPatch(frame, Size, Size, 0, 0, 8, zoom: 2);

        Assert.AreEqual((200, 100, 50), PatchPixel(patch, 8, 4, 4));
        Assert.AreEqual(byte.MaxValue, patch[(((4 * 8) + 1) * 4) + 3], "no hole where the frame ran out");
    }

    [TestMethod]
    public void TheMiddleOfThePatch_ShowsTheMarkTheReadoutIsReporting()
    {
        // The circle promises which pixel a click will take. Magnifying the bare capture
        // while the hint line reports a mark's colour would break that promise everywhere
        // anything had been drawn — the two have to read the same pixels.
        var frame = Frame((6, 6, 10, 20, 30));
        var mark = new byte[] { 90, 80, 70, byte.MaxValue };
        var rendered = new RenderedRegion(mark, 1, 1, new CaptureRegion(6, 6, 1, 1));

        var patch = PixelEffects.MagnifiedPatch(frame, Size, Size, 6, 6, 8, zoom: 2, rendered);

        Assert.AreEqual((70, 80, 90), PatchPixel(patch, 8, 4, 4));
    }

    /// <summary>An otherwise black frame with the given pixels painted in.</summary>
    private static byte[] Frame(params (int X, int Y, byte R, byte G, byte B)[] pixels)
    {
        var frame = new byte[Size * Size * 4];
        for (var index = 3; index < frame.Length; index += 4)
        {
            frame[index] = byte.MaxValue;
        }

        foreach (var (x, y, r, g, b) in pixels)
        {
            var offset = (((y * Size) + x) * 4);
            frame[offset] = b;
            frame[offset + 1] = g;
            frame[offset + 2] = r;
        }

        return frame;
    }

    private static (byte R, byte G, byte B) PatchPixel(byte[] patch, int diameter, int x, int y)
    {
        var offset = (((y * diameter) + x) * 4);
        return (patch[offset + 2], patch[offset + 1], patch[offset]);
    }
}
