using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class AudioPlanTests
{
    [TestMethod]
    public void OneSample_IsTwentyMillisecondsOfStereoAtFortyEightKilohertz()
    {
        Assert.AreEqual(960, AudioPlan.FramesPerSample, "48000 / 50");
        Assert.AreEqual(960 * 2 * 2, AudioPlan.BytesPerSample, "two channels of two bytes each");
    }

    [TestMethod]
    public void Timestamps_ComeFromTheSampleCountRatherThanAClock()
    {
        // A timestamp read off a clock drifts against the samples actually written, and
        // the drift is heard as the sound sliding out of step with the picture.
        Assert.AreEqual(TimeSpan.Zero, AudioPlan.TimestampOf(0));
        Assert.AreEqual(TimeSpan.FromSeconds(1), AudioPlan.TimestampOf(50));

        // Exactly, after an hour: 20 ms is a whole number of ticks, so this cannot
        // accumulate a rounding error however long the recording runs.
        Assert.AreEqual(TimeSpan.FromHours(1), AudioPlan.TimestampOf(50 * 60 * 60));
    }
}

[TestClass]
public sealed class AudioMixingTests
{
    [TestMethod]
    public void MixInto_AddsTheSourcesTogether()
    {
        short[] track = [100, -100, 0];

        AudioMixing.MixInto(track, [10, 10, 10]);

        CollectionAssert.AreEqual(new short[] { 110, -90, 10 }, track);
    }

    [TestMethod]
    public void MixInto_ClipsRatherThanWrapping()
    {
        // Two loud sources sum past what a 16-bit sample holds. Wrapping turns that into
        // a burst of noise at full scale, which is far worse than a flattened peak.
        short[] track = [30000, -30000];

        AudioMixing.MixInto(track, [30000, -30000]);

        CollectionAssert.AreEqual(new short[] { short.MaxValue, short.MinValue }, track);
    }

    [TestMethod]
    public void MixInto_StopsAtTheShorterOfTheTwo()
    {
        short[] track = [1, 2, 3];

        AudioMixing.MixInto(track, [10]);

        CollectionAssert.AreEqual(new short[] { 11, 2, 3 }, track);
    }

    [TestMethod]
    public void SpreadInto_PutsAMonoSourceInBothEars()
    {
        // A voice arriving in one ear is heard as a fault in the recording.
        var stereo = new short[6];

        AudioMixing.SpreadInto(stereo, [7, 8, 9]);

        CollectionAssert.AreEqual(new short[] { 7, 7, 8, 8, 9, 9 }, stereo);
    }

    [TestMethod]
    public void WriteBytes_WritesLittleEndianPairs()
    {
        var bytes = new byte[4];

        AudioMixing.WriteBytes([0x0102, -1], bytes);

        CollectionAssert.AreEqual(new byte[] { 0x02, 0x01, 0xFF, 0xFF }, bytes);
    }
}

[TestClass]
public sealed class AudioSampleBufferTests
{
    [TestMethod]
    public void Take_FillsWithSilenceWhenTheSourceHadNothing()
    {
        // The case the whole class exists for: a Windows loopback endpoint delivers
        // nothing at all while the machine is quiet. A track built only from what
        // arrived would be short by every silent passage, and the sound would slide
        // earlier and earlier against the picture.
        var buffer = new AudioSampleBuffer();
        var sample = new short[4];
        Array.Fill(sample, (short)999);

        var real = buffer.Take(sample);

        Assert.AreEqual(0, real);
        CollectionAssert.AreEqual(new short[4], sample);
    }

    [TestMethod]
    public void Take_FillsTheRestWithSilenceWhenTheSourceCameUpShort()
    {
        var buffer = new AudioSampleBuffer();
        buffer.Append([5, 6]);

        var sample = new short[4];
        var real = buffer.Take(sample);

        Assert.AreEqual(2, real);
        CollectionAssert.AreEqual(new short[] { 5, 6, 0, 0 }, sample);
    }

    [TestMethod]
    public void Take_CarriesOnAcrossWhateverSizesTheSourceProducedIn()
    {
        // An endpoint hands over whatever a packet happens to hold, which has nothing to
        // do with the twenty milliseconds a sample carries.
        var buffer = new AudioSampleBuffer();
        buffer.Append([1]);
        buffer.Append([2, 3, 4, 5, 6]);

        var first = new short[4];
        var second = new short[4];
        buffer.Take(first);
        var real = buffer.Take(second);

        CollectionAssert.AreEqual(new short[] { 1, 2, 3, 4 }, first);
        CollectionAssert.AreEqual(new short[] { 5, 6, 0, 0 }, second);
        Assert.AreEqual(2, real);
    }

    [TestMethod]
    public void Append_DropsTheOldestRatherThanGrowingWithoutEnd()
    {
        // A source producing faster than the track is drained is a stalled encoder or a
        // loaded machine. Keeping it all trades memory for a backlog that plays back as
        // sound running behind everything it was recorded with.
        var buffer = new AudioSampleBuffer(capacity: 4);
        buffer.Append([1, 2, 3, 4]);
        buffer.Append([5, 6, 7, 8]);

        var sample = new short[4];
        buffer.Take(sample);

        CollectionAssert.AreEqual(new short[] { 5, 6, 7, 8 }, sample);
        Assert.AreEqual(4, buffer.Dropped);
    }

    [TestMethod]
    public void Pending_SaysHowMuchIsWaiting()
    {
        var buffer = new AudioSampleBuffer();
        buffer.Append([1, 2, 3]);
        Assert.AreEqual(3, buffer.Pending);

        buffer.Take(new short[2]);
        Assert.AreEqual(1, buffer.Pending);
    }
}
