using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Windows.Storage;

namespace Macshot.Windows.Recording.Tests;

/// <summary>
/// What the export does to the <em>picture</em>: where a zoom, a censor and a caption
/// land, and that each is there only while it is meant to be.
/// </summary>
/// <remarks>
/// The recording is four colours in four corners, so every assertion is a region average:
/// a zoom onto one corner fills the frame with that corner's colour, a censor over one
/// corner changes that corner and no other, and a caption's pixels appear where its
/// rectangle is. Placement and timing are the two things a preview cannot check — the
/// editor plays the source, not the buffer, which is how an export that came out upside
/// down shipped once already.
/// </remarks>
[TestClass]
public sealed class VideoOverlayExportTests
{
    private const int SourceSeconds = 6;

    /// <summary>Inside the effects' window, and clear of both ramps.</summary>
    private const double During = 3.0;

    /// <summary>Before any of them starts.</summary>
    private const double Before = 0.5;

    private StorageFolder _scratch = null!;

    [TestInitialize]
    public async Task CreateScratchAsync() => _scratch = await TestExport.ScratchAsync();

    [TestCleanup]
    public async Task DeleteScratchAsync() => await _scratch.DeleteAsync();

    /// <summary>
    /// A zoom magnifies about the point it was given, not about the middle of the frame.
    /// Centred on the top-left quadrant at 4x, the crop is inside that quadrant and the
    /// whole output frame is its colour — which a zoom that ignored the centre, or took
    /// it as a fraction of the output rather than of the source, would not produce.
    /// </summary>
    [TestMethod]
    public async Task WriteAsync_MagnifiesAboutTheCentreTheSegmentCarries()
    {
        var effects = new VideoEffects();
        effects.Zooms.Add(new VideoZoomSegment(2, 4, 4.0, new CapturePoint(0.25, 0.25), 0, 0));

        var written = await ExportAsync(effects);

        var zoomed = (await TestVideo.FrameAtAsync(written, During)).Average(0, 0, 1, 1);
        var plain = (await TestVideo.FrameAtAsync(written, Before)).Average(0, 0, 1, 1);

        Assert.IsTrue(
            TestVideo.Distance(zoomed, TestVideo.TopLeft) < 40,
            $"the zoomed frame should be the top-left colour, and averaged {zoomed}");
        Assert.IsTrue(
            TestVideo.Distance(plain, TestVideo.TopLeft) > 80,
            $"the frame outside the zoom should still show all four quadrants, and averaged {plain}");
    }

    /// <summary>
    /// A censor covers its own rectangle and leaves the rest of the frame alone. The
    /// failure this rules out is a rectangle mapped to the wrong corner — which the
    /// vertical flip on the way to the encoder made happen once, and which looks
    /// deliberate in a still.
    /// </summary>
    [TestMethod]
    public async Task WriteAsync_HidesTheRectangleItWasGivenAndNoOtherPartOfTheFrame()
    {
        var effects = new VideoEffects();
        effects.Censors.Add(new VideoCensorSegment(
            2, 4, new CaptureRegion(0, 0, 0.5, 0.5), VideoCensorStyle.Solid, 0, 0));

        var written = await ExportAsync(effects);
        var during = await TestVideo.FrameAtAsync(written, During);
        var before = await TestVideo.FrameAtAsync(written, Before);

        Assert.IsTrue(
            TestVideo.Distance(during.Average(0, 0, 0.5, 0.5), TestVideo.TopLeft) > 80,
            "the censored quadrant still shows what was under it");
        Assert.IsTrue(
            TestVideo.Distance(during.Average(0.5, 0, 0.5, 0.5), TestVideo.TopRight) < 40,
            "the censor reached a quadrant it was not drawn over");
        Assert.IsTrue(
            TestVideo.Distance(during.Average(0, 0.5, 0.5, 0.5), TestVideo.BottomLeft) < 40,
            "the censor reached a quadrant it was not drawn over");
        Assert.IsTrue(
            TestVideo.Distance(before.Average(0, 0, 0.5, 0.5), TestVideo.TopLeft) < 40,
            "the censor is covering the frame before its segment starts");
    }

    /// <summary>
    /// A caption is composited where its rectangle is, and only while it runs. Its pixels
    /// come from the editor rather than from here, so what this checks is the placement
    /// and the window — the two things that are the compositor's own.
    /// </summary>
    [TestMethod]
    public async Task WriteAsync_DrawsACaptionInItsOwnRectangleForItsOwnStretch()
    {
        var white = new byte[TestVideo.Width / 2 * (TestVideo.Height / 2) * 4];
        Array.Fill(white, byte.MaxValue);

        var segment = new VideoTextSegment(
            2,
            4,
            new CaptureRegion(0, 0, 0.5, 0.5),
            "macshot",
            VideoTextSegment.DefaultFontSize,
            Bold: false,
            Italic: false,
            new AnnotationColor(255, 255, 255),
            VideoTextBackground.None,
            new AnnotationColor(0, 0, 0),
            VideoTextAlignment.Centre,
            FadeIn: 0,
            FadeOut: 0);

        var caption = new VideoCaption(
            segment,
            new FrameOverlay.VideoCaptionRaster(TestVideo.Width / 2, TestVideo.Height / 2, white));

        var written = await ExportAsync(new VideoEffects(), [caption]);
        var during = await TestVideo.FrameAtAsync(written, During);
        var before = await TestVideo.FrameAtAsync(written, Before);

        Assert.IsTrue(
            TestVideo.Distance(during.Average(0, 0, 0.5, 0.5), (255, 255, 255)) < 40,
            "the caption was not drawn where its rectangle is");
        Assert.IsTrue(
            TestVideo.Distance(during.Average(0.5, 0, 0.5, 0.5), TestVideo.TopRight) < 40,
            "the caption spread outside its rectangle");
        Assert.IsTrue(
            TestVideo.Distance(before.Average(0, 0, 0.5, 0.5), TestVideo.TopLeft) < 40,
            "the caption is on screen before its segment starts");
    }

    private async Task<StorageFile> ExportAsync(
        VideoEffects effects, IReadOnlyList<VideoCaption>? captions = null) =>
        await TestExport.RunAsync(
            _scratch,
            await TestVideo.WriteQuadrantsAsync(_scratch, SourceSeconds),
            SourceSeconds,
            effects,
            captions);
}
