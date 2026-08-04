using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class VideoOverlayGeometryTests
{
    private const double Tolerance = 1e-6;

    /// <summary>
    /// A censor or a caption is stated as a fraction of the source frame so it survives
    /// the export being scaled. With no zoom running it must land at the same fraction of
    /// the output, whatever size that is — the alternative is a redaction that slides off
    /// what it was covering the moment a user picks 50%.
    /// </summary>
    [TestMethod]
    public void OutputRect_LandsAtTheSameFractionOfAnExportScaledToADifferentSize()
    {
        var whole = new CaptureRegion(0, 0, 1920, 1080);
        var rect = VideoOverlayGeometry.OutputRect(
            new CaptureRegion(0.25, 0.5, 0.5, 0.25),
            whole,
            1920,
            1080,
            960,
            540);

        Assert.AreEqual(240, rect.X, Tolerance);
        Assert.AreEqual(270, rect.Y, Tolerance);
        Assert.AreEqual(480, rect.Width, Tolerance);
        Assert.AreEqual(135, rect.Height, Tolerance);
    }

    /// <summary>
    /// Under a zoom the rectangle has to follow the content it was drawn over, which
    /// means it is magnified and shifted by exactly what the zoom did to the picture. A
    /// rectangle left where it was would drift off the thing it hides precisely while the
    /// zoom is making that thing larger and more readable.
    /// </summary>
    [TestMethod]
    public void OutputRect_FollowsTheContentWhenAZoomIsRunning()
    {
        // A 2× zoom on the middle of the frame: the crop is the middle half.
        var crop = new CaptureRegion(480, 270, 960, 540);
        var rect = VideoOverlayGeometry.OutputRect(
            new CaptureRegion(0.25, 0.25, 0.5, 0.5),
            crop,
            1920,
            1080,
            1920,
            1080);

        // The rectangle's own corner was at (480, 270), which the crop puts at the
        // output's origin, and its size doubles with everything else.
        Assert.AreEqual(0, rect.X, Tolerance);
        Assert.AreEqual(0, rect.Y, Tolerance);
        Assert.AreEqual(1920, rect.Width, Tolerance);
        Assert.AreEqual(1080, rect.Height, Tolerance);
    }

    /// <summary>
    /// A rectangle the zoom pushed off the frame must come back as a rectangle outside
    /// the output rather than as one clamped onto it — a censor whose subject has left
    /// the shot has to leave with it, not slide to the nearest edge and sit there.
    /// </summary>
    [TestMethod]
    public void OutputRect_LetsARectangleTheZoomPushedOffScreenLeaveTheFrame()
    {
        var crop = new CaptureRegion(960, 540, 960, 540);
        var rect = VideoOverlayGeometry.OutputRect(
            new CaptureRegion(0.05, 0.05, 0.1, 0.1),
            crop,
            1920,
            1080,
            1920,
            1080);

        Assert.IsTrue(rect.Right < 0);
        Assert.IsTrue(rect.Bottom < 0);
    }

    /// <summary>
    /// The preview control letterboxes the video, so a rectangle dragged over it is not a
    /// rectangle on the frame. Getting this wrong shifts every censor by the width of one
    /// bar — invisible in the editor and obvious in the exported file.
    /// </summary>
    [TestMethod]
    public void Letterbox_FindsWhereThePictureActuallySitsInsideAWiderControl()
    {
        var box = VideoOverlayGeometry.Letterbox(1000, 500, 1920, 1080);

        Assert.AreEqual(500, box.Height, Tolerance);
        Assert.AreEqual(1920.0 / 1080 * 500, box.Width, Tolerance);
        Assert.AreEqual(0, box.Y, Tolerance);
        Assert.AreEqual((1000 - box.Width) / 2, box.X, Tolerance);
    }

    /// <summary>
    /// Dragging a rectangle and then reading it back has to give the rectangle that was
    /// dragged. The two conversions are used on opposite sides of every drag, and a
    /// mismatch between them would make a censor creep a little further every time it was
    /// touched.
    /// </summary>
    [TestMethod]
    public void NormalizeAndDenormalize_RoundTripARectangleDraggedOnTheLetterboxedPicture()
    {
        var box = VideoOverlayGeometry.Letterbox(1000, 500, 1920, 1080);
        var drawn = new CaptureRegion(box.X + 40, box.Y + 30, 120, 90);

        var back = VideoOverlayGeometry.Denormalize(VideoOverlayGeometry.Normalize(drawn, box), box);

        Assert.AreEqual(drawn.X, back.X, Tolerance);
        Assert.AreEqual(drawn.Y, back.Y, Tolerance);
        Assert.AreEqual(drawn.Width, back.Width, Tolerance);
        Assert.AreEqual(drawn.Height, back.Height, Tolerance);
    }
}

