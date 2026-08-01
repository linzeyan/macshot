using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

/// <summary>
/// The four ways the censor tool covers a region, and the one property they share:
/// how much of the original survives is decided here, not by a control.
/// </summary>
[TestClass]
public sealed class CensorTests
{
    private const int Size = 600;

    [TestMethod]
    public void Pixelate_CoversTheSameWayWhateverTheStrokeWidthIs()
    {
        // The whole reason the censor tool has no size control. A cell taken from the
        // stroke slider makes the same redaction a different strength depending on how
        // thick the last arrow was.
        var thin = Rendered(Noise(), Censor(CensorMode.Pixelate, strokeWidth: 1));
        var thick = Rendered(Noise(), Censor(CensorMode.Pixelate, strokeWidth: 32));

        CollectionAssert.AreEqual(thin, thick);
    }

    [TestMethod]
    public void Pixelate_AveragesIntoCellsRatherThanLeavingWhatWasThere()
    {
        var frame = Noise();

        var censored = Rendered(frame, Censor(CensorMode.Pixelate));

        // Neighbouring pixels well inside one cell are now the same colour, and the
        // region no longer matches what was under it.
        Assert.AreEqual(At(censored, 104, 104), At(censored, 105, 104));
        Assert.AreNotEqual(At(frame, 104, 104), At(censored, 104, 104));
    }

    [TestMethod]
    public void Blur_SpreadsFurtherInALargerRegionThanASmallOne()
    {
        // macshot's radius is 3% of the shorter side with a floor of 10, so it scales
        // with what is being hidden: a radius that hides a line of text in a small
        // region is nothing at all across a whole window.
        var small = Reach(new CaptureRegion(100, 100, 120, 120));
        var large = Reach(new CaptureRegion(0, 0, Size, Size));

        Assert.IsTrue(large > small, $"a {Size}px region blurred no further than a 120px one ({large} vs {small})");
    }

    [TestMethod]
    public void Blur_IgnoresTheStrokeWidth()
    {
        var thin = Rendered(Edge(), Censor(CensorMode.Blur, strokeWidth: 1));
        var thick = Rendered(Edge(), Censor(CensorMode.Blur, strokeWidth: 32));

        CollectionAssert.AreEqual(thin, thick);
    }

    [TestMethod]
    public void Solid_PaintsTheRegionInTheChosenColour()
    {
        var censored = Rendered(Noise(), Censor(CensorMode.Solid, color: new AnnotationColor(10, 20, 30)));

        Assert.AreEqual(new AnnotationColor(10, 20, 30), At(censored, 150, 150));
    }

    [TestMethod]
    public void Erase_FillsTheRegionWithWhatSurroundsIt()
    {
        // What separates it from solid: the region reads as empty background rather
        // than as something covered up, so nobody looks for what was under the box.
        var frame = Solid(new AnnotationColor(200, 30, 30));
        Fill(frame, new CaptureRegion(100, 100, 200, 200), new AnnotationColor(0, 0, 255));

        var censored = Rendered(frame, Censor(CensorMode.Erase, new CaptureRegion(100, 100, 200, 200)));

        Assert.AreEqual(new AnnotationColor(200, 30, 30), At(censored, 200, 200), "the blue block should be gone");
        Assert.AreEqual(new AnnotationColor(200, 30, 30), At(censored, 101, 101), "including at its corners");
    }

    [TestMethod]
    public void Erase_LeavesEverythingOutsideTheRegionAlone()
    {
        // A stripe on either side of the region, so "unchanged" is something the frame
        // can actually be wrong about — a plain background would pass this either way.
        var frame = Solid(new AnnotationColor(200, 30, 30));
        Fill(frame, new CaptureRegion(95, 0, 5, Size), new AnnotationColor(0, 255, 0));
        Fill(frame, new CaptureRegion(300, 0, 5, Size), new AnnotationColor(0, 255, 0));

        var censored = Rendered(frame, Censor(CensorMode.Erase, new CaptureRegion(100, 100, 200, 200)));

        Assert.AreEqual(new AnnotationColor(0, 255, 0), At(censored, 99, 150), "one pixel outside the left edge");
        Assert.AreEqual(new AnnotationColor(0, 255, 0), At(censored, 300, 150), "and outside the right one");
    }

