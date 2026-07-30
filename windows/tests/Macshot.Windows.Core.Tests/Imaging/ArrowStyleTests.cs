using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

/// <summary>
/// What each arrow style puts at the ends of the shaft.
/// </summary>
/// <remarks>
/// Every arrow here runs left to right along row 32 with a heavy stroke, so the head is
/// at the right end and is large enough to have an inside: the samples below sit between
/// the shaft, which both styles draw, and the sloping edge, which both styles draw too.
/// Only a filled head inks what is between them, and a head sized to a hairline stroke
/// would leave no room to ask.
/// </remarks>
[TestClass]
public sealed class ArrowStyleTests
{
    private const int Size = 64;

    [TestMethod]
    public void Filled_InksTheInsideOfTheHead()
    {
        var rendered = Render(Arrow(ArrowStyle.Filled));

        Assert.IsTrue(IsInked(rendered, 36, 38), "the head is solid, not an outline");
    }

    [TestMethod]
    public void Open_LeavesTheInsideOfTheHeadAlone()
    {
        var rendered = Render(Arrow(ArrowStyle.Open));

        // The two strokes still cross this column, so a point between them and clear of
        // both is what separates a drawn head from a filled one.
        Assert.IsFalse(IsInked(rendered, 36, 38), "an open head is two strokes with nothing between them");
        Assert.IsTrue(IsInked(rendered, 36, 43), "the strokes themselves are still there");
    }

    [TestMethod]
    public void Double_PutsAHeadOnTheNearEndAsWell()
    {
        var single = Render(Arrow(ArrowStyle.Filled));
        var doubled = Render(Arrow(ArrowStyle.Double));

        Assert.IsFalse(IsInked(single, 28, 38), "a plain arrow has nothing at its tail");
        Assert.IsTrue(IsInked(doubled, 28, 38), "a double arrow points both ways");
    }

    [TestMethod]
    public void Tail_PutsABarSquareAcrossTheNearEnd()
    {
        var rendered = Render(Arrow(ArrowStyle.Tail));

        // Square across a shaft that runs left to right means straight up and down from
        // the start, which is where nothing else in this arrow reaches.
        Assert.IsTrue(IsInked(rendered, 8, 22), "the bar reaches above the shaft");
        Assert.IsTrue(IsInked(rendered, 8, 42), "and below it");
        Assert.IsFalse(IsInked(rendered, 20, 22), "but only at the very end");
    }

    [TestMethod]
    public void TheHeadGrowsWithTheStroke()
    {
        // A hairline arrow with a head sized to its stroke would end in nothing at all,
        // and a heavy one with a fixed head would look like it lost its point.
        var thin = Render(Arrow(ArrowStyle.Filled, strokeWidth: 1));
        var thick = Render(Arrow(ArrowStyle.Filled, strokeWidth: 6));

        Assert.IsFalse(IsInked(thin, 44, 26), "a hairline head reaches nowhere near here");
        Assert.IsTrue(IsInked(thick, 44, 26));
    }

    private static Annotation Arrow(ArrowStyle style, double strokeWidth = 6) =>
        Annotation.Create(
            AnnotationTool.Arrow,
            new CapturePoint(8, 32),
            new CapturePoint(56, 32),
            new AnnotationStyle(new AnnotationColor(0, 0, 0), strokeWidth, ArrowStyle: style));

    private static byte[] Render(Annotation arrow)
    {
        var blank = new byte[Size * Size * 4];
        Array.Fill(blank, byte.MaxValue);
        return AnnotationRasterizer.Render(Size, Size, blank, [arrow]);
    }

    private static bool IsInked(byte[] pixels, int column, int row)
    {
        var offset = ((row * Size) + column) * 4;
        return pixels[offset] != byte.MaxValue
            || pixels[offset + 1] != byte.MaxValue
            || pixels[offset + 2] != byte.MaxValue;
    }
}
