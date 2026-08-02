using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

/// <summary>
/// The highlight tool, which lights a region by taking everything else down rather than
/// by putting anything over it — and the two things that go wrong if the dim is treated
/// as one more mark: it stacks, and it darkens what was drawn to be read.
/// </summary>
[TestClass]
public sealed class SpotlightTests
{
    private const int Size = 400;

    /// <summary>Mid grey, so a dim reads as darker and the white ring as lighter.</summary>
    private static readonly AnnotationColor Grey = new(100, 100, 100);

    private static readonly CaptureRegion Lit = new(100, 100, 200, 200);

    [TestMethod]
    public void TheSpotlight_ReachesTheToolbarNowThatItCanBeDrawn()
    {
        // The bug this feature started as: the tool was in the enum, in the order and in
        // the tooltips, but the strip is built from what the renderer can draw, and this
        // was not on that list. It was a tool nobody could ever pick.
        CollectionAssert.Contains(AnnotationRasterizer.SupportedTools.ToArray(), AnnotationTool.Highlight);
        Assert.IsTrue(
            ToolbarActions.Tools(AnnotationTool.Arrow).Any(item => item.Tool == AnnotationTool.Highlight),
            "the spotlight can be drawn but is still not on the strip");
    }

    [TestMethod]
    public void OutsideTheLight_IsTakenDownByTheStrengthAsked()
    {
        var rendered = Rendered(Grey, Spotlight(Lit, dim: 0.55));

        // Mixed towards black by the dim, not by anything about the colour in hand: the
        // spotlight is the one tool whose mark the drawing colour plays no part in.
        Assert.AreEqual(new AnnotationColor(45, 45, 45), At(rendered, 20, 20));
        Assert.AreEqual(new AnnotationColor(45, 45, 45), At(rendered, Size - 5, Size - 5));
    }

    [TestMethod]
    public void InsideTheLight_IsLeftExactlyAsItWas()
    {
        var rendered = Rendered(Grey, Spotlight(Lit, dim: 0.55));

        Assert.AreEqual(Grey, At(rendered, 150, 250));

        // The middle of the region, which is also the middle of the diagonal from the
        // corner it was dragged from to the corner it was dragged to. The spotlight used
        // to be rasterized as a stroke along that diagonal, so a line in the drawing
        // colour ran across exactly the pixels the tool exists to leave alone.
        Assert.AreEqual(Grey, At(rendered, 200, 200));
    }

    [TestMethod]
    public void TwoOverlappingSpotlights_DoNotDimTheirSurroundingsTwice()
    {
        // A dim laid once per mark would take the surroundings down 0.5 and then 0.5
        // again, leaving a quarter of the capture where the user asked for a half. One
        // pass over the union of them is what keeps two spotlights reading as one
        // brightness.
        var alone = Rendered(Grey, Spotlight(Lit, dim: 0.5));
        var pair = Rendered(
            Grey,
            Spotlight(Lit, dim: 0.5),
            Spotlight(new CaptureRegion(200, 200, 150, 150), dim: 0.5));

        Assert.AreEqual(new AnnotationColor(50, 50, 50), At(alone, 20, 20));
        Assert.AreEqual(At(alone, 20, 20), At(pair, 20, 20));
    }

    [TestMethod]
    public void WhereTwoSpotlightsOverlap_StaysLit()
    {
        // The reason macshot punches the holes out of the dim rather than filling a
        // combined path even-odd: a point inside two rectangles has odd winding, so an
        // even-odd fill would count it as outside both and dim the one place the user
        // pointed at twice over.
        var rendered = Rendered(
            Grey,
            Spotlight(Lit, dim: 0.55),
            Spotlight(new CaptureRegion(200, 200, 150, 150), dim: 0.55));

        Assert.AreEqual(Grey, At(rendered, 250, 250));
    }

    [TestMethod]
    public void TheStrongestSpotlight_SetsTheStrengthForAllOfThem()
    {
        // The dim is a single layer over the capture, and a layer has one opacity. Taking
        // the strongest is macshot's answer: a stack that read as two greys would say
        // there are two brightnesses of "not this part", which is not a distinction the
        // tool makes.
        var rendered = Rendered(
            Grey,
            Spotlight(Lit, dim: 0.25),
            Spotlight(new CaptureRegion(320, 20, 60, 60), dim: 0.75));

        Assert.AreEqual(new AnnotationColor(25, 25, 25), At(rendered, 20, 20));
    }

    [TestMethod]
    public void TheDimGoesUnderTheMarks_WhicheverOrderTheyWereDrawnIn()
    {
        // The point of laying it in a pass of its own. Drawn in list order the dim would
        // fall on every mark placed before it, so an arrow drawn first and a spotlight
        // second would leave the arrow greyed — and an annotation the user cannot read is
        // the one thing an annotation cannot be.
        var rendered = Rendered(
            Grey,
            Mark(new AnnotationColor(255, 0, 0)),
            Spotlight(Lit, dim: 0.55));

        Assert.AreEqual(new AnnotationColor(255, 0, 0), At(rendered, 50, 350));
        Assert.AreEqual(new AnnotationColor(45, 45, 45), At(rendered, 50, 300));
    }

    [TestMethod]
    public void TheRing_MarksWhereTheLightEnds()
    {
        var rendered = Rendered(Grey, Spotlight(Lit, dim: 0.55));

        // White over the lit side of the edge, so the region can be seen to have been
        // chosen rather than merely being where the picture happens to be brighter.
        var ring = At(rendered, (int)Lit.X, 200).Red;
        Assert.IsTrue(ring > Grey.Red, $"the ring must be lighter than the region it encloses, but was {ring}");
    }

    [TestMethod]
    public void ASpotlightTooSmallToAimAt_LightsNothingAndDimsNothing()
    {
        // A press that slipped. Dimming the whole capture for it would hide everything
        // behind a speck of bright pixels too small to find and click on to undo.
        var rendered = Rendered(Grey, Spotlight(new CaptureRegion(200, 200, 1, 1), dim: 0.55));

        Assert.AreEqual(Grey, At(rendered, 20, 20));
    }

    /// <summary>A spotlight over <paramref name="region"/>, dimming the rest by <paramref name="dim"/>.</summary>
    private static Annotation Spotlight(CaptureRegion region, double dim) => Annotation.Create(
        AnnotationTool.Highlight,
        new CapturePoint(region.X, region.Y),
        new CapturePoint(region.Right, region.Bottom),
        new AnnotationStyle(new AnnotationColor(0, 0, 0), 3) { DimOpacity = dim });

    /// <summary>A fat opaque line well outside the lit region, for the pass-order test.</summary>
    private static Annotation Mark(AnnotationColor color) => Annotation.Create(
        AnnotationTool.Line,
        new CapturePoint(20, 350),
        new CapturePoint(80, 350),
        new AnnotationStyle(color, 8));

    private static byte[] Rendered(AnnotationColor background, params Annotation[] annotations) =>
        AnnotationRasterizer.Render(Size, Size, Solid(background), annotations);

    private static byte[] Solid(AnnotationColor color)
    {
        var frame = new byte[Size * Size * 4];
        for (var offset = 0; offset < frame.Length; offset += 4)
        {
            frame[offset] = color.Blue;
            frame[offset + 1] = color.Green;
            frame[offset + 2] = color.Red;
            frame[offset + 3] = byte.MaxValue;
        }

        return frame;
    }

    private static AnnotationColor At(byte[] frame, int x, int y)
    {
        var offset = ((y * Size) + x) * 4;
        return new AnnotationColor(frame[offset + 2], frame[offset + 1], frame[offset]);
    }
}
