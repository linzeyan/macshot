using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class RetakeCadenceTests
{
    private static readonly TimeSpan PastTheGrace = RetakeCadence.StarvedAfter + TimeSpan.FromSeconds(1);

    [TestMethod]
    public void ShouldTake_SaysYesOnceTheGraceHasPassedWithNothingDelivered()
    {
        // The first version read `elapsed - TimeSpan.MinValue`, which overflows rather than
        // answering — every recording threw a second after it started, and the counter this
        // was diagnosed by stayed at zero, so it read as "the fallback never engages".
        Assert.IsTrue(new RetakeCadence().ShouldTake(kept: 0, PastTheGrace, paused: false));
    }

    [TestMethod]
    public void ShouldTake_WaitsOutTheGraceRatherThanRacingASessionThatIsAboutToWork()
    {
        var cadence = new RetakeCadence();

        Assert.IsFalse(cadence.ShouldTake(kept: 0, TimeSpan.Zero, paused: false));
        Assert.IsFalse(
            cadence.ShouldTake(kept: 0, RetakeCadence.StarvedAfter - TimeSpan.FromMilliseconds(1), paused: false));
    }

    [TestMethod]
    public void ShouldTake_StopsForGoodOnceTheCompositorHasDeliveredAnything()
    {
        // The point of the guard: this is a fallback for a session that never works, not a
        // second capture path for a screen that has merely stopped moving. Repeating the
        // last frame is the right answer to stillness and costs nothing.
        Assert.IsFalse(new RetakeCadence().ShouldTake(kept: 1, PastTheGrace, paused: false));
    }

    [TestMethod]
    public void ShouldTake_LeavesAPauseEmpty()
    {
        // A pause is meant to leave an absence in the file. Filling it with frames taken by
        // hand would put the held seconds back into the recording.
        Assert.IsFalse(new RetakeCadence().ShouldTake(kept: 0, PastTheGrace, paused: true));
    }

    [TestMethod]
    public void ShouldTake_HoldsToItsIntervalSoOneCaptureDoesNotStarveTheEncoder()
    {
        var cadence = new RetakeCadence();

        Assert.IsTrue(cadence.ShouldTake(kept: 0, PastTheGrace, paused: false));
        cadence.Record(took: true);

        Assert.IsFalse(
            cadence.ShouldTake(kept: 0, PastTheGrace + RetakeCadence.Interval - TimeSpan.FromMilliseconds(1), false),
            "each of these costs a whole capture session inside the encoder's sample request");

        Assert.IsTrue(cadence.ShouldTake(kept: 0, PastTheGrace + RetakeCadence.Interval, paused: false));
    }

    [TestMethod]
    public void ShouldTake_GivesUpWhereTakingOneByHandDoesNotWorkEither()
    {
        // Every failed attempt pays the capture timeout, so a machine where this cannot work
        // is left with the still image it was going to have rather than a stalled encoder.
        var cadence = new RetakeCadence();
        var at = PastTheGrace;

        for (var attempt = 0; attempt < RetakeCadence.Attempts; attempt++)
        {
            Assert.IsTrue(cadence.ShouldTake(kept: 0, at, paused: false));
            cadence.Record(took: false);
            at += RetakeCadence.Interval;
        }

        Assert.IsFalse(cadence.ShouldTake(kept: 0, at, paused: false));
        Assert.AreEqual(0, cadence.Taken);
    }

    [TestMethod]
    public void Record_TreatsFailuresAsConsecutiveRatherThanCumulative()
    {
        // A recording is minutes long and a capture can fail for reasons that pass — a
        // display mode change, a moment of contention. Counting those towards a permanent
        // retirement would retire it on a machine where it works.
        var cadence = new RetakeCadence();
        var at = PastTheGrace;

        for (var round = 0; round < RetakeCadence.Attempts * 3; round++)
        {
            Assert.IsTrue(cadence.ShouldTake(kept: 0, at, paused: false));
            cadence.Record(took: round % 2 == 0);
            at += RetakeCadence.Interval;
        }
    }
}
