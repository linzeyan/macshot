using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

/// <summary>
/// Turning an arrow round without redrawing it.
/// </summary>
/// <remarks>
/// An arrow is drawn from where the hand starts to where it stops, and what it should
/// point at is often where the hand started — pointing at a menu item means dragging out
/// of the menu, which is the drag hardest to aim. Every arrow here runs left to right
/// along row 32, so the head belongs at the right end and reversing has to move it to
/// the left one. These check the ink moves, not that a flag is stored.
/// </remarks>
[TestClass]
public sealed class ArrowReversalTests
{
    private const int Size = 64;

    [TestMethod]
    public void ReversingMovesTheHeadToTheEndTheDragBeganFrom()
    {
        var forward = Render(Arrow(ArrowStyle.Filled, reversed: false));
        var backward = Render(Arrow(ArrowStyle.Filled, reversed: true));

        // Inside the head, a head's length in from each end. Only a filled triangle inks
        // what is between the shaft and its own sloping edge.
        Assert.IsTrue(IsInked(forward, 36, 38), "an arrow drawn left to right points right");
        Assert.IsFalse(IsInked(forward, 28, 38), "and has nothing at its tail");

        Assert.IsTrue(IsInked(backward, 28, 38), "a reversed one points back the way it came");
        Assert.IsFalse(IsInked(backward, 36, 38), "and gives up the head it had");
    }

    /// <summary>
    /// The bar has to follow the head. Left where it was it would sit across the point,
    /// which reads as an arrow with a line through it rather than one with a tail.
    /// </summary>
    [TestMethod]
    public void TheTailBarMovesWithTheHead()
    {
        var forward = Render(Arrow(ArrowStyle.Tail, reversed: false));
        var backward = Render(Arrow(ArrowStyle.Tail, reversed: true));

        // Square across the shaft means straight up and down from one end, which is
        // where nothing else in this arrow reaches.
        Assert.IsTrue(IsInked(forward, 8, 22), "the bar starts at the left end");
        Assert.IsFalse(IsInked(forward, 56, 22));

        Assert.IsTrue(IsInked(backward, 56, 22), "and reversing takes it to the right one");
        Assert.IsFalse(IsInked(backward, 8, 22));
    }

    /// <summary>
    /// A double-headed arrow has one at each end, so reversing has nothing to change —
    /// and any difference would mean the two heads are not built the same way.
    /// </summary>
    [TestMethod]
    public void ADoubleHeadedArrowIsTheSameEitherWay()
    {
        CollectionAssert.AreEqual(
            Render(Arrow(ArrowStyle.Double, reversed: false)),
            Render(Arrow(ArrowStyle.Double, reversed: true)));
    }

    [TestMethod]
    public void AnOpenHeadReversesToo()
    {
        var backward = Render(Arrow(ArrowStyle.Open, reversed: true));

        // The two strokes are at the near end now, and nothing is between them.
        Assert.IsTrue(IsInked(backward, 28, 43), "the strokes are at the end the drag began from");
        Assert.IsFalse(IsInked(backward, 28, 38), "and an open head is still open");
    }

    private static Annotation Arrow(ArrowStyle style, bool reversed) =>
        Annotation.Create(
            AnnotationTool.Arrow,
            new CapturePoint(8, 32),
            new CapturePoint(56, 32),
            new AnnotationStyle(new AnnotationColor(0, 0, 0), 6, ArrowStyle: style)
            {
                ArrowReversed = reversed,
            });

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
