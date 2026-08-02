using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class SketchyArrowTests
{
    private const int Width = 96;
    private const int Height = 48;

    /// <summary>
    /// The preview redraws on every pointer move and the export renders again afterwards.
    /// An arrow whose wobble came from a shared random source would boil for the whole
    /// length of a drag and then come out of the export as a third shape.
    /// </summary>
    [TestMethod]
    public void Sketchy_DrawsTheSameArrowEveryTime()
    {
        var arrow = SketchyArrow();

        CollectionAssert.AreEqual(Render(arrow), Render(arrow));
    }

    /// <summary>
    /// The seed is the annotation's id, so two arrows drawn to the same place must still
    /// differ — otherwise the style is a machine arrow with one fixed crooked shape, which
    /// is worse than a straight one.
    /// </summary>
    [TestMethod]
    public void Sketchy_DrawsADifferentArrowForADifferentMark()
    {
        CollectionAssert.AreNotEqual(Render(SketchyArrow()), Render(SketchyArrow()));
    }

    /// <summary>
    /// Moving a hand-drawn arrow must not redraw it: the id goes with it, so the wobble
    /// does too. Without that, dragging an arrow would reshuffle it under the pointer.
    /// </summary>
    [TestMethod]
    public void Sketchy_KeepsItsShapeWhenItIsMoved()
    {
        var arrow = SketchyArrow();
        var moved = arrow.Translate(4, 0);

        // Compared as the same arrow drawn four pixels along, which is what the move is:
        // every lit pixel of the original has to reappear shifted by exactly that.
        var before = Render(arrow);
        var after = Render(moved);

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x + 4 < Width; x++)
            {
                Assert.AreEqual(
                    before[((y * Width) + x) * 4],
                    after[((y * Width) + x + 4) * 4],
                    $"The arrow changed shape when it moved, at {x},{y}.");
            }
        }
    }

    /// <summary>
    /// A style that drew nothing would look exactly like a broken tool, and the shaft is
    /// deliberately held clear of the head — a version that stopped the shaft short and
    /// never drew the chevron would still leave marks and pass a weaker test.
    /// </summary>
    [TestMethod]
    public void Sketchy_MarksBothTheShaftAndTheHead()
    {
        var pixels = Render(SketchyArrow());

        Assert.IsTrue(Marked(pixels, 8, 40), "The shaft was not drawn.");
        Assert.IsTrue(Marked(pixels, 60, 90), "The head was not drawn.");
    }

    private static bool Marked(byte[] pixels, int fromX, int toX)
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = fromX; x < toX; x++)
            {
                if (pixels[((y * Width) + x) * 4] != 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Annotation SketchyArrow() => Annotation.Create(
        AnnotationTool.Arrow,
        new CapturePoint(8, 24),
        new CapturePoint(88, 24),
        AnnotationStyle.Default with { ArrowStyle = ArrowStyle.Sketchy, StrokeWidth = 3 });

    /// <summary>Renders onto black, so any non-zero byte is somewhere the arrow reached.</summary>
    private static byte[] Render(Annotation arrow) =>
        AnnotationRasterizer.Render(Width, Height, new byte[Width * Height * 4], [arrow]);
}
