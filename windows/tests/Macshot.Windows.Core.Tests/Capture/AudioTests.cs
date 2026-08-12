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
public sealed class AudioMergeTests
{
    [TestMethod]
    public void Blend_AtUnityIsTheMixTheRecordingAlreadyHolds()
    {
        // The panel's two answers have to agree with each other: a recording carries both
        // sources summed at unity, so merging without moving a slider must produce the
        // same sound. If this drifts, Merge Audio quietly changes recordings it was told
        // to leave alone.
        var into = new byte[4];

        AudioMerge.Blend(Bytes(100, 30000), Bytes(200, 30000), into, 1.0, 1.0);

        CollectionAssert.AreEqual(Bytes(300, short.MaxValue), into);
    }

    [TestMethod]
    public void Blend_ClipsTheSumRatherThanEachSource()
    {
        // Scaling a source into place and clamping it there before the other is added
        // would flatten a microphone turned up to 1.5 against the ceiling, so the loud
        // passages of a merge would come out quieter than the same passages of the mix
        // already in the file. 30000 x 1.5 is 45000 - past a sample - and the system
        // audio underneath it brings the sum back inside.
        var into = new byte[2];

        AudioMerge.Blend(Bytes(30000), Bytes(-20000), into, 1.5, 1.0);

        CollectionAssert.AreEqual(Bytes(25000), into);
    }

    [TestMethod]
    public void Blend_TreatsAMissingTailAsSilenceRatherThanEndingTheMerge()
    {
        // The two sources are written by the same loop, a sample at a time, so they can
        // only differ by the last one. Stopping at the shorter would lose the end of a
        // recording to a rounding at its very end.
        var into = new byte[4];

        AudioMerge.Blend(Bytes(100, 400), Bytes(200), into, 1.0, 1.0);

        CollectionAssert.AreEqual(Bytes(300, 400), into);
    }

    [TestMethod]
    public void Rewrites_IsFalseAtUnityBecauseAMergeReEncodesTheWholeRecording()
    {
        // Windows has no muxer that puts a new audio track beside an encoded video one, so
        // honouring the answer costs a full re-encode. Pressing Merge Audio with both
        // sliders where they started must not spend minutes producing the file that is
        // already on disk.
        Assert.IsFalse(AudioMerge.Rewrites(1.0, 1.0));
        Assert.IsFalse(AudioMerge.Rewrites(1.001, 0.999), "below what anyone can hear");

        Assert.IsTrue(AudioMerge.Rewrites(1.0, 0.5));
        Assert.IsTrue(AudioMerge.Rewrites(1.5, 1.0));
    }

    [TestMethod]
    public void Clamp_HoldsAVolumeToWhatTheSlidersCanSay()
    {
        Assert.AreEqual(AudioMerge.MaximumVolume, AudioMerge.Clamp(4));
        Assert.AreEqual(AudioMerge.MinimumVolume, AudioMerge.Clamp(-1));

        // A slider that never reported a value leaves the recording as it was, rather than
        // silencing a source because an unset value multiplied to nothing.
        Assert.AreEqual(AudioMerge.DefaultVolume, AudioMerge.Clamp(double.NaN));
    }

    [TestMethod]
    public void IsOffered_OnlyWhenBothSourcesWereRecorded()
    {
        // macshot asks only when the file it made holds two tracks. With one source there
        // is nothing to balance against, and being asked anyway would be a question with
        // no wrong answer standing between the user and their recording.
        Assert.IsTrue(AudioMerge.IsOffered(systemAudio: true, microphone: true));
        Assert.IsFalse(AudioMerge.IsOffered(systemAudio: true, microphone: false));
        Assert.IsFalse(AudioMerge.IsOffered(systemAudio: false, microphone: true));
        Assert.IsFalse(AudioMerge.IsOffered(systemAudio: false, microphone: false));
    }

    [TestMethod]
    public void Order_ListsTheMicrophoneFirstAsMacshotsPanelDoes()
    {
        CollectionAssert.AreEqual(
            new[] { AudioTrackKind.Microphone, AudioTrackKind.System },
            AudioMerge.Order.ToArray());
    }

    [TestMethod]
    public void Label_IsMacshotsOwnWordingBecauseThatIsTheTranslationKey()
    {
        // Keys are the English strings themselves and macshot's forty translations are
        // vendored under Strings/upstream. A word changed here is a row that comes out in
        // English in every one of them.
        Assert.AreEqual("Microphone:", AudioMerge.Label(AudioTrackKind.Microphone));
        Assert.AreEqual("System audio:", AudioMerge.Label(AudioTrackKind.System));
    }

    [TestMethod]
    public void KeepSeparate_LeavesBothVolumesWhereTheyStarted()
    {
        // A panel dismissed by its close button must deliver the recording untouched, not
        // silently merge it at whatever the sliders happened to be left at.
        Assert.IsFalse(AudioMergeAnswer.KeepSeparate.Merge);
        Assert.IsFalse(AudioMerge.Rewrites(
            AudioMergeAnswer.KeepSeparate.MicrophoneVolume,
            AudioMergeAnswer.KeepSeparate.SystemVolume));
    }

    /// <summary>The samples as a track holds them: little-endian pairs.</summary>
    private static byte[] Bytes(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        AudioMixing.WriteBytes(samples, bytes);
        return bytes;
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
