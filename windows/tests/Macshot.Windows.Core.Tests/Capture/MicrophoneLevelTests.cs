using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class MicrophoneLevelTests
{
    [TestMethod]
    public void PeakOf_ReadsSilenceAsNothingAndFullScaleAsAll()
    {
        // The two ends have to be exact, because they are what the user checks the meter
        // against: an empty bar means nothing is being heard, a full one means the input
        // is clipping and the recording will be distorted.
        Assert.AreEqual(0, MicrophoneLevel.PeakOf([0, 0, 0]));
        Assert.AreEqual(1, MicrophoneLevel.PeakOf([0, short.MaxValue, 0]));
    }

    [TestMethod]
    public void PeakOf_ReadsTheLoudestPossibleSampleAsLoudRatherThanAsSilence()
    {
        // The most negative 16-bit sample has no positive counterpart: negating it in 16
        // bits gives it back unchanged. A meter that took magnitudes in shorts would show
        // a full-scale negative peak — which is what a loud voice clipping looks like — as
        // silence, and the user would turn the gain up on a microphone already too hot.
        Assert.AreEqual(1, MicrophoneLevel.PeakOf([short.MinValue]));
    }

    [TestMethod]
    public void Follow_RisesTheInstantSoundArrives()
    {
        // The whole point of the meter is answering "is this microphone hearing me" in the
        // moment somebody says a word into it. A meter that eased upwards would still be
        // climbing when the word had finished.
        var meter = new MicrophoneLevel();

        Assert.IsTrue(meter.Follow(0.9));
        Assert.AreEqual(0.9, meter.Current, 1e-9);
    }

    [TestMethod]
    public void Follow_FallsBackSlowlySoAPauseBetweenWordsIsNotAFlicker()
    {
        // Speech is mostly gaps. Dropping to the new reading on the way down would leave
        // the bar flickering between syllables, which reads as a fault rather than as a
        // level.
        var meter = new MicrophoneLevel();
        meter.Follow(1);

        meter.Follow(0);
        Assert.AreEqual(0.8, meter.Current, 1e-9);

        meter.Follow(0);
        Assert.AreEqual(0.64, meter.Current, 1e-9);
    }

    [TestMethod]
    public void Follow_SettlesAtNothingRatherThanApproachingItForever()
    {
        // A release that only ever multiplies never reaches zero, and a bar a thousandth
        // high over a silent room says the microphone is hearing something.
        var meter = new MicrophoneLevel();
        meter.Follow(1);

        for (var tick = 0; tick < 100; tick++)
        {
            meter.Follow(0);
        }

        Assert.AreEqual(0, meter.Current);
    }

    [TestMethod]
    public void Follow_ReportsNothingToDrawForAChangeTooSmallToSee()
    {
        // The meter is asked twenty times a second for as long as the mic switch is on.
        // Repainting for a change nobody can see is a layout pass through the whole
        // toolbar, over a screenshot, for nothing.
        var meter = new MicrophoneLevel();
        meter.Follow(0.5);

        Assert.IsFalse(meter.Follow(0.5005));
    }

    [TestMethod]
    public void Follow_ClampsASourceLouderThanFullScale()
    {
        // Nothing should ever hand this more than full scale, and a bar taller than the
        // button it is drawn in would be the first anyone heard of it.
        var meter = new MicrophoneLevel();

        meter.Follow(4);

        Assert.AreEqual(1, meter.Current);
    }

    [TestMethod]
    public void Silence_ClearsTheMeterAndSaysWhetherAnythingWasShowing()
    {
        // Switching the microphone off has to take the bar with it: a meter left standing
        // at the last thing it heard says the microphone is still open.
        var meter = new MicrophoneLevel();
        meter.Follow(0.7);

        Assert.IsTrue(meter.Silence());
        Assert.AreEqual(0, meter.Current);
        Assert.IsFalse(meter.Silence());
    }
}