[TestClass]
public sealed class FrameCensorTests
{
    /// <summary>
    /// A solid censor at full strength must leave nothing of what was underneath. This is
    /// the one effect whose whole purpose is that the original cannot be recovered, so a
    /// blend that left even a trace would be a redaction that does not redact.
    /// </summary>
    [TestMethod]
    public void Apply_LeavesNothingOfTheOriginalUnderASolidCensorAtFullStrength()
    {
        var frame = Filled(20, 20, 200);

        FrameCensor.Apply(frame, 20, 20, new CaptureRegion(4, 4, 8, 8), VideoCensorStyle.Solid, 1);

        Assert.AreEqual(0, At(frame, 20, 8, 8).Blue);
        Assert.AreEqual(byte.MaxValue, At(frame, 20, 8, 8).Alpha);
    }

    /// <summary>
    /// Nothing outside the rectangle may be touched. A censor that bled would blur
    /// content the user never marked, which on a screen recording is the material they
    /// expected to stay readable.
    /// </summary>
    [TestMethod]
    public void Apply_LeavesEveryPixelOutsideTheRectangleExactlyAsItWas()
    {
        var frame = Filled(20, 20, 200);

        FrameCensor.Apply(frame, 20, 20, new CaptureRegion(4, 4, 8, 8), VideoCensorStyle.Solid, 1);

        Assert.AreEqual(200, At(frame, 20, 1, 1).Blue);
        Assert.AreEqual(200, At(frame, 20, 15, 15).Blue);
        Assert.AreEqual(200, At(frame, 20, 3, 8).Blue);
    }

    /// <summary>
    /// Halfway through the ramp the result must be halfway between the original and the
    /// censor. That mix is what makes the effect arrive without a visible pop, and a
    /// strength that snapped from nothing to everything would defeat the ramp the segment
    /// spent a quarter of a second on.
    /// </summary>
    [TestMethod]
    public void Apply_MixesTheCensorWithWhatWasThereWhileTheRampIsRunning()
    {
        var frame = Filled(20, 20, 200);

        FrameCensor.Apply(frame, 20, 20, new CaptureRegion(4, 4, 8, 8), VideoCensorStyle.Solid, 0.5);

        Assert.AreEqual(100, At(frame, 20, 8, 8).Blue);
    }

    /// <summary>
    /// A strength of nothing must cost nothing and change nothing. Every frame outside a
    /// censor's span asks this question, so a version that copied the region first would
    /// pay for the effect on frames it never applies to.
    /// </summary>
    [TestMethod]
    public void Apply_DoesNothingAtAllBeforeTheRampHasStarted()
    {
        var frame = Filled(20, 20, 200);

        FrameCensor.Apply(frame, 20, 20, new CaptureRegion(4, 4, 8, 8), VideoCensorStyle.Blur, 0);

        Assert.AreEqual(200, At(frame, 20, 8, 8).Blue);
    }

    /// <summary>
    /// A rectangle a zoom pushed off the frame must not address pixels that are not
    /// there. The renderer hands this rectangle over unclipped by design, because the
    /// rectangle leaving the frame is meaningful, so the clipping has to happen here.
    /// </summary>
    [TestMethod]
    public void Apply_SurvivesARectangleTheZoomPushedOffTheFrame()
    {
        var frame = Filled(20, 20, 200);

        FrameCensor.Apply(frame, 20, 20, new CaptureRegion(-50, -50, 20, 20), VideoCensorStyle.Solid, 1);
        FrameCensor.Apply(frame, 20, 20, new CaptureRegion(100, 100, 20, 20), VideoCensorStyle.Solid, 1);

        Assert.AreEqual(200, At(frame, 20, 10, 10).Blue);
    }

