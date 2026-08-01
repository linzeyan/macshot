using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

/// <summary>
/// The halo laid under a mark so it survives the background it lands on.
/// </summary>
/// <remarks>
/// macshot's <c>outlineColor</c>: a red arrow over a red button is invisible, and the
/// answer is a rim rather than a different arrow. Every line here runs along row 32 on a
/// white field, so the halo is what appears immediately above and below the stroke.
/// </remarks>
[TestClass]
public sealed class AnnotationOutlineTests
{
    private const int Size = 64;

    private static readonly AnnotationColor Mark = new(255, 0, 0);

    /// <summary>
    /// Blue, on a white field, under a red mark: three colours no two of which share a
    /// channel, so every assertion below can name which one it is looking at.
    /// </summary>
    private static readonly AnnotationColor Halo = new(0, 0, 255);

    [TestMethod]
    public void WithoutAnOutlineTheStrokeEndsWhereItsWidthEnds()
    {
        var rendered = Render(Line(outline: null));

        Assert.IsFalse(IsInked(rendered, 32, 24), "nothing reaches this far from a 4-wide line");
    }

    [TestMethod]
    public void AnOutlineWidensTheMarkBySixAndIsInItsOwnColour()
    {
        var rendered = Render(Line(Halo));

        // Halfway out into the halo: past the 4-wide stroke, inside 4 + 6.
        var offset = ((28 * Size) + 32) * 4;

        Assert.IsTrue(IsInked(rendered, 32, 28), "the halo reaches past the stroke");
        // BGRA: blue leading red is the halo, red leading blue is the mark.
        Assert.IsTrue(
            rendered[offset] > rendered[offset + 2],
            "and is the halo's colour, not the mark's");
    }

    /// <summary>
    /// The halo goes underneath. Painted over the mark it would be a fat line in the
    /// wrong colour rather than a rim.
    /// </summary>
    [TestMethod]
    public void TheMarkItselfKeepsItsOwnColour()
    {
        var rendered = Render(Line(Halo));
        var middle = ((32 * Size) + 32) * 4;

        Assert.IsTrue(rendered[middle + 2] > rendered[middle], "the centre of the line is still red");
    }

    /// <summary>
    /// macshot forces solid while an outline is on. A dashed halo round a dashed line is
    /// two rows of dots and reads as neither.
    /// </summary>
    [TestMethod]
    public void TheHaloIsSolidEvenWhenTheMarkIsDashed()
    {
        var rendered = Render(Line(Halo, LineStyle.Dashed));

        var gaps = 0;
        for (var x = 12; x < 52; x++)
        {
            if (!IsInked(rendered, x, 28))
            {
                gaps++;
            }
        }

        Assert.AreEqual(0, gaps, "the halo should run unbroken under a dashed line");
    }

    private static Annotation Line(AnnotationColor? outline, LineStyle lineStyle = LineStyle.Solid) =>
        Annotation.Create(
            AnnotationTool.Line,
            new CapturePoint(8, 32),
            new CapturePoint(56, 32),
            new AnnotationStyle(Mark, 4, lineStyle) { Outline = outline });

    private static byte[] Render(Annotation mark)
    {
        var blank = new byte[Size * Size * 4];
        Array.Fill(blank, byte.MaxValue);
        return AnnotationRasterizer.Render(Size, Size, blank, [mark]);
    }

    private static bool IsInked(byte[] pixels, int column, int row)
    {
        var offset = ((row * Size) + column) * 4;
        return pixels[offset] != byte.MaxValue
            || pixels[offset + 1] != byte.MaxValue
            || pixels[offset + 2] != byte.MaxValue;
    }
}
