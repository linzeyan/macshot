using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class BeautifyRendererTests
{
    private static byte[] Solid(int width, int height, byte blue, byte green, byte red)
    {
        var pixels = new byte[width * height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = blue;
            pixels[offset + 1] = green;
            pixels[offset + 2] = red;
            pixels[offset + 3] = byte.MaxValue;
        }

        return pixels;
    }

    private static (int Blue, int Green, int Red) At(byte[] pixels, int width, int column, int row)
    {
        var offset = ((row * width) + column) * 4;
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2]);
    }

    [TestMethod]
    public void Render_GrowsTheFrameByThePaddingOnEverySide()
    {
        var (width, height, _) = BeautifyRenderer.Render(
            200,
            100,
            Solid(200, 100, 255, 255, 255),
            new BeautifyOptions(Padding: 0.1));

        // The shorter side is 100, so a tenth of it is ten pixels on each edge.
        Assert.AreEqual(220, width);
        Assert.AreEqual(120, height);
    }

    [TestMethod]
    public void Render_KeepsTheCapturePixelsUntouchedInTheMiddle()
    {
        var (width, _, pixels) = BeautifyRenderer.Render(
            80,
            80,
            Solid(80, 80, 10, 20, 30),
            new BeautifyOptions(Padding: 0.125, CornerRadius: 0, ShadowRadius: 0));

        // Ten pixels of padding, so the capture's own top-left pixel lands at (10, 10)
        // and nothing here resamples it.
        Assert.AreEqual((10, 20, 30), At(pixels, width, 40, 40));
        Assert.AreEqual((10, 20, 30), At(pixels, width, 11, 11));
    }

    [TestMethod]
    public void Render_PutsTheBackgroundOutsideTheCapture()
    {
        var (width, _, pixels) = BeautifyRenderer.Render(
            80,
            80,
            Solid(80, 80, 255, 255, 255),
            new BeautifyOptions(StyleIndex: 0, Padding: 0.125, ShadowRadius: 0));

        // The very corner of the output is background, never capture.
        Assert.AreNotEqual((255, 255, 255), At(pixels, width, 0, 0));
    }

    [TestMethod]
    public void Render_RoundsTheCornersOff()
    {
        var options = new BeautifyOptions(Padding: 0.125, CornerRadius: 0.25, ShadowRadius: 0);

        var (width, _, pixels) = BeautifyRenderer.Render(80, 80, Solid(80, 80, 255, 255, 255), options);

        // The capture's own corner sits at (10, 10). With a radius of a quarter of the
        // shorter side that corner is outside the rounded card, so it is background.
        Assert.AreNotEqual((255, 255, 255), At(pixels, width, 10, 10));

        // The middle of the top edge is still inside it.
        Assert.AreEqual((255, 255, 255), At(pixels, width, 50, 11));
    }

    [TestMethod]
    public void Render_CastsAShadowBelowTheCard()
    {
        var lit = new BeautifyOptions(Padding: 0.2, CornerRadius: 0, ShadowRadius: 0, ShadowOpacity: 0);
        var shaded = lit with { ShadowRadius = 0.1, ShadowOpacity = 1 };

        var (width, height, withoutShadow) = BeautifyRenderer.Render(80, 80, Solid(80, 80, 255, 255, 255), lit);
        var (_, _, withShadow) = BeautifyRenderer.Render(80, 80, Solid(80, 80, 255, 255, 255), shaded);

        // Just under the bottom edge of the card, which is where the shadow falls.
        var under = At(withShadow, width, width / 2, height - 8);
        var clear = At(withoutShadow, width, width / 2, height - 8);

        Assert.IsTrue(under.Blue < clear.Blue, "The shadow should darken the background beneath the card.");
    }

    [TestMethod]
    public void Sample_RunsFromTheFirstStopToTheLast()
    {
        var style = new BeautifyStyle("Test", 0, new AnnotationColor(0, 0, 0), new AnnotationColor(100, 200, 40));

        Assert.AreEqual(new AnnotationColor(0, 0, 0), style.Sample(0));
        Assert.AreEqual(new AnnotationColor(100, 200, 40), style.Sample(1));
        Assert.AreEqual(new AnnotationColor(50, 100, 20), style.Sample(0.5));
    }

    [TestMethod]
    public void Sample_ReachesTheMiddleStopOfAThreeStopStyle()
    {
        var style = new BeautifyStyle(
            "Test",
            0,
            new AnnotationColor(0, 0, 0),
            new AnnotationColor(10, 20, 30),
            new AnnotationColor(255, 255, 255));

        Assert.AreEqual(new AnnotationColor(10, 20, 30), style.Sample(0.5));
    }

    [TestMethod]
    public void Sample_ClampsRatherThanRunningOffTheEnds()
    {
        var style = new BeautifyStyle("Test", 0, new AnnotationColor(1, 2, 3), new AnnotationColor(9, 9, 9));

        Assert.AreEqual(new AnnotationColor(1, 2, 3), style.Sample(-5));
        Assert.AreEqual(new AnnotationColor(9, 9, 9), style.Sample(5));
    }

    [TestMethod]
    public void Normalized_PullsAHandEditedFileBackIntoRange()
    {
        var normalized = new BeautifyOptions(
            StyleIndex: 9999,
            Padding: 4,
            CornerRadius: -1,
            ShadowRadius: 8,
            ShadowOpacity: 3).Normalized();

        Assert.AreEqual(BeautifyRenderer.Styles.Count - 1, normalized.StyleIndex);
        Assert.AreEqual(0.5, normalized.Padding);
        Assert.AreEqual(0, normalized.CornerRadius);
        Assert.AreEqual(0.25, normalized.ShadowRadius);
        Assert.AreEqual(1, normalized.ShadowOpacity);
    }

    [TestMethod]
    public void Render_WithNoFrameAtAllIsTheCaptureItself()
    {
        var original = Solid(20, 20, 7, 8, 9);

        var (width, height, pixels) = BeautifyRenderer.Render(
            20,
            20,
            original,
            new BeautifyOptions(Padding: 0, CornerRadius: 0, ShadowRadius: 0));

        Assert.AreEqual(20, width);
        Assert.AreEqual(20, height);
        CollectionAssert.AreEqual(original, pixels);
    }

    [TestMethod]
    public void Render_RejectsABufferThatIsNotTheFrame()
    {
        Assert.ThrowsException<ArgumentException>(() => BeautifyRenderer.Render(4, 4, new byte[10]));
    }

    [TestMethod]
    public void Styles_AreAllNamedAndHaveSomethingToInterpolate()
    {
        Assert.IsTrue(BeautifyRenderer.Styles.Count > 0);
        Assert.IsTrue(BeautifyRenderer.Styles.All(style => !string.IsNullOrWhiteSpace(style.Name)));
        Assert.IsTrue(BeautifyRenderer.Styles.All(style => style.Stops.Length >= 2));
    }
}