    private static byte[] Filled(int width, int height, byte value)
    {
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, value);
        return pixels;
    }

    private static (byte Blue, byte Alpha) At(byte[] pixels, int width, int x, int y)
    {
        var offset = ((y * width) + x) * 4;
        return (pixels[offset], pixels[offset + 3]);
    }
}

[TestClass]
public sealed class FrameOverlayTests
{
    /// <summary>
    /// An opaque caption pixel must replace what is under it. The caption is drawn last,
    /// so anything short of replacement would show the picture through the pill and make
    /// white text on a bright screenshot unreadable — the thing the pill exists to
    /// prevent.
    /// </summary>
    [TestMethod]
    public void Composite_ReplacesTheFrameWhereTheCaptionIsOpaque()
    {
        var frame = new byte[16 * 16 * 4];
        Array.Fill(frame, (byte)200);
        var sprite = Sprite(4, 4, blue: 10, alpha: byte.MaxValue);

        FrameOverlay.Composite(frame, 16, 16, sprite, 4, 4, new CaptureRegion(2, 2, 4, 4), 1);

        Assert.AreEqual(10, frame[(((3 * 16) + 3) * 4)]);
        Assert.AreEqual(200, frame[(((0 * 16) + 0) * 4)]);
    }

    /// <summary>
    /// Most of a caption's raster is the clear space round its pill, and those pixels must
    /// leave the frame untouched. A sprite composited without honouring its alpha would
    /// paint a black rectangle across the picture.
    /// </summary>
    [TestMethod]
    public void Composite_LeavesTheFrameAloneWhereTheCaptionIsTransparent()
    {
        var frame = new byte[16 * 16 * 4];
        Array.Fill(frame, (byte)200);
        var sprite = Sprite(4, 4, blue: 0, alpha: 0);

        FrameOverlay.Composite(frame, 16, 16, sprite, 4, 4, new CaptureRegion(2, 2, 4, 4), 1);

        Assert.AreEqual(200, frame[(((3 * 16) + 3) * 4)]);
    }

    /// <summary>
    /// The exported frame must stay opaque wherever a caption was drawn. The encoder is
    /// handed these bytes directly, and a frame carrying the caption's own alpha comes
    /// back with a hole in it rather than with a caption on it.
    /// </summary>
    [TestMethod]
    public void Composite_LeavesTheFrameOpaqueSoTheEncoderDoesNotSeeAHole()
    {
        var frame = new byte[16 * 16 * 4];
        Array.Fill(frame, (byte)200);
        var sprite = Sprite(4, 4, blue: 10, alpha: 128);

        FrameOverlay.Composite(frame, 16, 16, sprite, 4, 4, new CaptureRegion(2, 2, 4, 4), 1);

        Assert.AreEqual(byte.MaxValue, frame[(((3 * 16) + 3) * 4) + 3]);
    }

    /// <summary>
    /// While the caption's ramp is running it must be partly transparent, or the fade the
    /// segment spent a quarter of a second on would not happen at all.
    /// </summary>
    [TestMethod]
    public void Composite_ThinsTheCaptionWhileItsRampIsRunning()
    {
        var frame = new byte[16 * 16 * 4];
        Array.Fill(frame, (byte)200);
        var sprite = Sprite(4, 4, blue: 0, alpha: byte.MaxValue);

        FrameOverlay.Composite(frame, 16, 16, sprite, 4, 4, new CaptureRegion(2, 2, 4, 4), 0.5);

        Assert.AreEqual(100, frame[(((3 * 16) + 3) * 4)]);
    }

    private static byte[] Sprite(int width, int height, byte blue, byte alpha)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < width * height; index++)
        {
            pixels[index * 4] = blue;
            pixels[(index * 4) + 3] = alpha;
        }

        return pixels;
    }
}
