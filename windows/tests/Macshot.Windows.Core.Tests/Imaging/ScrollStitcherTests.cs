using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class ScrollStitcherTests
{
    private const int Width = 32;
    private const int FrameHeight = 64;
    private const int PageHeight = 400;

    [TestMethod]
    public void Add_ScrolledFramesRebuildTheWholePage()
    {
        // The point of the whole exercise: frames of a page scrolled past a window
        // have to come back out as the page, not as a page with a band missing or a
        // band repeated. Asserting the bytes is the only way to know that.
        var page = Page();
        var stitcher = new ScrollStitcher(Width, FrameHeight);

        var offsets = new[] { 0, 20, 55, 90, 130, 160 };
        foreach (var offset in offsets)
        {
            stitcher.Add(FrameAt(page, offset));
        }

        var expectedHeight = offsets[^1] + FrameHeight;
        Assert.AreEqual(expectedHeight, stitcher.Height);
        CollectionAssert.AreEqual(page[..(expectedHeight * Width * 4)], stitcher.ToImage());
    }

    [TestMethod]
    public void ToPreview_KeepsTheShapeOfWhatHasBeenStitched()
    {
        var stitcher = new ScrollStitcher(Width, FrameHeight);
        var page = Page();
        stitcher.Add(FrameAt(page, 0));
        stitcher.Add(FrameAt(page, 40));

        var (pixels, width, height) = stitcher.ToPreview(16);

        // Half the width, so half the rows: the panel this feeds is a picture of the
        // capture, and a preview with the wrong aspect would look like a drift the
        // stitcher had not made.
        Assert.AreEqual(16, width);
        Assert.AreEqual(stitcher.Height / 2, height);
        Assert.AreEqual(width * height * 4, pixels.Length);
    }

    [TestMethod]
    public void ToPreview_NeverEnlargesANarrowCapture()
    {
        var stitcher = new ScrollStitcher(Width, FrameHeight);
        stitcher.Add(FrameAt(Page(), 0));

        var (_, width, height) = stitcher.ToPreview(Width * 4);

        Assert.AreEqual(Width, width);
        Assert.AreEqual(stitcher.Height, height);
    }

    [TestMethod]
    public void ToPreview_OfNothingIsEmptyRatherThanAThrow()
    {
        var (pixels, width, height) = new ScrollStitcher(Width, FrameHeight).ToPreview(200);

        Assert.AreEqual(0, pixels.Length);
        Assert.AreEqual(0, width);
        Assert.AreEqual(0, height);
    }

    [TestMethod]
    public void Add_FirstFrameSeedsTheImage()
    {
        var stitcher = new ScrollStitcher(Width, FrameHeight);

        Assert.AreEqual(ScrollStitchOutcome.Seeded, stitcher.Add(FrameAt(Page(), 0)));
        Assert.AreEqual(FrameHeight, stitcher.Height);
    }

    [TestMethod]
    public void Add_AFrameThatHasNotMovedAddsNothing()
    {
        // Frames arrive on a timer, so most of them show a view nobody has scrolled
        // since the last one. Growing the image for those would repeat the visible
        // page over and over.
        var page = Page();
        var stitcher = new ScrollStitcher(Width, FrameHeight);
        stitcher.Add(FrameAt(page, 0));

        Assert.AreEqual(ScrollStitchOutcome.Unchanged, stitcher.Add(FrameAt(page, 0)));
        Assert.AreEqual(FrameHeight, stitcher.Height);
    }

    [TestMethod]
    public void Add_AppendsOnlyWhatTheScrollRevealed()
    {
        var page = Page();
        var stitcher = new ScrollStitcher(Width, FrameHeight);
        stitcher.Add(FrameAt(page, 0));

        Assert.AreEqual(ScrollStitchOutcome.Advanced, stitcher.Add(FrameAt(page, 30)));
        Assert.AreEqual(FrameHeight + 30, stitcher.Height);
    }

    [TestMethod]
    public void Add_UnrelatedContentIsDroppedRatherThanGuessedAt()
    {
        // The user switched windows, or the page navigated. Appending the best of a
        // set of bad matches would splice one document into another and call it a
        // screenshot.
        var stitcher = new ScrollStitcher(Width, FrameHeight);
        stitcher.Add(FrameAt(Page(), 0));

        Assert.AreEqual(ScrollStitchOutcome.Rejected, stitcher.Add(FrameAt(Page(seed: 917), 0)));
        Assert.AreEqual(FrameHeight, stitcher.Height);
    }

    [TestMethod]
    public void Add_AFeaturelessBandIsRefusedInsteadOfMatchedAnywhere()
    {
        // A blank strip scores the same at every offset, so the winning offset is
        // noise. Trusting it is how a stitched capture loses a page of content
        // without anything looking wrong at the time.
        var page = Page();
        var stitcher = new ScrollStitcher(Width, FrameHeight);
        stitcher.Add(FrameAt(page, 0));

        var blank = new byte[Width * FrameHeight * 4];
        Array.Fill(blank, byte.MaxValue);

        Assert.AreEqual(ScrollStitchOutcome.Rejected, stitcher.Add(blank));
        Assert.AreEqual(FrameHeight, stitcher.Height);
    }

    [TestMethod]
    public void Add_RejectsAFrameOfTheWrongSize()
    {
        var stitcher = new ScrollStitcher(Width, FrameHeight);

        Assert.ThrowsException<ArgumentException>(() => stitcher.Add(new byte[16]));
    }

    [TestMethod]
    public void Constructor_RejectsAFrameShorterThanTheMatchedBand()
    {
        // There would be nothing to match against, so frames would either all seed or
        // all be rejected depending on order. Failing here says why.
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => new ScrollStitcher(Width, ScrollStitcher.BandHeight - 1));
    }

    /// <summary>
    /// A page whose rows are individually distinctive, which is what real content is
    /// and what a band match relies on. The generator is seeded so a failure is
    /// reproducible.
    /// </summary>
    private static byte[] Page(int seed = 7)
    {
        var page = new byte[Width * PageHeight * 4];
        var random = new Random(seed);
        for (var row = 0; row < PageHeight; row++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = ((row * Width) + x) * 4;
                page[offset] = (byte)random.Next(256);
                page[offset + 1] = (byte)random.Next(256);
                page[offset + 2] = (byte)random.Next(256);
                page[offset + 3] = byte.MaxValue;
            }
        }

        return page;
    }

    private static byte[] FrameAt(byte[] page, int offset)
    {
        var stride = Width * 4;
        return page[(offset * stride)..((offset + FrameHeight) * stride)];
    }
}
