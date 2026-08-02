using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

/// <summary>
/// What the three fill styles put inside a closed shape.
/// </summary>
/// <remarks>
/// Every shape here is drawn black on white and sampled at its middle, well clear of the
/// line around it, and on that line. Those two points are what separate the styles: an
/// outline leaves the middle white, a solid inks it fully, and the wash between them inks
/// it part way — which is the one macshot has that a fill switch could not express, and
/// so the one worth a test that can fail.
/// </remarks>
[TestClass]
public sealed class ShapeFillTests
{
    private const int Size = 64;

    /// <summary>The middle of the shape, far from any edge of it.</summary>
    private const int Middle = 32;

    [TestMethod]
    public void Stroke_LeavesTheInsideAlone()
    {
        var rendered = Render(Shape(AnnotationTool.Rectangle, ShapeFill.Stroke));

        Assert.AreEqual(byte.MaxValue, Ink(rendered, Middle, Middle), "an outlined shape has nothing in it");
        Assert.AreNotEqual(byte.MaxValue, Ink(rendered, 8, Middle), "but it does have a line round it");
    }

    [TestMethod]
    public void Fill_InksTheInsideAtFullStrength()
    {
        var rendered = Render(Shape(AnnotationTool.Rectangle, ShapeFill.Fill));

        Assert.AreEqual(0, Ink(rendered, Middle, Middle), "a solid shape is the colour all the way through");
    }

    [TestMethod]
    public void StrokeAndFill_WashesTheInsideAndKeepsTheLineFull()
    {
        var rendered = Render(Shape(AnnotationTool.Rectangle, ShapeFill.StrokeAndFill));

        var inside = Ink(rendered, Middle, Middle);
        var edge = Ink(rendered, 8, Middle);

        // The whole point of the middle style: the region is pointed at without being
        // hidden, so what is under it still shows through the wash.
        Assert.IsTrue(inside > 0, "the wash is translucent, not solid");
        Assert.IsTrue(inside < byte.MaxValue, "but it is there");
        Assert.IsTrue(edge < inside, "and the line over it is drawn at full strength");
    }

    [TestMethod]
    public void TheEllipseFillsToo()
    {
        // Same switch, other shape. A fill implemented for the rectangle alone is the
        // likely mistake, and it is invisible until someone draws an oval.
        var outlined = Render(Shape(AnnotationTool.Ellipse, ShapeFill.Stroke));
        var solid = Render(Shape(AnnotationTool.Ellipse, ShapeFill.Fill));

        Assert.AreEqual(byte.MaxValue, Ink(outlined, Middle, Middle));
        Assert.AreEqual(0, Ink(solid, Middle, Middle));
    }

    [TestMethod]
    public void ASolidShapeStillTakesItsHalo()
    {
        // macshot strokes the halo around a filled shape rather than under it, so it
        // shows as a ring outside the fill. Composited into the same mask it would land
        // exactly beneath the fill and never be seen — which is what this asks about.
        var plain = Render(Shape(AnnotationTool.Rectangle, ShapeFill.Fill));
        var haloed = Render(Shape(
            AnnotationTool.Rectangle,
            ShapeFill.Fill,
            halo: new AnnotationColor(255, 0, 0)));

        // Just outside the shape's left edge, where only a ring wider than the shape can
        // reach.
        Assert.AreEqual(byte.MaxValue, Ink(plain, 5, Middle), "nothing reaches outside a plain fill");
        Assert.AreNotEqual(byte.MaxValue, Ink(haloed, 5, Middle), "the halo does");
    }

    [TestMethod]
    public void AFileRemembersWhichWayItWasFilled()
    {
        var shape = Shape(AnnotationTool.Ellipse, ShapeFill.StrokeAndFill);

        var reopened = AnnotationFile.Read(AnnotationFile.Write([shape]));

        Assert.AreEqual(1, reopened.Count);
        Assert.AreEqual(ShapeFill.StrokeAndFill, reopened[0].Style.ShapeFill);
    }

    private static Annotation Shape(AnnotationTool tool, ShapeFill fill, AnnotationColor? halo = null) =>
        Annotation.Create(
            tool,
            new CapturePoint(8, 8),
            new CapturePoint(56, 56),
            new AnnotationStyle(new AnnotationColor(0, 0, 0), 4, ShapeFill: fill) { Outline = halo });

    private static byte[] Render(Annotation shape)
    {
        var blank = new byte[Size * Size * 4];
        Array.Fill(blank, byte.MaxValue);
        return AnnotationRasterizer.Render(Size, Size, blank, [shape]);
    }

    /// <summary>The blue byte at a point: 255 where nothing was drawn, 0 where black was.</summary>
    private static byte Ink(byte[] pixels, int column, int row) => pixels[((row * Size) + column) * 4];
}