    [TestMethod]
    public void Erase_TakesTheEdgeColourWhenTheRegionRunsOffTheFrame()
    {
        // A region dragged past the edge of the screen has nothing outside it to sample.
        // Clamping means it takes the colour of its own edge rather than black.
        var frame = Solid(new AnnotationColor(70, 140, 210));

        var censored = Rendered(frame, Censor(CensorMode.Erase, new CaptureRegion(0, 0, Size, Size)));

        Assert.AreEqual(new AnnotationColor(70, 140, 210), At(censored, 0, 0));
        Assert.AreEqual(new AnnotationColor(70, 140, 210), At(censored, Size - 1, Size - 1));
    }

    /// <summary>
    /// How far a blurred region carries the dark half of the frame into the light half,
    /// measured along the middle row. A wider radius reaches further.
    /// </summary>
    private static int Reach(CaptureRegion region)
    {
        var censored = Rendered(Edge(), Censor(CensorMode.Blur, region));
        var row = (int)(region.Y + (region.Height / 2));
        var right = (int)(region.X + region.Width);

        var reach = 0;
        for (var x = Size / 2; x < right; x++)
        {
            if (At(censored, x, row).Blue < 250)
            {
                reach = x - (Size / 2);
            }
        }

        return reach;
    }

    private static Annotation Censor(
        CensorMode mode,
        CaptureRegion? region = null,
        double strokeWidth = 3,
        AnnotationColor? color = null)
    {
        var bounds = region ?? new CaptureRegion(100, 100, 200, 200);
        return Annotation.Create(
            AnnotationTool.Censor,
            new CapturePoint(bounds.X, bounds.Y),
            new CapturePoint(bounds.X + bounds.Width, bounds.Y + bounds.Height),
            new AnnotationStyle(color ?? new AnnotationColor(0, 0, 0), strokeWidth, CensorMode: mode));
    }

    private static byte[] Rendered(byte[] frame, Annotation annotation) =>
        AnnotationRasterizer.Render(Size, Size, frame, [annotation]);

    /// <summary>A frame whose left half is black and right half white.</summary>
    private static byte[] Edge()
    {
        var frame = Solid(new AnnotationColor(255, 255, 255));
        Fill(frame, new CaptureRegion(0, 0, Size / 2, Size), new AnnotationColor(0, 0, 0));
        return frame;
    }

    /// <summary>A frame no two neighbouring pixels of which are the same colour.</summary>
    private static byte[] Noise()
    {
        var frame = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var offset = ((y * Size) + x) * 4;
                frame[offset] = (byte)((x * 37) + (y * 11));
                frame[offset + 1] = (byte)((x * 17) + (y * 53));
                frame[offset + 2] = (byte)((x * 71) + (y * 29));
                frame[offset + 3] = byte.MaxValue;
            }
        }

        return frame;
    }

    private static byte[] Solid(AnnotationColor color)
    {
        var frame = new byte[Size * Size * 4];
        Fill(frame, new CaptureRegion(0, 0, Size, Size), color);
        return frame;
    }

    private static void Fill(byte[] frame, CaptureRegion region, AnnotationColor color)
    {
        for (var y = (int)region.Y; y < region.Y + region.Height; y++)
        {
            for (var x = (int)region.X; x < region.X + region.Width; x++)
            {
                var offset = ((y * Size) + x) * 4;
                frame[offset] = color.Blue;
                frame[offset + 1] = color.Green;
                frame[offset + 2] = color.Red;
                frame[offset + 3] = byte.MaxValue;
            }
        }
    }

    private static AnnotationColor At(byte[] frame, int x, int y)
    {
        var offset = ((y * Size) + x) * 4;
        return new AnnotationColor(frame[offset + 2], frame[offset + 1], frame[offset]);
    }
}
