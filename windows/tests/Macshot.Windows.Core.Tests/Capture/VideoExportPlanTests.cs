using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

/// <summary>
/// The video editor's arithmetic: what the dimensions menu offers, what the encoder is
/// asked for, and what the reading beside the buttons promises.
/// </summary>
[TestClass]
public sealed class VideoExportPlanTests
{
    [TestMethod]
    public void Scaled_RoundsBothSidesDownToEven()
    {
        // H.264's chroma planes are half resolution: an odd width is either refused or
        // silently padded, and the padding shows as a green line down one edge.
        var (width, height) = VideoExportPlan.Scaled(1365, 767, 33);

        Assert.AreEqual(0, width % 2);
        Assert.AreEqual(0, height % 2);
        Assert.AreEqual(450, width);
        Assert.AreEqual(252, height);
    }

    [TestMethod]
    public void ScaleChoices_LeavesOutThePercentagesThatWouldMakeItUnreadable()
    {
        // A 400-wide recording at 25% is 100 across, which is not a screenshot of
        // anything any more. 33% is 132, which still is.
        CollectionAssert.AreEqual(new[] { 100, 75, 50, 33 }, VideoExportPlan.ScaleChoices(400, 300).ToArray());
    }

    [TestMethod]
    public void ScaleChoices_OffersAllOfThemForARecordingLargeEnough()
    {
        CollectionAssert.AreEqual(
            new[] { 100, 75, 50, 33, 25 },
            VideoExportPlan.ScaleChoices(1920, 1080).ToArray());
    }

    [TestMethod]
    public void ScaleChoices_StillOffersTheOriginalForSomethingTiny()
    {
        // Original is not a choice that can be refused: it is what the file already is.
        CollectionAssert.AreEqual(new[] { 100 }, VideoExportPlan.ScaleChoices(64, 64).ToArray());
        CollectionAssert.AreEqual(new[] { 100 }, VideoExportPlan.ScaleChoices(0, 0).ToArray());
    }

    [TestMethod]
    public void DimensionsLabel_SaysThePercentageOnlyWhenThereIsOne()
    {
        Assert.AreEqual("1920 × 1080", VideoExportPlan.DimensionsLabel(1920, 1080, 100));
        Assert.AreEqual("960 × 540 (50%)", VideoExportPlan.DimensionsLabel(1920, 1080, 50));
    }

    [TestMethod]
    public void Bitrate_AsksForBitsPerPixelPerFrameRatherThanAFixedNumber()
    {
        // 1920 × 1080 × 30 × 0.40 is 24.9 Mbit/s, inside High's range and untapered.
        Assert.AreEqual(24_883_200, VideoExportPlan.Bitrate(1920, 1080, 30, VideoQuality.High));
    }

    [TestMethod]
    public void Bitrate_TapersAboveHdAndAgainAbove4K()
    {
        // Without the taper a 4K capture asks for a bitrate nobody can send anywhere.
        // Medium rather than Low, whose ceiling of 12 Mbit/s would clamp the answer and
        // hide the taper the assertion is about.
        var hd = VideoExportPlan.Bitrate(1920, 1080, 30, VideoQuality.Medium);
        var beyondHd = VideoExportPlan.Bitrate(2560, 1440, 30, VideoQuality.Medium);

        Assert.IsTrue(beyondHd > hd, "a larger frame still asks for more");
        Assert.AreEqual(
            (int)(2560d * 1440 * 30 * 0.22 * 0.92),
            beyondHd,
            "the 0.92 taper applies past 1080p");
    }

    [TestMethod]
    public void Bitrate_StaysInsideTheTierEvenForAbsurdInput()
    {
        Assert.AreEqual(
            VideoExportPlan.MaxBitrate(VideoQuality.High),
            VideoExportPlan.Bitrate(7680, 4320, 120, VideoQuality.High));

        Assert.AreEqual(
            VideoExportPlan.MinBitrate(VideoQuality.High),
            VideoExportPlan.Bitrate(160, 120, 5, VideoQuality.High));
    }

    [TestMethod]
    public void Bitrate_AnswersNonsenseWithTheFloorRatherThanZero()
    {
        // A video track that reported no size would otherwise ask the encoder for a
        // bitrate of nothing, which it accepts and turns into an unwatchable file.
        Assert.AreEqual(VideoQuality.Medium, VideoQuality.Medium);
        Assert.AreEqual(
            VideoExportPlan.MinBitrate(VideoQuality.Medium),
            VideoExportPlan.Bitrate(0, 0, 0, VideoQuality.Medium));
    }

