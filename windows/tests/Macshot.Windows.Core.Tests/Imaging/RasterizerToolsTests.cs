using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

/// <summary>
/// The tools added after the first rasterizer pass — measure and loupe — and the two
/// properties that reshape the ones already there.
/// </summary>
[TestClass]
public sealed class RasterizerToolsTests
{
    private const int Size = 64;

    private static byte[] Blank()
    {
        var pixels = new byte[Size * Size * 4];
        Array.Fill(pixels, byte.MaxValue);
        return pixels;
    }

    private static bool IsInked(byte[] pixels, int column, int row)
    {
        var offset = ((row * Size) + column) * 4;

        // Anything the rasterizer touched moves off the white the frame started as.
        return pixels[offset] != byte.MaxValue
            || pixels[offset + 1] != byte.MaxValue
            || pixels[offset + 2] != byte.MaxValue;
    }

    private static byte[] Render(byte[] source, params Annotation[] annotations) =>
        AnnotationRasterizer.Render(Size, Size, source, annotations);

    private static Annotation Shape(AnnotationTool tool, double x1, double y1, double x2, double y2) =>
        Annotation.Create(tool, new CapturePoint(x1, y1), new CapturePoint(x2, y2));

    [TestMethod]
    public void SupportedTools_OffersTheTwoNewToolsToTheToolbar()
    {
        // The toolbar is built from this list precisely so it cannot fall behind what
        // the rasterizer can draw.
        CollectionAssert.Contains(AnnotationRasterizer.SupportedTools.ToArray(), AnnotationTool.Measure);
        CollectionAssert.Contains(AnnotationRasterizer.SupportedTools.ToArray(), AnnotationTool.Loupe);
    }

    [TestMethod]
    public void Measure_DrawsABarSquareAcrossEachEnd()
    {
        var rendered = Render(Blank(), Shape(AnnotationTool.Measure, 20, 32, 44, 32));

        // The span itself.
        Assert.IsTrue(IsInked(rendered, 32, 32));

        // And a bar across each end, which a plain line would not have.
        Assert.IsTrue(IsInked(rendered, 20, 27), "The start should carry a bar across the span.");
        Assert.IsTrue(IsInked(rendered, 44, 36), "The end should carry a bar across the span.");
    }

    [TestMethod]
    public void Measure_LeavesTheFrameAloneBeyondItsEnds()
    {
        var rendered = Render(Blank(), Shape(AnnotationTool.Measure, 20, 32, 44, 32));

        Assert.IsFalse(IsInked(rendered, 55, 32));
    }

    [TestMethod]
    public void Loupe_BringsDistantPixelsInUnderTheCircle()
    {
        var source = Blank();

        // A single black pixel four columns left of the loupe's centre. At twice the
        // size it must be drawn twice as far from the centre as it really is.
        var offset = ((32 * Size) + 28) * 4;
        source[offset] = 0;
        source[offset + 1] = 0;
        source[offset + 2] = 0;

        var rendered = Render(source, Shape(AnnotationTool.Loupe, 16, 16, 48, 48));

        Assert.IsTrue(IsInked(rendered, 24, 32), "The magnified mark should land at twice its offset.");
    }

    [TestMethod]
    public void Loupe_LeavesEverythingOutsideItsCircleAlone()
    {
        var rendered = Render(Blank(), Shape(AnnotationTool.Loupe, 16, 16, 48, 48));

        // The corner of the loupe's bounding box lies outside the circle it draws.
        Assert.IsFalse(IsInked(rendered, 17, 17));
    }

    [TestMethod]
    public void Loupe_RingsItselfSoItsEdgeShowsOverFlatPixels()
    {
        var rendered = Render(Blank(), Shape(AnnotationTool.Loupe, 16, 16, 48, 48));

        // On a blank frame the magnified content is indistinguishable from what
        // surrounds it; without the ring the tool would appear to do nothing at all.
        Assert.IsTrue(IsInked(rendered, 32, 16));
    }

    [TestMethod]
    public void Bend_PullsTheMiddleOfALineOffTheStraightPath()
    {
        var straight = Render(Blank(), Shape(AnnotationTool.Line, 10, 32, 54, 32));
        var bent = Render(Blank(), Shape(AnnotationTool.Line, 10, 32, 54, 32) with { Bend = 0.2 });

        // The line is 44 long and the bend is a fifth of that, so its middle sits
        // 8.8 pixels below the straight path — which is what doubling the control
        // point buys, since a quadratic curve only reaches halfway towards it.
        Assert.IsTrue(IsInked(straight, 32, 32));
        Assert.IsFalse(IsInked(straight, 32, 41), "A straight line has nothing this far off the path.");
        Assert.IsTrue(IsInked(bent, 32, 41), "The bend should carry the middle of the line down to it.");
    }

    [TestMethod]
    public void Bend_LeavesTheEndsWhereTheyWere()
    {
        var bent = Render(Blank(), Shape(AnnotationTool.Line, 10, 32, 54, 32) with { Bend = 0.2 });

        Assert.IsTrue(IsInked(bent, 10, 32));
        Assert.IsTrue(IsInked(bent, 54, 32));
    }

    [TestMethod]
    public void Bend_OfZeroIsTheStraightLineItAlwaysWas()
    {
        var straight = Render(Blank(), Shape(AnnotationTool.Line, 10, 20, 54, 44));
        var explicitlyStraight = Render(Blank(), Shape(AnnotationTool.Line, 10, 20, 54, 44) with { Bend = 0 });

        CollectionAssert.AreEqual(straight, explicitlyStraight);
    }

