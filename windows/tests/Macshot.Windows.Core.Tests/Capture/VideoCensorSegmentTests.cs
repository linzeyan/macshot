using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class VideoCensorSegmentTests
{
    private const double Tolerance = 1e-9;

    /// <summary>
    /// A redaction that appears between one frame and the next reads as a glitch and
    /// draws the eye to exactly the thing being hidden. The ramp is what stops that, so
    /// the strength must be nothing at both edges and all of it in the middle.
    /// </summary>
    [TestMethod]
    public void OpacityAt_RampsInAndOutRatherThanSwitchingOn()
    {
        var censor = new VideoCensorSegment(
            1,
            3,
            VideoCensorSegment.DefaultRect,
            VideoCensorStyle.Blur,
            VideoCensorSegment.DefaultFade,
            VideoCensorSegment.DefaultFade);

        Assert.AreEqual(0, censor.OpacityAt(1), Tolerance);
        Assert.AreEqual(1, censor.OpacityAt(2), Tolerance);
        Assert.AreEqual(0, censor.OpacityAt(3), Tolerance);
    }

    /// <summary>
    /// Outside the segment nothing may be hidden at all. A censor that leaked past its
    /// own end would blur material the user never marked, and on a screen recording that
    /// is content they expected to be readable.
    /// </summary>
    [TestMethod]
    public void OpacityAt_HidesNothingOutsideTheSegment()
    {
        var censor = VideoCensorSegment.Placed(5, 20);

        Assert.AreEqual(0, censor.OpacityAt(censor.Start - 0.001), Tolerance);
        Assert.AreEqual(0, censor.OpacityAt(censor.End + 0.001), Tolerance);
    }

    /// <summary>
    /// A censor dragged shorter than two of its own ramps would never reach full
    /// strength, so it would never actually hide anything — which is a redaction that
    /// looks applied and is not. The ramps must be rescaled when the length changes.
    /// </summary>
    [TestMethod]
    public void WithEnd_RescalesTheRampsSoAShortenedCensorStillHidesCompletely()
    {
        var censor = VideoCensorSegment.Placed(5, 20).WithEnd(5.4, 20);

        Assert.AreEqual(1, censor.OpacityAt((censor.Start + censor.End) / 2), Tolerance);
    }

    /// <summary>
    /// A rectangle dragged off the edge of the frame must come back rather than address
    /// pixels that do not exist, and it must never collapse to nothing — a zero-area
    /// censor hides nothing and leaves no handle to make it bigger again.
    /// </summary>
    [TestMethod]
    public void ClampRect_KeepsTheRectangleInsideTheFrameAndBigEnoughToGrabAgain()
    {
        var pushed = VideoCensorSegment.ClampRect(new CaptureRegion(0.9, 0.9, 0.5, 0.5));

        Assert.IsTrue(pushed.X >= 0);
        Assert.IsTrue(pushed.Y >= 0);
        Assert.AreEqual(1, pushed.Right, 1e-6);
        Assert.AreEqual(1, pushed.Bottom, 1e-6);

        var collapsed = VideoCensorSegment.ClampRect(new CaptureRegion(0.5, 0.5, 0, 0));
        Assert.AreEqual(VideoCensorSegment.MinRectSize, collapsed.Width, Tolerance);
        Assert.AreEqual(VideoCensorSegment.MinRectSize, collapsed.Height, Tolerance);
    }

    /// <summary>
    /// Two censors hiding two different things at the same moment is the ordinary case,
    /// unlike two zooms. The band must offer the whole recording rather than a gap.
    /// </summary>
    [TestMethod]
    public void GapFor_LetsCensorsStackWhereZoomsMayNot()
    {
        var effects = new VideoEffects();
        effects.Zooms.Add(VideoZoomSegment.Placed(5, 20));
        effects.Censors.Add(VideoCensorSegment.Placed(5, 20));

        Assert.IsNotNull(effects.GapFor(VideoEffectKind.Censor, 5, 20));
        Assert.IsNull(effects.GapFor(VideoEffectKind.Zoom, 5, 20));
    }
}

[TestClass]
public sealed class VideoTextSegmentTests
{
    private const double Tolerance = 1e-9;