    [TestMethod]
    public void EstimatedBytes_ScalesWithHowMuchIsKeptAndHowLargeItStays()
    {
        // Half the length at half the width is an eighth of the file: pixels scale
        // quadratically, time does not.
        var whole = VideoExportPlan.EstimatedBytes(
            8_000_000, 10, 10, 100, VideoQuality.High, 30, 15, asGif: false);
        var trimmedAndScaled = VideoExportPlan.EstimatedBytes(
            8_000_000, 5, 10, 50, VideoQuality.High, 30, 15, asGif: false);

        Assert.AreEqual(8_000_000, whole);
        Assert.AreEqual(1_000_000, trimmedAndScaled);
    }

    [TestMethod]
    public void EstimatedBytes_TreatsGifAsAWholeDifferentThing()
    {
        // Every GIF frame is a whole image, so nothing about the source's H.264 size
        // predicts it. The number is there to say "much larger", not to be right.
        var gif = VideoExportPlan.EstimatedBytes(
            1_000_000, 10, 10, 100, VideoQuality.High, 30, 15, asGif: true);

        Assert.AreEqual(1_500_000, gif, "three times the source, halved by taking half the frames");
    }

    [TestMethod]
    public void EstimatedBytes_SaysNothingAboutASourceItWasNotGivenTheSizeOf()
    {
        Assert.AreEqual(0, VideoExportPlan.EstimatedBytes(0, 5, 10, 50, VideoQuality.Low, 30, 15, false));
    }

    [TestMethod]
    public void ShowsEstimate_StaysQuietUntilSomethingWouldChangeTheFile()
    {
        Assert.IsFalse(VideoExportPlan.ShowsEstimate(10, 10, 100, VideoQuality.High, asGif: false));
        Assert.IsTrue(VideoExportPlan.ShowsEstimate(9, 10, 100, VideoQuality.High, asGif: false));
        Assert.IsTrue(VideoExportPlan.ShowsEstimate(10, 10, 50, VideoQuality.High, asGif: false));
        Assert.IsTrue(VideoExportPlan.ShowsEstimate(10, 10, 100, VideoQuality.Medium, asGif: false));
        Assert.IsTrue(VideoExportPlan.ShowsEstimate(10, 10, 100, VideoQuality.High, asGif: true));
    }
}

/// <summary>Where the trim handles may go, and how the timeline reads a moment back.</summary>
[TestClass]
public sealed class VideoTrimTests
{
    [TestMethod]
    public void WithStart_CannotBeDraggedPastTheOtherHandle()
    {
        // A trim dragged to nothing exports a file with no frames in it, which every
        // player refuses with an error about the file being corrupt.
        var trim = VideoTrim.Whole(10).WithEnd(4, 10).WithStart(9, 10);

        Assert.AreEqual(4 - VideoTrim.MinimumSeconds, trim.Start, 1e-9);
        Assert.AreEqual(VideoTrim.MinimumSeconds, trim.Duration, 1e-9);
    }

    [TestMethod]
    public void WithEnd_CannotBeDraggedBehindTheOtherHandle()
    {
        var trim = VideoTrim.Whole(10).WithStart(6, 10).WithEnd(1, 10);

        Assert.AreEqual(6 + VideoTrim.MinimumSeconds, trim.End, 1e-9);
    }

    [TestMethod]
    public void Handles_StayInsideTheRecording()
    {
        var trim = VideoTrim.Whole(10).WithStart(-5, 10).WithEnd(99, 10);

        Assert.AreEqual(0, trim.Start, 1e-9);
        Assert.AreEqual(10, trim.End, 1e-9);
    }

    [TestMethod]
    public void IsWhole_AllowsForHandlesThatAreDraggedInPixels()
    {
        // A timeline eight hundred pixels wide cannot express an exact zero on a long
        // recording, and without the slack an untouched export would re-encode.
        Assert.IsTrue(VideoTrim.Whole(60).IsWhole(60));
        Assert.IsTrue(new VideoTrim(0.005, 59.995).IsWhole(60));
        Assert.IsFalse(new VideoTrim(0.5, 60).IsWhole(60));
    }

    [TestMethod]
    public void Format_LeavesTheHoursOutUntilThereAreSome()
    {
        Assert.AreEqual("0:00", VideoTrim.Format(0));
        Assert.AreEqual("0:07", VideoTrim.Format(7.9));
        Assert.AreEqual("1:05", VideoTrim.Format(65));
        Assert.AreEqual("1:00:00", VideoTrim.Format(3600));
        Assert.AreEqual("2:03:04", VideoTrim.Format(7384));
    }

    [TestMethod]
    public void Format_AnswersNonsenseWithZeroRatherThanThrowing()
    {
        // The playhead is asked to draw itself before the duration is known, and a
        // reading of NaN would be the only thing on screen.
        Assert.AreEqual("0:00", VideoTrim.Format(double.NaN));
        Assert.AreEqual("0:00", VideoTrim.Format(-1));
    }
}
