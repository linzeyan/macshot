using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class AnnotationRasterizerTests
{
    /// <summary>
    /// The live preview renders through <c>RenderInto</c> and the delivered image is
    /// whatever the preview last produced, so the two entry points drifting apart
    /// would silently hand the user pixels they never approved.
    /// </summary>
    [TestMethod]
    public void RenderInto_ProducesTheSamePixelsAsRender()
    {
        const int width = 24;
        const int height = 16;
        var source = new byte[width * height * 4];
        Array.Fill(source, (byte)40);
        var annotations = new[]
        {
            Annotation.Create(AnnotationTool.Rectangle, new CapturePoint(3, 3), new CapturePoint(18, 12)),
            Annotation.Create(AnnotationTool.Blur, new CapturePoint(5, 5), new CapturePoint(15, 10)),
        };

        var expected = AnnotationRasterizer.Render(width, height, source, annotations);
        var actual = new byte[source.Length];
        AnnotationRasterizer.RenderInto(width, height, source, actual, annotations);

        CollectionAssert.AreEqual(expected, actual);
    }

    /// <summary>
    /// The toolbar is built from this list, and a tool the rasterizer cannot draw
    /// throws rather than quietly rendering nothing.
    /// </summary>
    [TestMethod]
    public void SupportedTools_AreAllRasterizable()
    {
        const int width = 12;
        const int height = 12;
        var source = new byte[width * height * 4];

        foreach (var tool in AnnotationRasterizer.SupportedTools)
        {
            var annotation = Annotation.Create(tool, new CapturePoint(2, 2), new CapturePoint(9, 9));
            AnnotationRasterizer.Render(width, height, source, [annotation]);
        }
    }

    [TestMethod]
    public void RenderInto_RejectsADestinationOfTheWrongSize()
    {
        var source = new byte[4 * 4 * 4];

        Assert.ThrowsException<ArgumentException>(() =>
            AnnotationRasterizer.RenderInto(4, 4, source, new byte[8], []));
    }

    private const int Width = 64;
    private const int Height = 24;

    private static readonly AnnotationColor Black = new(0, 0, 0);

    [TestMethod]
    public void Render_SolidStrokePaintsTheWholeLine()
    {
        var rendered = RenderLine(LineStyle.Solid);

        for (var x = 10; x <= 50; x++)
        {
            Assert.IsTrue(IsInked(rendered, x, 10), $"a solid stroke must be continuous, but x={x} is untouched");
        }
    }

    [TestMethod]
    public void Render_DottedStrokeDepositsDotsWithGapsBetweenThem()
    {
        // Regression: the dash walker treated the zero length "on" run of the
        // dotted pattern as an empty range, so dotted strokes painted nothing at
        // all after the first step. Dots must appear at the pattern spacing.
        var rendered = RenderLine(LineStyle.Dotted, strokeWidth: 2);

        // The pattern for stroke width 2 is [0, 6]: a dot every 6px from the start.
        Assert.IsTrue(IsInked(rendered, 5, 10), "the stroke must start with a dot");
        Assert.IsTrue(IsInked(rendered, 11, 10), "the next dot must land one full period along");
        Assert.IsTrue(IsInked(rendered, 17, 10), "dots must continue for the whole stroke");
        Assert.IsFalse(IsInked(rendered, 8, 10), "the gap between dots must stay empty");
    }

    [TestMethod]
    public void Render_DashedStrokeAlternatesInkAndGap()
    {
        var rendered = RenderLine(LineStyle.Dashed, strokeWidth: 2);

        // The pattern for stroke width 2 is [6, 4]: ink for 6px, gap for 4px. The
        // round caps at each end of a dash eat into the gap, so the empty pixels
        // sit in the middle of it.
        Assert.IsTrue(IsInked(rendered, 7, 10));
        Assert.IsFalse(IsInked(rendered, 13, 10));
        Assert.IsTrue(IsInked(rendered, 17, 10));
    }

    [TestMethod]
    public void Render_TranslucentStrokeDoesNotBlendItsOwnOverlapsTwice()
    {
        // Stamping round caps along a stroke overlaps them heavily. Blending each
        // stamp separately would darken the overlaps, which is what makes a
        // half-opacity highlighter look mottled instead of even.
        var style = new AnnotationStyle(Black, 4, LineStyle.Solid, Opacity: 0.5);
        var annotation = Annotation.Create(
            AnnotationTool.Marker,
            new CapturePoint(5, 10),
            new CapturePoint(55, 10),
            style);

        var rendered = AnnotationRasterizer.Render(Width, Height, WhiteFrame(), [annotation]);

        var reference = BlueAt(rendered, 20, 10);
        Assert.AreEqual(128, reference, "50% black over white must land on the midpoint");
        for (var x = 21; x <= 40; x++)
        {
            Assert.AreEqual(reference, BlueAt(rendered, x, 10), $"x={x} differs, so overlaps were blended twice");
        }
    }

    [TestMethod]
    public void Render_FilledRectangleCoversItsInteriorAndNothingOutside()
    {
        var annotation = Annotation.Create(
            AnnotationTool.FilledRectangle,
            new CapturePoint(10, 5),
            new CapturePoint(30, 15),
            new AnnotationStyle(Black, 3));

        var rendered = AnnotationRasterizer.Render(Width, Height, WhiteFrame(), [annotation]);

        Assert.AreEqual(0, BlueAt(rendered, 20, 10), "the interior must be fully opaque");
        Assert.AreEqual(255, BlueAt(rendered, 40, 10), "pixels outside the rectangle must be untouched");
    }

    [TestMethod]
    public void Render_ArrowHeadIsNotClippedAwayByTheShaftBounds()
    {
        // A horizontal shaft has a zero height bounding box, but its head reaches
        // well above and below it. Sizing the coverage mask from the shaft alone
        // silently drops the head.
        var annotation = Annotation.Create(
            AnnotationTool.Arrow,
            new CapturePoint(10, 12),
            new CapturePoint(50, 12),
            new AnnotationStyle(Black, 3));

        var rendered = AnnotationRasterizer.Render(Width, Height, WhiteFrame(), [annotation]);

        var headPainted = false;
        for (var y = 0; y < 10 && !headPainted; y++)
        {
            for (var x = 30; x < 55; x++)
            {
                if (IsInked(rendered, x, y))
                {
                    headPainted = true;
                    break;
                }
            }
        }

        Assert.IsTrue(headPainted, "the arrow head must be painted above the shaft");
    }

    [TestMethod]
    public void Render_AnnotationOutsideTheFrameIsDroppedInsteadOfThrowing()
    {
        var annotation = Annotation.Create(
            AnnotationTool.Rectangle,
            new CapturePoint(-500, -500),
            new CapturePoint(-400, -400));

        var rendered = AnnotationRasterizer.Render(Width, Height, WhiteFrame(), [annotation]);

        CollectionAssert.AreEqual(WhiteFrame(), rendered);
    }

    [TestMethod]
    public void Render_RejectsToolsThatNeedTextRendering()
    {
        // Failing loudly keeps a half-drawn export from being mistaken for a
        // complete one while the Win2D draw path is still missing.
        var annotation = Annotation.Create(AnnotationTool.Text, default, default) with { Text = "hello" };

        Assert.ThrowsException<NotSupportedException>(
            () => AnnotationRasterizer.Render(Width, Height, WhiteFrame(), [annotation]));
    }

    [TestMethod]
    public void Render_RejectsAPixelBufferThatDoesNotMatchTheFrame()
    {
        Assert.ThrowsException<ArgumentException>(
            () => AnnotationRasterizer.Render(Width, Height, new byte[16], []));
    }

    private static byte[] RenderLine(LineStyle lineStyle, double strokeWidth = 3)
    {
        var annotation = Annotation.Create(
            AnnotationTool.Line,
            new CapturePoint(5, 10),
            new CapturePoint(55, 10),
            new AnnotationStyle(Black, strokeWidth, lineStyle));

        return AnnotationRasterizer.Render(Width, Height, WhiteFrame(), [annotation]);
    }

    private static byte[] WhiteFrame()
    {
        var frame = new byte[Width * Height * 4];
        Array.Fill(frame, byte.MaxValue);
        return frame;
    }

    private static byte BlueAt(byte[] frame, int x, int y) => frame[(y * Width + x) * 4];

    private static bool IsInked(byte[] frame, int x, int y) => BlueAt(frame, x, y) < 250;
}