    /// <summary>
    /// A caption is stated at a size against a 1080-tall frame so that it keeps its
    /// proportion whatever the export is scaled to. Half the reference height must
    /// therefore halve the glyphs — otherwise a caption sized on a preview would swamp a
    /// small export and disappear on a large one.
    /// </summary>
    [TestMethod]
    public void PixelFontSize_ScalesWithTheFrameSoACaptionKeepsItsProportion()
    {
        Assert.AreEqual(48, VideoTextSegment.PixelFontSize(48, 1080, rectHeight: 1000), Tolerance);
        Assert.AreEqual(24, VideoTextSegment.PixelFontSize(48, 540, rectHeight: 1000), Tolerance);
        Assert.AreEqual(96, VideoTextSegment.PixelFontSize(48, 2160, rectHeight: 1000), Tolerance);
    }

    /// <summary>
    /// Glyphs taller than the box they were given have their descenders cut off, and the
    /// user sees a caption that looks broken rather than one that is too large. The size
    /// is capped by the rectangle it has to fit inside.
    /// </summary>
    [TestMethod]
    public void PixelFontSize_CapsTheGlyphsToTheRectangleSoDescendersAreNotCutOff()
    {
        Assert.AreEqual(39, VideoTextSegment.PixelFontSize(96, 1080, rectHeight: 50), Tolerance);
    }

    /// <summary>
    /// A caption cleared to nothing would rasterize as an empty pill sitting on the
    /// picture, which reads as a rendering fault. Deleting the segment is how a caption
    /// is removed; emptying its text is not.
    /// </summary>
    [TestMethod]
    public void WithText_RefusesToLeaveACaptionWithNothingInIt()
    {
        var caption = VideoTextSegment.Placed(5, 20);

        Assert.AreEqual(VideoTextSegment.DefaultText, caption.WithText("   ").Text);
        Assert.AreEqual(VideoTextSegment.DefaultText, caption.WithText(null).Text);
        Assert.AreEqual("Look here", caption.WithText("Look here").Text);
    }

    /// <summary>
    /// A caption fades like a censor does, and for the same reason: text that appears
    /// between two frames is read as a subtitle burned in by accident.
    /// </summary>
    [TestMethod]
    public void OpacityAt_RampsInAndOutLikeEveryOtherFadingEffect()
    {
        var caption = new VideoTextSegment(
            1,
            3,
            VideoTextSegment.DefaultRect,
            "Hi",
            VideoTextSegment.DefaultFontSize,
            Bold: true,
            Italic: false,
            VideoTextSegment.SystemFontFamily,
            VideoTextSegment.DefaultTextColor,
            VideoTextBackground.Rounded,
            VideoTextSegment.DefaultBackgroundColor,
            OutlineEnabled: false,
            VideoTextSegment.DefaultOutlineColor,
            VideoTextSegment.DefaultOutlineWidth,
            VideoTextAlignment.Centre,
            VideoTextSegment.DefaultFade,
            VideoTextSegment.DefaultFade);

        Assert.AreEqual(0, caption.OpacityAt(1), Tolerance);
        Assert.AreEqual(1, caption.OpacityAt(2), Tolerance);
        Assert.AreEqual(0, caption.OpacityAt(3), Tolerance);
    }

    /// <summary>
    /// macshot's default caption sits across the lower third, which is where a viewer
    /// expects one and where it covers least of a screen recording. A default that
    /// landed in the middle would have to be dragged off the content every time.
    /// </summary>
    [TestMethod]
    public void Placed_StartsInTheLowerThirdWhereACaptionBelongs()
    {
        var caption = VideoTextSegment.Placed(5, 20);

        Assert.IsTrue(caption.Rect.Y > 0.5);
        Assert.IsTrue(caption.Rect.Bottom <= 1);
        Assert.AreEqual(VideoTextAlignment.Centre, caption.Alignment);
    }

    /// <summary>
    /// A new caption arrives set in the interface's own face with no rim round it, which is
    /// macshot's default and the only one that is safe: a caption that came up outlined
    /// would have to be switched off by everybody who did not want one, and a caption that
    /// came up in a named family would be set in a face the next machine may not have.
    /// </summary>
    [TestMethod]
    public void Placed_LeavesTheFaceToTheSystemAndTheOutlineOff()
    {
        var caption = VideoTextSegment.Placed(5, 20);

        Assert.AreEqual(VideoTextSegment.SystemFontFamily, caption.FontFamily);
        Assert.IsTrue(caption.UsesSystemFont);
        Assert.IsFalse(caption.OutlineEnabled);
        Assert.AreEqual(VideoTextSegment.DefaultOutlineColor, caption.OutlineColor);
        Assert.AreEqual(VideoTextSegment.DefaultOutlineWidth, caption.OutlineWidth, Tolerance);
    }