    [TestMethod]
    public void Rotation_TurnsAShapeOutsideItsUprightBounds()
    {
        var upright = Shape(AnnotationTool.Rectangle, 20, 28, 44, 36);

        var turned = Render(Blank(), upright with { Rotation = Math.PI / 2 });

        // A quarter turn about the centre (32, 32) swaps the rectangle's sides, so it
        // reaches well above where the upright one stopped and no longer reaches out
        // to its old left edge.
        Assert.IsTrue(IsInked(turned, 32, 20), "A quarter-turned rectangle should reach above its upright bounds.");
        Assert.IsFalse(IsInked(turned, 21, 32), "And should no longer reach its upright left edge.");
    }

    [TestMethod]
    public void CornerRadius_CutsTheCornerOffARectangle()
    {
        var sharp = Shape(AnnotationTool.Rectangle, 20, 20, 44, 44);
        var rounded = sharp with { Style = sharp.Style with { CornerRadius = 8 } };

        // The very corner of the bounds: on the box when it is square, and cut away
        // when it is rounded.
        Assert.IsTrue(IsInked(Render(Blank(), sharp), 20, 20));
        Assert.IsFalse(IsInked(Render(Blank(), rounded), 20, 20));

        // The sides are untouched by rounding — only the corners move.
        Assert.IsTrue(IsInked(Render(Blank(), rounded), 32, 20));
    }

    [TestMethod]
    public void CornerRadius_CannotRoundMoreThanTheShapeHasToGive()
    {
        // Half the shorter side is a full half-circle at each end; asking for more has
        // no corner left to cut, and arcs that crossed would fold the shape in on
        // itself. The clamp is what keeps a slider dragged to its top honest.
        var small = Shape(AnnotationTool.Rectangle, 24, 28, 40, 36);
        var asked = small with { Style = small.Style with { CornerRadius = 400 } };

        var rendered = Render(Blank(), asked);

        Assert.IsTrue(IsInked(rendered, 32, 28), "the middle of the top edge is still drawn");
        Assert.IsTrue(IsInked(rendered, 24, 32), "and the middle of the left one");
    }

    [TestMethod]
    public void CornerRadius_LeavesTheRedactionToolAlone()
    {
        // A filled rectangle is what covers something up. Rounding its corners would
        // leave the pixels it was placed over showing at each one.
        var covered = Shape(AnnotationTool.Censor, 20, 20, 44, 44);
        var asked = covered with { Style = covered.Style with { CornerRadius = 12 } };

        CollectionAssert.AreEqual(Render(Blank(), covered), Render(Blank(), asked));
    }

    [TestMethod]
    public void Rotation_OfZeroIsTheShapeItAlwaysWas()
    {
        var upright = Render(Blank(), Shape(AnnotationTool.Rectangle, 20, 20, 44, 44));
        var explicitlyUpright = Render(Blank(), Shape(AnnotationTool.Rectangle, 20, 20, 44, 44) with { Rotation = 0 });

        CollectionAssert.AreEqual(upright, explicitlyUpright);
    }

    [TestMethod]
    public void Rotation_TurnsAnArrowHeadWithItsShaft()
    {
        var arrow = Shape(AnnotationTool.Arrow, 20, 32, 44, 32);

        var turned = Render(Blank(), arrow with { Rotation = Math.PI / 2 });

        // The head was at the right end; a quarter turn puts it at the bottom.
        Assert.IsTrue(IsInked(turned, 32, 43));
    }

    [TestMethod]
    public void Measure_DrawsItsReadingWhenTheUiHasSuppliedOne()
    {
        // Premultiplied black: colour zero, alpha full. White would composite to white
        // and be invisible against a blank frame.
        var opaque = new byte[4 * 4 * 4];
        for (var index = 3; index < opaque.Length; index += 4)
        {
            opaque[index] = byte.MaxValue;
        }

        var label = new AnnotationSprite(4, 4, opaque);

        var measured = Shape(AnnotationTool.Measure, 20, 32, 44, 32) with { Sprite = label };

        // A sprite on a measure is its reading, and it has to reach the frame rather
        // than be dropped for not being one of the three sprite tools.
        var rendered = Render(Blank(), measured);

        // Beside the span, centred on its middle — not at the bounds' corner, where a
        // label's sprite goes. A reading sitting on one end of the ruler covers the very
        // pixels the ruler was dragged out to measure.
        Assert.IsTrue(IsInked(rendered, 31, 20), "the reading belongs above a span that runs across");
        Assert.IsFalse(IsInked(rendered, 31, 40), "nothing belongs below it");
    }

    [TestMethod]
    public void Measure_PutsTheReadingBesideAVerticalSpan()
    {
        // Above is meaningless for a ruler that runs down the screen, so the reading goes
        // to the right of it, which is the side a reader looks for a label on.
        var opaque = new byte[4 * 4 * 4];
        for (var index = 3; index < opaque.Length; index += 4)
        {
            opaque[index] = byte.MaxValue;
        }

        var measured = Shape(AnnotationTool.Measure, 32, 20, 32, 44)
            with { Sprite = new AnnotationSprite(4, 4, opaque) };

        var rendered = Render(Blank(), measured);

        Assert.IsTrue(IsInked(rendered, 42, 31));
        Assert.IsFalse(IsInked(rendered, 22, 31), "the reading must not land on the left");
    }
}
