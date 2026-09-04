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
    public void RotateRight_PutsTheTopEdgeDownTheRightHandSide()
    {
        // What a quarter turn clockwise means, said in pixels rather than in words: the
        // source's first row becomes the destination's last column, top to bottom.
        var (width, height, pixels) = FrameTransforms.RotateRight(4, 3, Numbered(4, 3));

        Assert.AreEqual(3, width, "the turned frame is as wide as the source was tall");
        Assert.AreEqual(4, height);
        Assert.AreEqual((0, 0), At(pixels, width, 2, 0), "the top-left corner lands top-right");
        Assert.AreEqual((3, 0), At(pixels, width, 2, 3), "and the top-right corner bottom-right");
        Assert.AreEqual((0, 2), At(pixels, width, 0, 0), "the bottom-left corner lands top-left");
    }

    [TestMethod]
    public void RotateLeft_PutsTheTopEdgeUpTheLeftHandSide()
    {
        var (width, height, pixels) = FrameTransforms.RotateLeft(4, 3, Numbered(4, 3));

        Assert.AreEqual(3, width);
        Assert.AreEqual(4, height);
        Assert.AreEqual((0, 0), At(pixels, width, 0, 3), "the top-left corner lands bottom-left");
        Assert.AreEqual((3, 0), At(pixels, width, 0, 0), "and the top-right corner top-left");
    }

    [TestMethod]
    public void RotateRight_FourTimesIsTheFrameItStartedAs()
    {
        // The property that makes the menu item safe to press: nothing is lost or
        // resampled, so a wrong turn is undone by three more.
        var original = Numbered(6, 5);

        var (width, height, pixels) = FrameTransforms.RotateRight(6, 5, original);
        for (var turn = 0; turn < 3; turn++)
        {
            (width, height, pixels) = FrameTransforms.RotateRight(width, height, pixels);
        }

        Assert.AreEqual(6, width);
        Assert.AreEqual(5, height);
        CollectionAssert.AreEqual(original, pixels);
    }

    [TestMethod]
    public void RotateLeft_UndoesRotateRight()
    {
        var original = Numbered(7, 4);

        var (width, height, pixels) = FrameTransforms.RotateRight(7, 4, original);
        (width, height, pixels) = FrameTransforms.RotateLeft(width, height, pixels);

        Assert.AreEqual(7, width);
        Assert.AreEqual(4, height);
        CollectionAssert.AreEqual(original, pixels);
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

    /// <summary>
    /// A recording crops every frame, and a buffer it can reuse is the difference between
    /// a few pooled allocations and eight megabytes a frame going to the large object
    /// heap. It only helps if it produces the same pixels as the allocating one.
    /// </summary>
    [TestMethod]
    public void CropInto_WritesWhatCropWouldHaveAllocated()
    {
        var region = new CaptureRegion(2, 3, 4, 2);
        var (width, height, expected) = FrameTransforms.Crop(8, 8, Numbered(8, 8), region);

        var destination = new byte[width * height * 4];
        FrameTransforms.CropInto(8, 8, Numbered(8, 8), region, destination);

        CollectionAssert.AreEqual(expected, destination);
    }

    /// <summary>
    /// The size has to be answerable before the pixels are, or the caller cannot have the
    /// buffer ready to crop into.
    /// </summary>
    [TestMethod]
    public void CropSize_AnswersWhatCropWouldProduce()
    {
        var region = new CaptureRegion(6, 6, 40, 40);
        var (width, height, _) = FrameTransforms.Crop(8, 8, Numbered(8, 8), region);

        Assert.AreEqual((width, height), FrameTransforms.CropSize(8, 8, region));
    }

    /// <summary>
    /// A pooled buffer arrives holding the last frame's pixels. Filling only part of one
    /// and handing it to the encoder would put a band of the previous frame down the side
    /// of this one, so a destination of the wrong size is refused rather than partly used.
    /// </summary>
    [TestMethod]
    public void CropInto_RefusesABufferThatIsNotTheCropsSize()
    {
        Assert.ThrowsException<ArgumentException>(
            () => FrameTransforms.CropInto(
                8,
                8,
                Numbered(8, 8),
                new CaptureRegion(2, 3, 4, 2),
                new byte[4 * 2 * 4 - 4]));
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
