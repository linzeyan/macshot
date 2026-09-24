using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class RetakeCadenceTests
{
    private static readonly TimeSpan PastTheGrace = RetakeCadence.StarvedAfter + TimeSpan.FromSeconds(1);

    /// <summary>
    /// A 10fps recording, so the cadence runs at the file's own rate and these read as they
    /// did when the interval was a constant 100ms.
    /// </summary>
    private static readonly TimeSpan SlowEnoughToBeItsOwnRate = TimeSpan.FromMilliseconds(100);

    private static RetakeCadence Cadence() => new(SlowEnoughToBeItsOwnRate);

    [TestMethod]
    public void ShouldTake_SaysYesOnceTheGraceHasPassedWithNothingDelivered()
    {
        // The first version read `elapsed - TimeSpan.MinValue`, which overflows rather than
        // answering — every recording threw a second after it started, and the counter this
        // was diagnosed by stayed at zero, so it read as "the fallback never engages".
        Assert.IsTrue(Cadence().ShouldTake(kept: 0, PastTheGrace, paused: false));
    }

    [TestMethod]
    public void StarvedAfter_ClearsAWorkingSessionsFirstFrameWithoutFreezingTheHeadOfTheFile()
    {
        // Both bounds are the point of the number, and nothing else here can fail if it
        // moves — every other test asks about the grace in terms of itself.
        //
        // Too short and a healthy recording starts copying the screen before its own
        // session has said anything: measured across three VM recordings, the compositor's
        // first frame arrived at 55, 65 and 73ms. Too long and the machine this exists for
        // opens every recording with a visibly frozen still, which at a whole second is
        // what was reported.
        Assert.IsTrue(
            RetakeCadence.StarvedAfter >= TimeSpan.FromMilliseconds(220),
            "a loaded machine needs room past the ~73ms a working session was measured at");
        Assert.IsTrue(
            RetakeCadence.StarvedAfter <= TimeSpan.FromMilliseconds(400),
            "past this the wait reads as a stall rather than as the recording starting");
    }

    [TestMethod]
    public void Interval_TakesTheRecordingsOwnRateWhereTheFileCannotHoldFramesAnyFaster()
    {
        // A 10fps recording has nowhere to put a frame more often than every 100ms, so a
        // screen copied faster than that is a screen copied for nothing.
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), new RetakeCadence(TimeSpan.FromMilliseconds(100)).Interval);
    }

    [TestMethod]
    public void Interval_StopsAtWhatAScreenCopyCostsRatherThanChasingTheFrameRate()
    {
        // 120fps asks for a frame every 8ms and a screen copy was measured at 41ms for the
        // BitBlt alone, so chasing the rate would saturate a thread and allocate a screen
        // per copy to no visible end. This is the guard on that, and it is why the interval
        // is the recording's rate rather than a constant: before, a 120fps recording and a
        // 10fps one both got ten frames a second.
        Assert.AreEqual(
            RetakeCadence.FastestUseful,
            new RetakeCadence(TimeSpan.FromMilliseconds(8)).Interval);
    }

    [TestMethod]
    public void ShouldTake_WaitsOutTheGraceRatherThanRacingASessionThatIsAboutToWork()
    {
        var cadence = Cadence();

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
        Assert.IsFalse(Cadence().ShouldTake(kept: 1, PastTheGrace, paused: false));
    }

    [TestMethod]
    public void ShouldTake_LeavesAPauseEmpty()
    {
        // A pause is meant to leave an absence in the file. Filling it with frames taken by
        // hand would put the held seconds back into the recording.
        Assert.IsFalse(Cadence().ShouldTake(kept: 0, PastTheGrace, paused: true));
    }

    [TestMethod]
    public void ShouldTake_HoldsToItsIntervalSoOneCaptureDoesNotStarveTheEncoder()
    {
        var cadence = Cadence();

        Assert.IsTrue(cadence.ShouldTake(kept: 0, PastTheGrace, paused: false));
        cadence.Record(took: true);

        Assert.IsFalse(
            cadence.ShouldTake(kept: 0, PastTheGrace + SlowEnoughToBeItsOwnRate - TimeSpan.FromMilliseconds(1), false),
            "each of these copies the whole screen, at a measured 45ms and a screen's worth of memory");

        Assert.IsTrue(cadence.ShouldTake(kept: 0, PastTheGrace + SlowEnoughToBeItsOwnRate, paused: false));
    }

    [TestMethod]
    public void ShouldTake_GivesUpWhereTakingOneByHandDoesNotWorkEither()
    {
        // Every failed attempt pays the capture timeout, so a machine where this cannot work
        // is left with the still image it was going to have rather than a stalled encoder.
        var cadence = Cadence();
        var at = PastTheGrace;

        for (var attempt = 0; attempt < RetakeCadence.Attempts; attempt++)
        {
            Assert.IsTrue(cadence.ShouldTake(kept: 0, at, paused: false));
            cadence.Record(took: false);
            at += SlowEnoughToBeItsOwnRate;
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
        var cadence = Cadence();
        var at = PastTheGrace;

        for (var round = 0; round < RetakeCadence.Attempts * 3; round++)
        {
            Assert.IsTrue(cadence.ShouldTake(kept: 0, at, paused: false));
            cadence.Record(took: round % 2 == 0);
            at += SlowEnoughToBeItsOwnRate;
        }
    }

    [TestMethod]
    public void ShouldTake_TakesTheFirstFrameAtOnceWhenNoSessionWasOpened()
    {
        // The grace is room for a session about to deliver. A display recorded without one —
        // on a Windows that would otherwise draw its yellow border round the whole screen —
        // has nothing coming, and waiting it out would freeze the head of every recording.
        var cadence = new RetakeCadence(SlowEnoughToBeItsOwnRate, isOnlySource: true);

        Assert.IsTrue(cadence.ShouldTake(kept: 0, TimeSpan.Zero, paused: false));
    }

    [TestMethod]
    public void ShouldTake_KeepsCopyingThroughFailuresWhenTheCopiesAreTheWholeRecording()
    {
        // A UAC prompt or a display mode change makes the screen unreadable for a moment.
        // Retiring after three misses is right for a fallback, which leaves the compositor's
        // frames behind it; here it would leave the rest of the recording one still frame.
        var cadence = new RetakeCadence(SlowEnoughToBeItsOwnRate, isOnlySource: true);
        var at = TimeSpan.Zero;

        for (var attempt = 0; attempt < RetakeCadence.Attempts * 2; attempt++)
        {
            Assert.IsTrue(cadence.ShouldTake(kept: 0, at, paused: false));
            cadence.Record(took: false);
            at += SlowEnoughToBeItsOwnRate;
        }

        Assert.IsTrue(cadence.ShouldTake(kept: 0, at, paused: false), "the screen is readable again");
    }

    [TestMethod]
    public void ShouldTake_StillLeavesAPauseEmptyWhenTheCopiesAreTheWholeRecording()
    {
        // Dropping the grace and the retirement must not drop the pause with them: the held
        // seconds would come back into the file as frames.
        var cadence = new RetakeCadence(SlowEnoughToBeItsOwnRate, isOnlySource: true);

        Assert.IsFalse(cadence.ShouldTake(kept: 0, PastTheGrace, paused: true));
    }

    [TestMethod]
    public void ShouldTake_StillHoldsToItsIntervalWhenTheCopiesAreTheWholeRecording()
    {
        // Every frame of such a recording is a screen copy, so this is what stops one from
        // being started on every sample request the encoder makes.
        var cadence = new RetakeCadence(SlowEnoughToBeItsOwnRate, isOnlySource: true);

        Assert.IsTrue(cadence.ShouldTake(kept: 0, TimeSpan.Zero, paused: false));
        cadence.Record(took: true);

        Assert.IsFalse(cadence.ShouldTake(kept: 0, SlowEnoughToBeItsOwnRate - TimeSpan.FromMilliseconds(1), false));
        Assert.IsTrue(cadence.ShouldTake(kept: 0, SlowEnoughToBeItsOwnRate, paused: false));
    }
}
