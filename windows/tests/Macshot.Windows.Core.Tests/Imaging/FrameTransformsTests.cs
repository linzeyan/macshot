using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class FrameTransformsTests
{
    /// <summary>
    /// A frame whose every pixel says where it came from: blue holds the column and
    /// green the row, so a pixel that moved still names its origin.
    /// </summary>
    private static byte[] Numbered(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var offset = ((row * width) + column) * 4;
                pixels[offset] = (byte)column;
                pixels[offset + 1] = (byte)row;
                pixels[offset + 2] = 0;
                pixels[offset + 3] = byte.MaxValue;
            }
        }

        return pixels;
    }

    private static (int Column, int Row) At(byte[] pixels, int width, int column, int row)
    {
        var offset = ((row * width) + column) * 4;
        return (pixels[offset], pixels[offset + 1]);
    }

    [TestMethod]
    public void FlipHorizontal_SwapsTheColumnsAndLeavesTheRows()
    {
        var flipped = FrameTransforms.FlipHorizontal(4, 3, Numbered(4, 3));

        Assert.AreEqual((3, 0), At(flipped, 4, 0, 0));
        Assert.AreEqual((0, 2), At(flipped, 4, 3, 2));
    }

    [TestMethod]
    public void FlipVertical_SwapsTheRowsAndLeavesTheColumns()
    {
        var flipped = FrameTransforms.FlipVertical(4, 3, Numbered(4, 3));

        Assert.AreEqual((0, 2), At(flipped, 4, 0, 0));
        Assert.AreEqual((3, 0), At(flipped, 4, 3, 2));
    }

    [TestMethod]
    public void FlipHorizontal_TwiceIsTheFrameItStartedAs()
    {
        var original = Numbered(6, 5);

        var round = FrameTransforms.FlipHorizontal(6, 5, FrameTransforms.FlipHorizontal(6, 5, original));

        CollectionAssert.AreEqual(original, round);
    }

    [TestMethod]
    public void Crop_TakesTheRegionAsked()
    {
        var (width, height, pixels) = FrameTransforms.Crop(8, 8, Numbered(8, 8), new CaptureRegion(2, 3, 4, 2));

        Assert.AreEqual(4, width);
        Assert.AreEqual(2, height);
        Assert.AreEqual((2, 3), At(pixels, width, 0, 0));
        Assert.AreEqual((5, 4), At(pixels, width, 3, 1));
    }

    [TestMethod]
    public void Crop_RoundsOutwardsSoNoAskedForColumnIsLost()
    {
        // 2.4 to 5.1 covers part of columns 2 through 5, so all four are kept.
        var (width, _, _) = FrameTransforms.Crop(8, 8, Numbered(8, 8), new CaptureRegion(2.4, 0, 2.7, 4));

        Assert.AreEqual(4, width);
    }

    [TestMethod]
    public void Crop_ClampsARegionRunningOffTheFrame()
    {
        var (width, height, _) = FrameTransforms.Crop(8, 8, Numbered(8, 8), new CaptureRegion(6, 6, 40, 40));

        Assert.AreEqual(2, width);
        Assert.AreEqual(2, height);
    }

    [TestMethod]
    public void Crop_RefusesARegionOutsideTheFrame()
    {
        Assert.ThrowsException<ArgumentException>(
            () => FrameTransforms.Crop(8, 8, Numbered(8, 8), new CaptureRegion(20, 20, 4, 4)));
    }

    [TestMethod]
    public void FlipPoint_PutsAnAnnotationWhereItsPixelsWent()
    {
        var moved = FrameTransforms.FlipPoint(new CapturePoint(10, 40), 100, 80, horizontal: true);

        Assert.AreEqual(new CapturePoint(90, 40), moved);
    }

    [TestMethod]
    public void FlipPoint_MirrorsTheOtherAxisWhenFlippedVertically()
    {
        var moved = FrameTransforms.FlipPoint(new CapturePoint(10, 30), 100, 80, horizontal: false);

        Assert.AreEqual(new CapturePoint(10, 50), moved);
    }

    [TestMethod]
    public void Invert_TurnsEveryColourChannelAndLeavesAlphaAlone()
    {
        // Alpha inverted would turn an opaque capture transparent, which is the one way
        // this can silently destroy an image rather than change it.
        var pixels = new byte[] { 0, 40, 255, 255, 128, 200, 10, 0 };

        var inverted = FrameTransforms.Invert(2, 1, pixels);

        CollectionAssert.AreEqual(new byte[] { 255, 215, 0, 255, 127, 55, 245, 0 }, inverted);
    }

    [TestMethod]
    public void Invert_IsItsOwnWayBack()
    {
        // The button is a switch, so pressing it twice has to give back exactly what was
        // captured — not something a rounding step left one level off.
        var pixels = Numbered(8, 8);

        var thereAndBack = FrameTransforms.Invert(8, 8, FrameTransforms.Invert(8, 8, pixels));

        CollectionAssert.AreEqual(pixels, thereAndBack);
    }

    [TestMethod]
    public void Transforms_RejectABufferThatIsNotTheFrame()
    {
        Assert.ThrowsException<ArgumentException>(() => FrameTransforms.FlipHorizontal(4, 4, new byte[10]));
    }

    [TestMethod]
    public void StackingPutsTheAddedCaptureUnderTheFirst()
    {
        var first = Numbered(3, 2);
        var added = Numbered(3, 1);

        var stacked = FrameTransforms.StackBelow(3, 2, first, 3, 1, added);

        Assert.AreEqual(3 * 3 * 4, stacked.Length, "three rows of three");
        Assert.AreEqual(1, stacked[((2 * 3) + 1) * 4], "the added row's second column, in the third row");
        Assert.AreEqual(0, stacked[((2 * 3) + 1) * 4 + 1], "and it is that capture's own first row");
    }

    [TestMethod]
    public void TheCanvasTakesTheWidthOfTheWiderCapture()
    {
        var narrow = Numbered(2, 1);
        var wide = Numbered(5, 1);

        var stacked = FrameTransforms.StackBelow(2, 1, narrow, 5, 1, wide);

        Assert.AreEqual(5 * 2 * 4, stacked.Length);
    }

    [TestMethod]
    public void BothCapturesKeepTheirLeftEdge()
    {
        // Left-aligned rather than centred: a column of captures added one after another
        // must line up rather than drift about the middle.
        var narrow = Numbered(2, 1);
        var wide = Numbered(5, 1);

        var stacked = FrameTransforms.StackBelow(5, 1, wide, 2, 1, narrow);

        Assert.AreEqual(byte.MaxValue, stacked[3], "the wide capture's first pixel is opaque");
        Assert.AreEqual(byte.MaxValue, stacked[(5 * 4) + 3], "and so is the narrow one's, a row below");
        Assert.AreEqual(0, stacked[(5 * 4) + (4 * 4) + 3], "the gap beside it is left transparent");
    }
}
