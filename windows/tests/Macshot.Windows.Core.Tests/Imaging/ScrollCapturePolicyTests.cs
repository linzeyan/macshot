using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class ScrollCapturePolicyTests
{
    private const int Tall = 100_000;

    [TestMethod]
    public void Observe_KeepsGoingWhileTheViewAdvances()
    {
        var policy = new ScrollCapturePolicy(Tall);

        Assert.AreEqual(ScrollCaptureStop.None, policy.Observe(ScrollStitchOutcome.Seeded, 800));
        for (var frame = 0; frame < 20; frame++)
        {
            Assert.AreEqual(ScrollCaptureStop.None, policy.Observe(ScrollStitchOutcome.Advanced, 800 + frame));
        }
    }

    [TestMethod]
    public void Observe_CallsItCompleteOnceTheViewStopsMoving()
    {
        var policy = new ScrollCapturePolicy(Tall);
        policy.Observe(ScrollStitchOutcome.Seeded, 800);

        // The bottom of a page is not one repeated frame — a scroll still settling
        // produces those — it is repeats that keep coming.
        for (var frame = 1; frame < ScrollCapturePolicy.UnchangedFramesBeforeComplete; frame++)
        {
            Assert.AreEqual(ScrollCaptureStop.None, policy.Observe(ScrollStitchOutcome.Unchanged, 800));
        }

        Assert.AreEqual(ScrollCaptureStop.Complete, policy.Observe(ScrollStitchOutcome.Unchanged, 800));
    }

    [TestMethod]
    public void Observe_ForgivesARepeatThatTheViewScrollsOutOf()
    {
        var policy = new ScrollCapturePolicy(Tall);
        policy.Observe(ScrollStitchOutcome.Seeded, 800);

        for (var frame = 1; frame < ScrollCapturePolicy.UnchangedFramesBeforeComplete; frame++)
        {
            policy.Observe(ScrollStitchOutcome.Unchanged, 800);
        }

        // A settling scroll that then moves again must not leave the capture one
        // repeat away from calling the page finished for the rest of the run.
        Assert.AreEqual(ScrollCaptureStop.None, policy.Observe(ScrollStitchOutcome.Advanced, 900));
        Assert.AreEqual(ScrollCaptureStop.None, policy.Observe(ScrollStitchOutcome.Unchanged, 900));
    }

    [TestMethod]
    public void Observe_GivesUpOnceFramesStopMatchingAltogether()
    {
        var policy = new ScrollCapturePolicy(Tall);
        policy.Observe(ScrollStitchOutcome.Seeded, 800);

        for (var frame = 1; frame < ScrollCapturePolicy.RejectedFramesBeforeLostTrack; frame++)
        {
            Assert.AreEqual(ScrollCaptureStop.None, policy.Observe(ScrollStitchOutcome.Rejected, 800));
        }

        Assert.AreEqual(ScrollCaptureStop.LostTrack, policy.Observe(ScrollStitchOutcome.Rejected, 800));
    }

    [TestMethod]
    public void Observe_ForgivesASingleUnmatchedFrame()
    {
        var policy = new ScrollCapturePolicy(Tall);
        policy.Observe(ScrollStitchOutcome.Seeded, 800);

        // One frame caught mid-repaint is ordinary. Ending the capture on it would
        // truncate the page for a reason the user cannot see.
        for (var round = 0; round < 10; round++)
        {
            Assert.AreEqual(ScrollCaptureStop.None, policy.Observe(ScrollStitchOutcome.Rejected, 800));
            Assert.AreEqual(ScrollCaptureStop.None, policy.Observe(ScrollStitchOutcome.Advanced, 900 + round));
        }
    }

    [TestMethod]
    public void Observe_StopsAtTheHeightCeiling()
    {
        var policy = new ScrollCapturePolicy(1000);

        Assert.AreEqual(ScrollCaptureStop.None, policy.Observe(ScrollStitchOutcome.Seeded, 999));

        // A feed that loads as it is scrolled has no bottom to reach, so the ceiling
        // is the only thing that ends the run before the buffer exhausts the machine.
        Assert.AreEqual(ScrollCaptureStop.HeightLimit, policy.Observe(ScrollStitchOutcome.Advanced, 1000));
    }

    [TestMethod]
    public void Observe_ReportsTheCeilingRatherThanTheStallWhenBothLandTogether()
    {
        var policy = new ScrollCapturePolicy(1000);
        policy.Observe(ScrollStitchOutcome.Seeded, 999);

        for (var frame = 1; frame < ScrollCapturePolicy.UnchangedFramesBeforeComplete; frame++)
        {
            policy.Observe(ScrollStitchOutcome.Unchanged, 999);
        }

        // Complete means the page ran out; HeightLimit means macshot stopped early.
        // Reporting the first when the second happened would tell the user their
        // capture is whole when rows are missing from the bottom of it.
        Assert.AreEqual(ScrollCaptureStop.HeightLimit, policy.Observe(ScrollStitchOutcome.Unchanged, 1000));
    }

    [TestMethod]
    public void Constructor_RefusesACeilingNothingCouldFitUnder()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new ScrollCapturePolicy(0));
    }

    [TestMethod]
    public void Observe_UndrivenNeverCallsAStillViewFinished()
    {
        // Nobody is turning the wheel, so a view that has not moved is someone deciding
        // where to scroll next. Ending there would end the capture before the user has
        // scrolled at all, which is every manual capture.
        var policy = new ScrollCapturePolicy(10_000, driven: false);

        for (var frame = 0; frame < ScrollCapturePolicy.UnchangedFramesBeforeComplete * 3; frame++)
        {
            Assert.AreEqual(
                ScrollCaptureStop.None,
                policy.Observe(ScrollStitchOutcome.Unchanged, 500));
        }
    }

    [TestMethod]
    public void Observe_UndrivenKeepsGoingThroughFramesItCannotMatch()
    {
        // A run of rejections is someone who flicked a whole page rather than a view
        // that has been lost. They can scroll back; giving up cannot be undone.
        var policy = new ScrollCapturePolicy(10_000, driven: false);

        for (var frame = 0; frame < ScrollCapturePolicy.RejectedFramesBeforeLostTrack * 3; frame++)
        {
            Assert.AreEqual(
                ScrollCaptureStop.None,
                policy.Observe(ScrollStitchOutcome.Rejected, 500));
        }
    }

    [TestMethod]
    public void Observe_UndrivenStillStopsAtTheCeiling()
    {
        // The one thing that is not about reading the frames: the picture is held whole
        // while it grows, and a feed that never ends has to meet something.
        var policy = new ScrollCapturePolicy(1_000, driven: false);

        Assert.AreEqual(
            ScrollCaptureStop.HeightLimit,
            policy.Observe(ScrollStitchOutcome.Advanced, 1_000));
    }
}