    /// <summary>
    /// The rim is stated in points against a 1080-tall frame for the same reason the size
    /// is, so the two have to scale together: a rim that kept its pixel width while the
    /// glyphs quadrupled would be a hairline at 4K and a slab at 360, and the caption would
    /// stop looking like the one the user set up on the preview.
    /// </summary>
    [TestMethod]
    public void OutlinePixels_ScaleWithTheFrameExactlyAsTheFontSizeDoes()
    {
        var caption = VideoTextSegment.Placed(5, 20) with { OutlineEnabled = true, OutlineWidth = 4 };

        Assert.AreEqual(4, caption.OutlinePixels(1080), Tolerance);
        Assert.AreEqual(8, caption.OutlinePixels(2160), Tolerance);

        // The same ratio the glyphs grow by, measured against a rectangle tall enough that
        // nothing is capped: the rim tracking the frame is only useful if it tracks the
        // glyphs it surrounds.
        Assert.AreEqual(
            VideoTextSegment.PixelFontSize(48, 2160, rectHeight: 4000)
                / VideoTextSegment.PixelFontSize(48, 1080, rectHeight: 4000),
            caption.OutlinePixels(2160) / caption.OutlinePixels(1080),
            Tolerance);
    }

    /// <summary>
    /// Switching the outline off has to make the colour and the width unreachable, not
    /// merely ignored somewhere downstream. A caption keeps both while the switch is off so
    /// that turning it back on restores what was set — and if anything drawing the caption
    /// consulted them anyway, the export would carry a rim nobody asked for.
    /// </summary>
    [TestMethod]
    public void OutlinePixels_AreNothingWhileTheOutlineIsOffWhateverItsColourAndWidthHold()
    {
        var plain = VideoTextSegment.Placed(5, 20);
        var loud = plain with
        {
            OutlineColor = new AnnotationColor(255, 0, 0),
            OutlineWidth = VideoTextSegment.MaxOutlineWidth,
        };

        Assert.AreEqual(0, plain.OutlinePixels(1080), Tolerance);
        Assert.AreEqual(0, loud.OutlinePixels(1080), Tolerance);
        Assert.AreEqual(0, loud.OutlinePixels(2160), Tolerance);
    }

    /// <summary>
    /// Both sizes are held to the ends of macshot's sliders. The ceilings are what matter:
    /// the size has no ceiling of its own — <see cref="VideoTextSegment.PixelFontSize"/>
    /// caps by the rectangle, so a four-thousand-point caption would rasterize a bitmap the
    /// size of the frame — and a rim past the stem thickness fills the counters of the
    /// glyphs in and makes the caption less readable rather than more.
    /// </summary>
    [TestMethod]
    public void WithFontSizeAndWithOutlineWidth_HoldBothToTheEndsOfTheSliderThatSetsThem()
    {
        var caption = VideoTextSegment.Placed(5, 20);

        Assert.AreEqual(VideoTextSegment.MaxFontSize, caption.WithFontSize(4000).FontSize, Tolerance);
        Assert.AreEqual(VideoTextSegment.MinFontSize, caption.WithFontSize(0).FontSize, Tolerance);
        Assert.AreEqual(64, caption.WithFontSize(64).FontSize, Tolerance);

        Assert.AreEqual(VideoTextSegment.MaxOutlineWidth, caption.WithOutlineWidth(99).OutlineWidth, Tolerance);
        Assert.AreEqual(VideoTextSegment.MinOutlineWidth, caption.WithOutlineWidth(-1).OutlineWidth, Tolerance);
        Assert.AreEqual(3, caption.WithOutlineWidth(3).OutlineWidth, Tolerance);
    }

    /// <summary>
    /// The sentinel and an unset family both mean the interface's own face. The rasterizer
    /// asks this rather than comparing strings itself, because a caption decoded from
    /// anywhere but this row can carry an empty family, and a caption set in a face named
    /// by the empty string is one that renders in nothing.
    /// </summary>
    [TestMethod]
    public void UsesSystemFont_TreatsAnUnsetFamilyAsTheSystemOne()
    {
        var caption = VideoTextSegment.Placed(5, 20);

        Assert.IsTrue((caption with { FontFamily = VideoTextSegment.SystemFontFamily }).UsesSystemFont);
        Assert.IsTrue((caption with { FontFamily = string.Empty }).UsesSystemFont);
        Assert.IsTrue((caption with { FontFamily = "  " }).UsesSystemFont);
        Assert.IsFalse((caption with { FontFamily = "Impact" }).UsesSystemFont);
    }
}
