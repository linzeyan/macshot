using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
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
    public void Swatch_IsTheBackgroundTheFramedImageActuallyGets()
    {
        // The reason it is rendered rather than drawn: the swatch a background is picked
        // from is a promise, and the eighteen meshes are exactly where a drawn one would
        // break it — their whole character is that they bulge rather than run in a line.
        foreach (var styleIndex in new[] { 0, 20, BeautifyRenderer.Styles.Count - 1 })
        {
            var (width, _, framed) = BeautifyRenderer.Render(
                10,
                10,
                Solid(10, 10, 40, 40, 40),
                new BeautifyOptions(StyleIndex: styleIndex));

            // Square in, square out, so the same relative position on both is the same
            // place on the gradient. The corner is far enough from the capture that the
            // shadow contributes nothing to it.
            var (_, _, swatch) = BeautifyRenderer.Swatch(styleIndex, width);

            Assert.AreEqual(At(framed, width, 0, 0), At(swatch, width, 0, 0), $"style {styleIndex}");
        }
    }

    [TestMethod]
    public void Swatch_ShowsTheStyleRatherThanOneFlatColour()
    {
        var (_, _, swatch) = BeautifyRenderer.Swatch(0, 28);

        Assert.AreNotEqual(
            At(swatch, 28, 0, 0),
            At(swatch, 28, 27, 27),
            "a 28-point square of one colour would tell the user nothing about the style");
    }

    [TestMethod]
    public void Render_GrowsTheFrameByThePaddingOnEverySide()
    {
        var (width, height, _) = BeautifyRenderer.Render(
            200,
            100,
            Solid(200, 100, 255, 255, 255),
            new BeautifyOptions(Padding: 10));

        // Ten points on each edge.
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
            new BeautifyOptions(Padding: 10, CornerRadius: 0, ShadowRadius: 0));

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
            new BeautifyOptions(StyleIndex: 0, Padding: 10, ShadowRadius: 0));

        // The very corner of the output is background, never capture.
        Assert.AreNotEqual((255, 255, 255), At(pixels, width, 0, 0));
    }

    [TestMethod]
    public void Render_RoundsTheCornersOff()
    {
        var options = new BeautifyOptions(Padding: 10, CornerRadius: 20, ShadowRadius: 0);

        var (width, _, pixels) = BeautifyRenderer.Render(80, 80, Solid(80, 80, 255, 255, 255), options);

        // The capture's own corner sits at (10, 10). With a twenty-point radius that
        // corner is outside the rounded card, so it is background.
        Assert.AreNotEqual((255, 255, 255), At(pixels, width, 10, 10));

        // The middle of the top edge is still inside it.
        Assert.AreEqual((255, 255, 255), At(pixels, width, 50, 11));
    }

    [TestMethod]
    public void Render_CastsAShadowBelowTheCard()
    {
        var lit = new BeautifyOptions(Padding: 16, CornerRadius: 0, ShadowRadius: 0, ShadowOpacity: 0);
        var shaded = lit with { ShadowRadius = 8, ShadowOpacity = 1 };

        var (width, height, withoutShadow) = BeautifyRenderer.Render(80, 80, Solid(80, 80, 255, 255, 255), lit);
        var (_, _, withShadow) = BeautifyRenderer.Render(80, 80, Solid(80, 80, 255, 255, 255), shaded);

        // Just under the bottom edge of the card, which is where the shadow falls.
        var under = At(withShadow, width, width / 2, height - 8);
        var clear = At(withoutShadow, width, width / 2, height - 8);

        Assert.IsTrue(under.Blue < clear.Blue, "The shadow should darken the background beneath the card.");
    }

    /// <summary>
    /// One soft shadow reads as a card hovering some way above the background. What
    /// puts it down on the background is a second, tighter shadow right under the
    /// edge — macshot casts both, and this cast only the ambient one. The two
    /// compositing is the whole effect, so the test is that the darkness immediately
    /// below the edge exceeds what the ambient shadow's own opacity could produce.
    /// </summary>
    [TestMethod]
    public void Render_DeepensTheShadowWhereTheCardMeetsTheBackground()
    {
        const double opacity = 0.5;
        var lit = new BeautifyOptions(Padding: 20, CornerRadius: 0, ShadowRadius: 0, ShadowOpacity: 0);
        var shaded = lit with { ShadowRadius = 8, ShadowOpacity = opacity };

        var (width, _, withoutShadow) = BeautifyRenderer.Render(80, 80, Solid(80, 80, 255, 255, 255), lit);
        var (_, _, withShadow) = BeautifyRenderer.Render(80, 80, Solid(80, 80, 255, 255, 255), shaded);

        // Twenty points of padding, so the card runs to row 99 and row 100 is the first
        // background row under its bottom edge.
        var clear = At(withoutShadow, width, width / 2, 100);
        var under = At(withShadow, width, width / 2, 100);

        Assert.IsTrue(clear.Blue > 0, "The reference pixel has to be lit for the ratio to mean anything.");

        var darkness = 1 - ((double)under.Blue / clear.Blue);
        Assert.IsTrue(
            darkness > opacity + 0.05,
            $"Only the ambient shadow appears to be cast: darkness {darkness:F3} is no more than its opacity {opacity}.");
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
            Padding: 4000,
            CornerRadius: -1,
            ShadowRadius: 800,
            ShadowOpacity: 3).Normalized();

        Assert.AreEqual(BeautifyRenderer.Styles.Count - 1, normalized.StyleIndex);
        Assert.AreEqual(BeautifyOptions.MaximumPadding, normalized.Padding);
        Assert.AreEqual(0, normalized.CornerRadius);
        Assert.AreEqual(BeautifyOptions.MaximumShadowRadius, normalized.ShadowRadius);
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

    [TestMethod]
    public void Styles_MatchTheMacOsCatalogueSoAStyleIndexMeansTheSameBackground()
    {
        // The chosen background is persisted as an index and the two products are meant
        // to agree on what each one names. Dropping or reordering an entry would go
        // unnoticed otherwise: every style renders something plausible.
        Assert.AreEqual(48, BeautifyRenderer.Styles.Count);
        Assert.AreEqual("Ultraviolet", BeautifyRenderer.Styles[0].Name);
        Assert.AreEqual("Ink", BeautifyRenderer.Styles[^1].Name);
    }

    [TestMethod]
    public void Styles_AreDistinctlyNamedSoTheFrameMenuCanBeReadFromTheNamesAlone()
    {
        var names = BeautifyRenderer.Styles.Select(style => style.Name).ToList();
        Assert.AreEqual(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [TestMethod]
    public void Sample_PlacesAStopWhereItsOffsetSaysRatherThanEvenlyAcross()
    {
        // The middle colour of an off-centre three-stop gradient decides which of the
        // two ends the image sits against — the visible difference the offsets exist for.
        var style = new BeautifyStyle(
            "Off centre",
            0,
            new AnnotationColor(0, 0, 0),
            new AnnotationColor(255, 255, 255),
            new AnnotationColor(0, 0, 0))
        {
            Offsets = [0, 0.25, 1],
        };

        Assert.AreEqual(255, style.Sample(0.25).Red);
        Assert.AreEqual(128, style.Sample(0.125).Red);

        // Halfway along is already past the peak, so it is on the way back down.
        Assert.AreEqual(170, style.Sample(0.5).Red);
    }

    [TestMethod]
    public void Sample_FallsBackToEvenSpacingWhenTheOffsetsDoNotMatchTheStops()
    {
        // A miscounted catalogue entry should render evenly rather than throw the first
        // time someone frames a capture with it.
        var style = new BeautifyStyle(
            "Mismatched",
            0,
            new AnnotationColor(0, 0, 0),
            new AnnotationColor(255, 255, 255),
            new AnnotationColor(0, 0, 0))
        {
            Offsets = [0, 1],
        };

        Assert.AreEqual(255, style.Sample(0.5).Red);
    }

    /// <summary>
    /// The padding is a width, not a proportion.
    /// </summary>
    /// <remarks>
    /// This is the rule the port originally had backwards: padding was a fraction of the
    /// shorter side, so the frame grew with the picture and the same slider position gave
    /// a hairline round a screenshot of a dialog and a hand's breadth round one of a whole
    /// display. macshot's is points (<c>OverlayView.swift:443</c>, and a slider from 16 to
    /// 96), and a port whose numbers mean something different from the app it is a port of
    /// disagrees with it at every setting rather than at none.
    /// </remarks>
    [TestMethod]
    public void PaddingFor_IsAWidthInPoints_NotAShareOfTheCapture()
    {
        var options = new BeautifyOptions(Padding: 48);

        Assert.AreEqual(48, BeautifyRenderer.PaddingFor(options));

        // Rounded rather than truncated, which is the difference between a frame that
        // matches the file and one a pixel short of it.
        Assert.AreEqual(33, BeautifyRenderer.PaddingFor(new BeautifyOptions(Padding: 32.6)));
    }

    /// <summary>
    /// The frame is a width in points, so a capture of denser pixels gets more of them.
    /// </summary>
    /// <remarks>
    /// macshot measures the frame against an image that is itself in points, so a 48-point
    /// frame on a Retina capture is 96 pixels wide in the file. This port captures device
    /// pixels, and rasterizing the same 48 as 48 pixels drew a frame little over half the
    /// Mac's on a 175% display — thin enough that the size box, which hangs off the capture
    /// inside the frame, no longer fitted in it and fell outside the gradient instead.
    /// </remarks>
    [TestMethod]
    public void PaddingFor_IsPointsTurnedIntoWhicheverPixelsTheCaptureHas()
    {
        var options = new BeautifyOptions(Padding: 48);

        Assert.AreEqual(48, BeautifyRenderer.PaddingFor(options));
        Assert.AreEqual(84, BeautifyRenderer.PaddingFor(options, 1.75));
        Assert.AreEqual(96, BeautifyRenderer.PaddingFor(options, 2));

        // And the frame that grows by it grows by the same number, so a preview placed
        // from one and drawn by the other cannot disagree.
        var framed = BeautifyRenderer.FrameAround(new CaptureRegion(10, 20, 200, 100), options, 2);

        Assert.AreEqual(200 + (96 * 2), framed.Width);
        Assert.AreEqual(10 - 96, framed.X);
    }

    /// <summary>
    /// A scale a display has not reported yet must not take the frame away.
    /// </summary>
    /// <remarks>
    /// XamlRoot hands back 0 before a window is shown, and a capture framed with it would
    /// be delivered with no frame at all while the row said the frame was on.
    /// </remarks>
    [TestMethod]
    public void PaddingFor_IgnoresAScaleNoDisplayCouldHave()
    {
        var options = new BeautifyOptions(Padding: 48);

        foreach (var nonsense in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        {
            Assert.AreEqual(48, BeautifyRenderer.PaddingFor(options, nonsense), $"scale {nonsense}");
        }
    }

    /// <summary>
    /// A frame asked for on a big capture is the same frame on a small one.
    /// </summary>
    /// <remarks>
    /// The consequence of the rule above, stated where it is visible: two captures framed
    /// at one setting have to carry the same border, or "48" on the slider is not a width
    /// anybody can learn.
    /// </remarks>
    [TestMethod]
    public void FrameAround_AddsTheSameBorderWhateverTheCaptureIs()
    {
        var options = new BeautifyOptions(Padding: 48);

        var small = BeautifyRenderer.FrameAround(new CaptureRegion(0, 0, 100, 80), options);
        var large = BeautifyRenderer.FrameAround(new CaptureRegion(0, 0, 3000, 2000), options);

        Assert.AreEqual(100 + 96, small.Width);
        Assert.AreEqual(3000 + 96, large.Width);
    }

    [TestMethod]
    public void FrameAround_GrowsTheSelectionOutwardsAndLeavesItWhereItWas()
    {
        var selection = new CaptureRegion(300, 200, 200, 100);
        var frame = BeautifyRenderer.FrameAround(selection, new BeautifyOptions(Padding: 10));

        // Ten points on every edge.
        Assert.AreEqual(new CaptureRegion(290, 190, 220, 120), frame);

        // The point of the whole design: the capture has not moved, so everything that
        // measures against the selection — the marks on it, the grips, what a click
        // lands on — is untouched by the frame going on.
        Assert.AreEqual(selection.X, frame.X + 10);
        Assert.AreEqual(selection.Y, frame.Y + 10);
    }

    [TestMethod]
    public void FrameAround_IsTheSizeTheExportActuallyComesOutAt()
    {
        // The preview is placed with FrameAround and the file is made by Render. If the
        // two ever disagree the preview becomes a promise the file breaks, so they are
        // checked against each other rather than each against a number.
        foreach (var padding in new[] { 0.0, 5, 16, 48, BeautifyOptions.MaximumPadding })
        {
            var options = new BeautifyOptions(Padding: padding);
            var (width, height, _) = BeautifyRenderer.Render(200, 130, Solid(200, 130, 0, 0, 0), options);
            var frame = BeautifyRenderer.FrameAround(new CaptureRegion(0, 0, 200, 130), options);

            Assert.AreEqual(width, (int)frame.Width, $"width at a padding of {padding}");
            Assert.AreEqual(height, (int)frame.Height, $"height at a padding of {padding}");
        }
    }

    [TestMethod]
    public void FrameAround_HasNothingToGrowFromAnEmptyRegion()
    {
        Assert.IsTrue(BeautifyRenderer.FrameAround(default).IsEmpty);
    }

    [TestMethod]
    public void Backdrop_IsTheSameSizeAsTheExport()
    {
        var options = new BeautifyOptions(Padding: 10);

        var (framedWidth, framedHeight, _) = BeautifyRenderer.Render(80, 80, Solid(80, 80, 0, 0, 0), options);
        var (previewWidth, previewHeight, _) = BeautifyRenderer.Backdrop(80, 80, options);

        Assert.AreEqual(framedWidth, previewWidth);
        Assert.AreEqual(framedHeight, previewHeight);
    }

    [TestMethod]
    public void Backdrop_LeavesTheCardClearAndCoversEverythingRoundIt()
    {
        var (width, height, pixels) = BeautifyRenderer.Backdrop(
            80,
            80,
            new BeautifyOptions(Padding: 10, CornerRadius: 20, ShadowRadius: 0));

        // The middle of the card is where the capture already is, so the frame gives all
        // of it away.
        Assert.AreEqual(0, Alpha(pixels, width, width / 2, height / 2));

        // The padding is the frame itself, and it has to hide the dimmed screen under it.
        Assert.AreEqual(255, Alpha(pixels, width, 0, 0));
        Assert.AreEqual(255, Alpha(pixels, width, width / 2, 1));

        // And the corner the radius cut off is background in the file, so it is opaque
        // here too — otherwise the preview would show a square corner the file rounds.
        Assert.AreEqual(255, Alpha(pixels, width, 10, 10));
    }

    [TestMethod]
    public void Backdrop_LaidOverTheCaptureIsTheImageTheExportMakes()
    {
        // The one that matters: what the overlay shows is this backdrop composited over
        // the capture already on screen, and what the user gets is Render. If those two
        // are not the same picture, the preview is lying about the file.
        var options = new BeautifyOptions(StyleIndex: 3, Padding: 6, CornerRadius: 8, ShadowRadius: 3.2);
        var capture = Solid(60, 40, 200, 120, 40);

        var (_, _, framed) = BeautifyRenderer.Render(60, 40, capture, options);
        var (_, _, backdrop) = BeautifyRenderer.Backdrop(60, 40, options);

        for (var offset = 0; offset < framed.Length; offset += 4)
        {
            // Source-over with a premultiplied source, against the capture underneath.
            var clear = (255 - backdrop[offset + 3]) / 255.0;

            for (var channel = 0; channel < 3; channel++)
            {
                var composited = backdrop[offset + channel] + (capture[channel] * clear);

                // Two, because each side rounds to a byte at a different point: the
                // export blends once, the preview scales and then composites.
                Assert.IsTrue(
                    Math.Abs(composited - framed[offset + channel]) <= 2,
                    $"pixel {offset / 4} channel {channel}: preview {composited:0.##}, export {framed[offset + channel]}");
            }
        }
    }

    [TestMethod]
    public void Backdrop_PaintsTheStyleThatWasAskedFor()
    {
        var first = BeautifyRenderer.Backdrop(40, 40, new BeautifyOptions(StyleIndex: 0, Padding: 10));
        var second = BeautifyRenderer.Backdrop(40, 40, new BeautifyOptions(StyleIndex: 1, Padding: 10));

        CollectionAssert.AreNotEqual(
            first.Pixels,
            second.Pixels,
            "choosing a background from the picker has to change what the overlay shows");
    }

    [TestMethod]
    public void Backdrop_FollowsThePaddingCornerAndShadowItIsGiven()
    {
        var baseline = new BeautifyOptions(Padding: 8, CornerRadius: 4, ShadowRadius: 2);
        var (_, _, plain) = BeautifyRenderer.Backdrop(40, 40, baseline);

        // Each of the three is a setting the user can change, and each has to reach the
        // preview rather than only the file.
        CollectionAssert.AreNotEqual(
            plain,
            BeautifyRenderer.Backdrop(40, 40, baseline with { CornerRadius = 16 }).Pixels);

        CollectionAssert.AreNotEqual(
            plain,
            BeautifyRenderer.Backdrop(40, 40, baseline with { ShadowRadius = 8 }).Pixels);

        // Padding changes the size, which moves the frame rather than repainting it.
        Assert.AreNotEqual(
            BeautifyRenderer.Backdrop(40, 40, baseline).Width,
            BeautifyRenderer.Backdrop(40, 40, baseline with { Padding = 0.3 }).Width);
    }

    private static int Alpha(byte[] pixels, int width, int column, int row) =>
        pixels[(((row * width) + column) * 4) + 3];
}
