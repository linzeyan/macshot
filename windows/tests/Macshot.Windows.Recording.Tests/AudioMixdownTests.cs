using Macshot.Windows.Core.Capture;
using Windows.Storage;

namespace Macshot.Windows.Recording.Tests;

/// <summary>
/// That balancing a recording's two sources afterwards produces the balance that was asked
/// for, and only it.
/// </summary>
/// <remarks>
/// <para>
/// The merge is a re-encode with the recording's own audio muted and a fresh mix of the two
/// sidecar tracks laid under it. Every way that can go wrong produces a file that plays: a
/// clip that did not mute is both sources heard twice at volumes nobody chose, and a
/// gain applied to the wrong side is a panel whose two sliders are swapped. Neither shows
/// up anywhere except in the samples.
/// </para>
/// <para>
/// So the two sources are given two pitches rather than two loudnesses. Which source is
/// audible, and how loud, is then a question about a known bin — see
/// <see cref="TestAudio.Level"/> — and the recording carries both at once exactly as a real
/// one does.
/// </para>
/// </remarks>
[TestClass]
public sealed class AudioMixdownTests
{
    private const int Seconds = 4;

    /// <summary>440 Hz and two octaves up, both whole divisors of the sample rate.</summary>
    private const double Microphone = 440;

    private const double System = 1760;

    /// <summary>What each source was recorded at, before any slider was moved.</summary>
    private const double Recorded = 0.30;

    /// <summary>
    /// What a level may be out by. AAC moves a pure tone's amplitude by a few percent, and
    /// this is far tighter than any of the differences being asserted — a source heard twice
    /// is out by 100%, and a source turned down to a quarter is out by 75%.
    /// </summary>
    private const double Tolerance = 0.05;

    private StorageFolder _scratch = null!;

    [TestInitialize]
    public async Task CreateScratchAsync() => _scratch = await TestExport.ScratchAsync();

    [TestCleanup]
    public async Task DeleteScratchAsync() => await _scratch.DeleteAsync();

    /// <summary>
    /// The one that fails silently in production. The recording already holds both sources
    /// summed, so the merge has to <em>replace</em> that sum rather than join it — and a
    /// merge laid on top of it still plays, still has both sources in it, and is wrong in a
    /// way only a level meter shows.
    /// </summary>
    [TestMethod]
    public async Task WriteAsync_ReplacesTheRecordingsOwnMixRatherThanPlayingUnderneathIt()
    {
        var sound = await MergeAsync(new AudioMergeAnswer(true, AudioMerge.DefaultVolume, 0));

        Assert.AreEqual(
            Recorded,
            TestAudio.Level(sound, Microphone, 1, 3),
            Tolerance,
            "the microphone is not at the level it was recorded at, so something was added to it");

        Assert.AreEqual(
            0,
            TestAudio.Level(sound, System, 1, 3),
            Tolerance,
            "the system source was turned all the way down and can still be heard: the "
                + "recording's own mix is playing underneath the merge");
    }

    /// <summary>
    /// The panel's whole purpose: two sliders, two sources, each where it was put. Asserted
    /// with the two at different volumes, because equal ones cannot tell a merge that
    /// swapped them from one that did not.
    /// </summary>
    [TestMethod]
    public async Task WriteAsync_PutsEachSourceAtTheVolumeItsOwnSliderWasGiven()
    {
        var sound = await MergeAsync(new AudioMergeAnswer(true, 0.25, AudioMerge.MaximumVolume));

        Assert.AreEqual(
            Recorded * 0.25,
            TestAudio.Level(sound, Microphone, 1, 3),
            Tolerance,
            "the microphone was turned down to a quarter");

        Assert.AreEqual(
            Recorded * AudioMerge.MaximumVolume,
            TestAudio.Level(sound, System, 1, 3),
            Tolerance,
            "the system source was turned up half again, which is what the slider's ceiling means");
    }

    /// <summary>
    /// A merge is about the sound, and the picture has to survive the re-encode it costs.
    /// Windows has no muxer that would leave the video track alone, so every frame goes
    /// through the encoder again for a change that was not about them.
    /// </summary>
    [TestMethod]
    public async Task WriteAsync_LeavesEveryFrameOfThePictureWhereTheRecordingHadIt()
    {
        var merged = await MergedFileAsync(
            new AudioMergeAnswer(true, AudioMerge.DefaultVolume, 0));

        Assert.AreEqual(Seconds, await TestVideo.SecondsAsync(merged), 0.2);

        for (var second = 0; second < Seconds; second++)
        {
            Assert.AreEqual(
                second,
                await TestVideo.SecondShownAtAsync(merged, second + 0.5),
                $"the merged recording shows the wrong frame {second + 0.5:0.0}s in");
        }
    }

    private async Task<byte[]> MergeAsync(AudioMergeAnswer answer) =>
        await TestAudio.SoundAsync(_scratch, await MergedFileAsync(answer));

    /// <summary>
    /// A recording of both sources summed, its two sidecar copies beside it, and the merge
    /// of them all — which is the state <c>AudioSidecar</c> leaves behind when a recording
    /// with both sources stops.
    /// </summary>
    private async Task<StorageFile> MergedFileAsync(AudioMergeAnswer answer)
    {
        var microphone = await TestAudio.WriteToneAsync(_scratch, Seconds, Microphone, Recorded);
        var system = await TestAudio.WriteToneAsync(_scratch, Seconds, System, Recorded);

        var recording = await TestVideo.WriteSecondsAsync(
            _scratch, Seconds, await TestAudio.SummedAsync(_scratch, microphone, system));

        var merged = await _scratch.CreateFileAsync(
            "macshot-merged.mp4", CreationCollisionOption.GenerateUniqueName);

        await AudioMixdown.WriteAsync(recording, merged, microphone.Path, system.Path, answer);

        return merged;
    }
}
