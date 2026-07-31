using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class GifRecordingPlanTests
{
    [TestMethod]
    public void Resolve_BringsADisplayDownToSomethingWorthSending()
    {
        var plan = GifRecordingPlan.Resolve(3840, 2160);

        Assert.AreEqual(GifRecordingPlan.MaximumEdge, plan.Width);
        Assert.AreEqual(540, plan.Height);
    }

    [TestMethod]
    public void Resolve_ShrinksByTheLongEdgeWhicheverOneThatIs()
    {
        // A window taller than it is wide is scaled by its height, or the ceiling
        // would do nothing to the dimension that is actually large.
        var plan = GifRecordingPlan.Resolve(600, 1920);

        Assert.AreEqual(GifRecordingPlan.MaximumEdge, plan.Height);
        Assert.AreEqual(300, plan.Width);
    }

    [TestMethod]
    public void Resolve_LeavesASmallSourceAlone()
    {
        var plan = GifRecordingPlan.Resolve(400, 300);

        Assert.AreEqual(400, plan.Width);
        Assert.AreEqual(300, plan.Height);
    }

    [TestMethod]
    public void Resolve_HoldsTheFrameRateToWhatAGifShouldBe()
    {
        Assert.AreEqual(GifRecordingPlan.MaxFrameRate, GifRecordingPlan.Resolve(800, 600, 60).FrameRate);
        Assert.AreEqual(GifRecordingPlan.MinFrameRate, GifRecordingPlan.Resolve(800, 600, 0).FrameRate);
    }

    [TestMethod]
    public void Next_RoundsToTheHundredthsAGifCanStore()
    {
        var timing = new GifFrameTiming();

        Assert.AreEqual(10, timing.Next(TimeSpan.FromMilliseconds(100)));
        Assert.AreEqual(25, timing.Next(TimeSpan.FromMilliseconds(250)));
    }

    [TestMethod]
    public void Next_CarriesTheRemainderSoTheGifDoesNotRunFast()
    {
        var timing = new GifFrameTiming();
        var gap = TimeSpan.FromSeconds(1.0 / 12);

        // Twelve frames of 8.33 hundredths each. Rounded independently they would
        // total 96, and the second would play four percent short; carried, they have
        // to come to a second.
        var total = 0;
        for (var frame = 0; frame < 12; frame++)
        {
            total += timing.Next(gap);
        }

        Assert.AreEqual(100, total);
    }

    [TestMethod]
    public void Next_WillNotWriteADelayViewersReadAsATenth()
    {
        var timing = new GifFrameTiming();

        // A frame 4 ms after the last one rounds to nothing, and a GIF delay of 0 or
        // 1 is treated as 10 by every viewer that inherited the browsers' rule — the
        // shortest honest delay is the floor.
        Assert.AreEqual(GifFrameTiming.MinimumDelay, timing.Next(TimeSpan.FromMilliseconds(4)));
    }

    [TestMethod]
    public void Resolve_AcceptsMacshotsOwnCeilingOfThirty()
    {
        // GIFEncoder.swift:29 caps whatever it is asked for at 30. The ceiling here was
        // 24, which is a caution rather than a limit — and one that would have made the
        // frame-rate setting unable to ask for what macshot allows.
        Assert.AreEqual(30, GifRecordingPlan.Resolve(320, 240, 30).FrameRate);
    }
}
