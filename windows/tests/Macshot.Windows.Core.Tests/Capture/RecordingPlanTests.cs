using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class RecordingPlanTests
{
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
