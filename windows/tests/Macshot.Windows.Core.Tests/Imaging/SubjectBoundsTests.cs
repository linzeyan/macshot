using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class SubjectBoundsTests
{
    private static byte[] CutOut(int width, int height, Func<int, int, byte> alpha)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                pixels[offset] = 90;
                pixels[offset + 1] = 90;
                pixels[offset + 2] = 90;
                pixels[offset + 3] = alpha(x, y);
            }
        }

        return pixels;
    }

    [TestMethod]
    public void Of_FindsTheBoxTheLiftedPixelsFillAndNoMore()
    {
        // Columns 3..6 and rows 2..8 survived the lift. Both edges are inclusive, so the
        // box is 4 by 7 — a box that lost its last row and column would leave a strip of
        // the person showing along two sides of the redaction.
        var pixels = CutOut(10, 12, (x, y) =>
            x is >= 3 and <= 6 && y is >= 2 and <= 8 ? byte.MaxValue : (byte)0);

        Assert.AreEqual(new CaptureRegion(3, 2, 4, 7), SubjectBounds.Of(pixels, 10, 12));
    }

    [TestMethod]
    public void Of_IgnoresTheSoftHaloRoundTheMatte()
    {
        // The model leaves a faint fringe well beyond the subject, especially round hair.
        // Counted, it reaches the frame and the box covers the whole capture — which is a
        // redaction of everything rather than of a person.
        var pixels = CutOut(10, 12, (x, y) =>
            x is >= 4 and <= 5 && y is >= 5 and <= 6 ? byte.MaxValue : (byte)40);

        Assert.AreEqual(new CaptureRegion(4, 5, 2, 2), SubjectBounds.Of(pixels, 10, 12));
    }

    [TestMethod]
    public void Of_AnswersWithNothingWhenNothingWasLifted()
    {
        // Distinguishable from a box covering nowhere, so the caller can say "no people
        // found" rather than adding an invisible redaction.
        Assert.IsNull(SubjectBounds.Of(CutOut(10, 12, (_, _) => 0), 10, 12));
    }
}
