using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class AutoMeasureTests
{
    /// <summary>
    /// A frame with one band of a second colour across it, so a scan has a known edge to
    /// find in each direction.
    /// </summary>
    private static byte[] Frame(int width, int height, Func<int, int, byte> shade)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = shade(x, y);
                var offset = ((y * width) + x) * 4;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = byte.MaxValue;
            }
        }

        return pixels;
    }

    [TestMethod]
    public void Run_StopsAtTheRowsWhereTheColourChanges()
    {
        // Rows 4..8 are the gap being measured; everything else is black. The reading a
        // user wants from this is "five pixels tall", and it is only right if both edges
        // are excluded and both ends are inclusive.
        var pixels = Frame(10, 12, (_, y) => y is >= 4 and <= 8 ? (byte)200 : (byte)0);

        var run = AutoMeasure.Run(pixels, 10, 12, x: 5, y: 6, vertical: true);

        Assert.AreEqual((4, 8), run);
    }

    [TestMethod]
    public void Run_ScansTheRowWhenAskedForHorizontal()
    {
        // The same frame read the other way: every column of row 6 is the band's colour,
        // so a horizontal scan should reach both edges of the frame rather than stopping
        // where the vertical one did.
        var pixels = Frame(10, 12, (_, y) => y is >= 4 and <= 8 ? (byte)200 : (byte)0);

        var run = AutoMeasure.Run(pixels, 10, 12, x: 5, y: 6, vertical: false);

        Assert.AreEqual((0, 9), run);
    }

    [TestMethod]
    public void Run_IgnoresAChangeTooSmallToSee()
    {
        // Dither and sub-pixel fringing move a channel by a handful of levels, and a scan
        // that stopped at those would report the width of one pixel on every photograph.
        // 8 levels on each of three channels is 24, under macshot's 30.
        var pixels = Frame(10, 12, (_, y) => y is >= 4 and <= 8 ? (byte)208 : (byte)200);

        var run = AutoMeasure.Run(pixels, 10, 12, x: 5, y: 6, vertical: true);

        Assert.AreEqual((0, 11), run);
    }

    [TestMethod]
    public void Run_ComparesEveryPixelAgainstTheOneUnderThePointer()
    {
        // A gradient that steps by 6 levels a row: compared with its neighbour no pixel
        // ever differs enough to stop the scan, and the ruler would report the whole
        // height of the capture. Compared with the pixel the user pointed at, it stops
        // where the colour has actually visibly changed.
        var pixels = Frame(10, 40, (_, y) => (byte)(y * 6));

        var run = AutoMeasure.Run(pixels, 10, 40, x: 5, y: 20, vertical: true);

        Assert.IsNotNull(run);
        Assert.IsTrue(run.Value.End - run.Value.Start < 39, "the scan ran through the whole gradient");
    }

    [TestMethod]
    public void Run_AnswersWithOnePixelForALineThatMatchesNothingBesideIt()
    {
        // A one-pixel rule is a real thing to measure, and "no answer" would be
        // indistinguishable from the key not working.
        var pixels = Frame(10, 12, (_, y) => y == 6 ? (byte)255 : (byte)0);

        var run = AutoMeasure.Run(pixels, 10, 12, x: 5, y: 6, vertical: true);

        Assert.AreEqual((6, 6), run);
    }

    [TestMethod]
    public void Run_DeclinesAPointOutsideTheFrame()
    {
        var pixels = Frame(10, 12, (_, _) => 0);

        Assert.IsNull(AutoMeasure.Run(pixels, 10, 12, x: 10, y: 6, vertical: true));
        Assert.IsNull(AutoMeasure.Run(pixels, 10, 12, x: 5, y: -1, vertical: false));
    }
}
