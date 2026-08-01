using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class RecordingPlanTests
{
    [TestMethod]
    public void FrameRateChoices_OffersMacshotSFive()
    {
        CollectionAssert.AreEqual(
            new[] { 15, 24, 30, 60, 120 },
            RecordingPlan.FrameRateChoices(RecordingPlan.DefaultFrameRate).ToArray());
    }

    [TestMethod]
    public void FrameRateChoices_KeepsARateTheFileNamesAndTheMenuDoesNot()
    {
        // The settings file is meant to be hand-editable and takes any rate in range. A
        // menu with nothing to select for 45 would land on its first entry and write 15
        // back, which is a settings window that changes settings by being opened.
        CollectionAssert.AreEqual(
            new[] { 15, 24, 30, 45, 60, 120 },
            RecordingPlan.FrameRateChoices(45).ToArray());
    }

    [TestMethod]
    public void FrameRateChoices_DoesNotOfferARateTheRecorderCannotRun()
    {
        CollectionAssert.AreEqual(
            new[] { 15, 24, 30, 60, 120 },
            RecordingPlan.FrameRateChoices(10_000).ToArray(),
            "clamped to the ceiling, which is already on the menu");

        CollectionAssert.Contains(RecordingPlan.FrameRateChoices(0).ToArray(), RecordingPlan.MinFrameRate);
    }

    [TestMethod]
    public void Resolve_KeepsASizeTheEncoderCanTake()
    {
        // An odd dimension has no whole chroma sample to go in, and H.264 refuses
        // the profile rather than rounding it off itself.
        var plan = RecordingPlan.Resolve(1365, 767);

        Assert.AreEqual(1364, plan.Width);
        Assert.AreEqual(766, plan.Height);
    }

    [TestMethod]
    public void Resolve_StillProducesAProfileForAWindowOnePixelWide()
    {
        var plan = RecordingPlan.Resolve(1, 1);

        Assert.AreEqual(2, plan.Width);
        Assert.AreEqual(2, plan.Height);
    }

    [TestMethod]
    public void Resolve_HoldsTheFrameRateToWhatCanBeEncoded()
    {
        Assert.AreEqual(RecordingPlan.MaxFrameRate, RecordingPlan.Resolve(1920, 1080, 240).FrameRate);
        Assert.AreEqual(RecordingPlan.MinFrameRate, RecordingPlan.Resolve(1920, 1080, 0).FrameRate);
    }

    [TestMethod]
    public void Resolve_SpendsMoreOnMorePixelsAndMoreFrames()
    {
        var small = RecordingPlan.Resolve(1280, 720, 30);
        var large = RecordingPlan.Resolve(1920, 1080, 30);
        var fast = RecordingPlan.Resolve(1280, 720, 60);

        Assert.IsTrue(large.Bitrate > small.Bitrate, "A larger frame needs more bits than a smaller one.");
        Assert.IsTrue(fast.Bitrate > small.Bitrate, "More frames a second needs more bits than fewer.");
    }

    [TestMethod]
    public void Resolve_SpendsWhatMacshotSpendsOnAnOrdinaryRecording()
    {
        // 1080p30 is the common case and the one worth pinning to a number rather
        // than to an inequality. macshot records screen content at 0.40 bits per
        // pixel per frame because H.264 softens high-contrast edges below roughly
        // 0.30 — this was 0.1 here, a quarter of it, and text came out mushy.
        // 1920 * 1080 * 30 * 0.40, with no taper: 1080p is the band boundary, not
        // past it.
        Assert.AreEqual(24_883_200u, RecordingPlan.Resolve(1920, 1080, 30).Bitrate);
    }

    [TestMethod]
    public void Resolve_AsksForFewerBitsPerPixelTheMorePixelsThereAre()
    {
        // Above 1080p and again above 4K the rate is tapered: the extra bits buy
        // least where there are most pixels to hide them in, and an untapered 4K
        // capture asks for a file nobody keeps. Compared per pixel per frame,
        // because in absolute terms a bigger frame always costs more.
        static double PerPixel(int width, int height)
        {
            var plan = RecordingPlan.Resolve(width, height, 30);
            return (double)plan.Bitrate / ((long)plan.Width * plan.Height * plan.FrameRate);
        }

        var fullHd = PerPixel(1920, 1080);
        var between = PerPixel(2560, 1440);
        var ultraHd = PerPixel(3840, 2162);

        Assert.IsTrue(between < fullHd, "Past 1080p the rate should taper.");
        Assert.IsTrue(ultraHd < between, "Past 4K it should taper again.");
    }

    [TestMethod]
    public void Resolve_TakesTheFrameRateMacshotOffersForAnimation()
    {
        // 120 is on macshot's frame-rate menu and the reason the setting exists:
        // recording a UI animation. The ceiling here was 60, so asking for 120 got
        // half of it.
        Assert.AreEqual(120, RecordingPlan.Resolve(1280, 720, 120).FrameRate);
    }

    [TestMethod]
    public void Resolve_StaysInsideWhatIsWorthEncoding()
    {
        // A tiny window would otherwise ask for a bitrate no encoder takes
        // seriously, and a 5K display for one no disk wants.
        Assert.AreEqual(RecordingPlan.MinBitrate, RecordingPlan.Resolve(64, 64).Bitrate);
        Assert.AreEqual(RecordingPlan.MaxBitrate, RecordingPlan.Resolve(5120, 2880, 60).Bitrate);
    }

    [TestMethod]
    public void FrameInterval_IsOneFrameOfTheRateAsked()
    {
        Assert.AreEqual(
            TimeSpan.FromSeconds(1.0 / 30),
            RecordingPlan.Resolve(1920, 1080, 30).FrameInterval);
    }

    [TestMethod]
    public void ShouldKeep_TakesTheFirstFrameOfTheRecording()
    {
        var cadence = new FrameCadence(TimeSpan.FromMilliseconds(100));

        Assert.IsTrue(cadence.ShouldKeep(TimeSpan.Zero));
        Assert.AreEqual(0, cadence.Dropped);
    }

    [TestMethod]
    public void ShouldKeep_TurnsAwayFramesThatArriveFasterThanTheRate()
    {
        var cadence = new FrameCadence(TimeSpan.FromMilliseconds(100));
        cadence.ShouldKeep(TimeSpan.Zero);

        // A 144 Hz display delivers roughly seven frames inside one 100 ms slot.
        // Only the one that lands on the next slot is wanted.
        Assert.IsFalse(cadence.ShouldKeep(TimeSpan.FromMilliseconds(7)));
        Assert.IsFalse(cadence.ShouldKeep(TimeSpan.FromMilliseconds(90)));
        Assert.IsTrue(cadence.ShouldKeep(TimeSpan.FromMilliseconds(100)));
        Assert.AreEqual(2, cadence.Dropped);
    }

    [TestMethod]
    public void ShouldKeep_DoesNotLetABurstThroughAfterAStall()
    {
        var cadence = new FrameCadence(TimeSpan.FromMilliseconds(100));
        cadence.ShouldKeep(TimeSpan.Zero);

        // Nothing changed on screen for a second, so the next frame is late. Were
        // the grid to step only once it would still be a second behind, and every
        // frame of the burst that follows would count as due.
        Assert.IsTrue(cadence.ShouldKeep(TimeSpan.FromMilliseconds(1000)));
        Assert.IsFalse(cadence.ShouldKeep(TimeSpan.FromMilliseconds(1010)));
        Assert.IsFalse(cadence.ShouldKeep(TimeSpan.FromMilliseconds(1050)));
        Assert.IsTrue(cadence.ShouldKeep(TimeSpan.FromMilliseconds(1100)));
    }
}
