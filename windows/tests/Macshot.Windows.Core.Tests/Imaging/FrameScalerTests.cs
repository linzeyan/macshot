using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class FrameScalerTests
{
    [TestMethod]
    public void Downscale_HandsBackTheSameFrameWhenNothingChanges()
    {
        var pixels = Solid(4, 4, 10, 20, 30);

        // The same array, not an equal one: a display's worth of pixels copied every
        // frame to produce the same image is the cost this avoids.
        Assert.AreSame(pixels, FrameScaler.Downscale(pixels, 4, 4, 4, 4));
    }

    [TestMethod]
    public void Downscale_KeepsASolidColourSolid()
    {
        var scaled = FrameScaler.Downscale(Solid(8, 8, 10, 20, 30), 8, 8, 4, 4);

        for (var pixel = 0; pixel < 4 * 4; pixel++)
        {
            Assert.AreEqual(10, scaled[pixel * 4]);
            Assert.AreEqual(20, scaled[(pixel * 4) + 1]);
            Assert.AreEqual(30, scaled[(pixel * 4) + 2]);
        }
    }

    [TestMethod]
    public void Downscale_AveragesWhatEachOutputPixelCovers()
    {
        // Two black pixels and two white ones in a 2x2. Sampling would give whichever
        // corner it looked at; averaging gives the grey that is actually there, which
        // is what stops thin lines from vanishing as a recording is shrunk.
        var pixels = new byte[2 * 2 * 4];
        Set(pixels, 0, 0, 0, 0);
        Set(pixels, 1, 255, 255, 255);
        Set(pixels, 2, 255, 255, 255);
        Set(pixels, 3, 0, 0, 0);

        var scaled = FrameScaler.Downscale(pixels, 2, 2, 1, 1);

        Assert.AreEqual(127, scaled[0]);
        Assert.AreEqual(127, scaled[1]);
        Assert.AreEqual(127, scaled[2]);
    }

    [TestMethod]
    public void Downscale_CoversTheWholeFrameWhenItDoesNotDivideEvenly()
    {
        // 5 does not divide by 2. The bottom right output pixel has to take the rows
        // and columns left over, or the edge of every recording is dropped.
        var scaled = FrameScaler.Downscale(Solid(5, 5, 40, 50, 60), 5, 5, 2, 2);

        Assert.AreEqual(2 * 2 * 4, scaled.Length);
        Assert.AreEqual(40, scaled[(3 * 4) + 0]);
        Assert.AreEqual(50, scaled[(3 * 4) + 1]);
        Assert.AreEqual(60, scaled[(3 * 4) + 2]);
    }

    [TestMethod]
    public void Downscale_RefusesAFrameShorterThanItClaimsToBe()
    {
        Assert.ThrowsException<ArgumentException>(
            () => FrameScaler.Downscale(new byte[16], 4, 4, 2, 2));
    }

    private static byte[] Solid(int width, int height, byte blue, byte green, byte red)
    {
        var pixels = new byte[width * height * 4];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            Set(pixels, pixel, blue, green, red);
        }

        return pixels;
    }

    private static void Set(byte[] pixels, int index, byte blue, byte green, byte red)
    {
        pixels[index * 4] = blue;
        pixels[(index * 4) + 1] = green;
        pixels[(index * 4) + 2] = red;
        pixels[(index * 4) + 3] = 255;
    }
}
