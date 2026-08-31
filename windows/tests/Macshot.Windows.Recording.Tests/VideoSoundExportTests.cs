using Macshot.Windows.Core.Capture;
using Windows.Storage;

namespace Macshot.Windows.Recording.Tests;

/// <summary>
/// That the sound comes out of the export where the picture does.
/// </summary>
/// <remarks>
/// <para>
/// The export moves the two with different machinery: the frames are decoded and
/// rewritten one at a time, while the track is either trimmed into runs and laid back
/// under them or resampled to PCM and re-encoded. Nothing forces those two to agree, and
/// a drift between them is the failure that a still frame cannot show and a preview
/// cannot either — the editor plays the source.
/// </para>
/// <para>
/// So every test here asks the same question twice at the same moment: which second is on
/// screen, and which second is in the speaker.
/// </para>
/// </remarks>
[TestClass]
public sealed class VideoSoundExportTests
{
    private const int SourceSeconds = 6;

    private StorageFolder _scratch = null!;

    [TestInitialize]
    public async Task CreateScratchAsync() => _scratch = await TestExport.ScratchAsync();

    [TestCleanup]
    public async Task DeleteScratchAsync() => await _scratch.DeleteAsync();

    /// <summary>
    /// The control, and the thing the compositor's return value promises: a recording with
    /// sound exports with that sound, second for second. An export that quietly dropped
    /// the track would still play.
    /// </summary>
    [TestMethod]
    public async Task WriteAsync_KeepsTheRecordingsSoundAgainstTheSamePictureItStartedOn()
    {
        var (written, carried) = await ExportAsync(new VideoEffects());

        Assert.IsTrue(carried, "the export reported that it left the recording's sound behind");
        await AssertHeardAndSeenAsync(written, [(0.5, 0), (1.5, 1), (2.5, 2), (3.5, 3), (4.5, 4)]);
    }

    /// <summary>
    /// A cut takes the sound out with the picture. This is the one that fails when the
    /// track is laid back under the frames whole: the video would be a second shorter than
    /// the audio and everything after the cut would be heard a second early.
    /// </summary>
    [TestMethod]
    public async Task WriteAsync_TakesTheSoundOutOfACutAlongWithTheFramesUnderIt()
    {
        var effects = new VideoEffects();
        effects.Cuts.Add(new VideoCutSegment(2, 3));

        var (written, carried) = await ExportAsync(effects);

        Assert.IsTrue(carried, "the export reported that it left the recording's sound behind");
        Assert.AreEqual(SourceSeconds - 1, await TestVideo.SecondsAsync(written), 0.2);
        await AssertHeardAndSeenAsync(written, [(0.5, 0), (1.5, 1), (2.5, 3), (3.5, 4)]);
    }

    /// <summary>
    /// A speed segment is the only effect that has to resample the track rather than move
    /// it, and it is where the two clocks are most likely to part. Two source seconds
    /// arrive as one, in order, against the frames they belong to.
    /// </summary>
    [TestMethod]
    public async Task WriteAsync_ResamplesTheSoundToMatchAStretchThePictureRunsFasterThrough()
    {
        var effects = new VideoEffects();
        effects.Speeds.Add(new VideoSpeedSegment(2, 4, 2.0));

        var (written, carried) = await ExportAsync(effects);

        Assert.IsTrue(carried, "the export reported that it left the recording's sound behind");
        Assert.AreEqual(SourceSeconds - 1, await TestVideo.SecondsAsync(written), 0.2);

        var sound = await TestAudio.SoundAsync(_scratch, written);

        // The two sped-up seconds, half a second each. Measured over the middle of each so
        // the window never straddles the step between them.
        Assert.AreEqual(2, TestAudio.SecondHeardAt(sound, 2.1, 2.4), "the first sped-up second");
        Assert.AreEqual(3, TestAudio.SecondHeardAt(sound, 2.6, 2.9), "the second sped-up second");

        // …and the seconds on either side are where they were.
        Assert.AreEqual(1, TestAudio.SecondHeardAt(sound, 1.2, 1.8));
        Assert.AreEqual(4, TestAudio.SecondHeardAt(sound, 3.2, 3.8));
    }

    /// <summary>Asserts that the picture and the sound name the same source second.</summary>
    private async Task AssertHeardAndSeenAsync(
        StorageFile written, (double At, int Second)[] expected)
    {
        var sound = await TestAudio.SoundAsync(_scratch, written);

        foreach (var (at, second) in expected)
        {
            Assert.AreEqual(
                second,
                await TestVideo.SecondShownAtAsync(written, at),
                $"output {at:0.00}s shows the wrong source second");

            // A window inside the second rather than the whole of it, so that a step in
            // the ladder never lands in the middle of what is being averaged.
            Assert.AreEqual(
                second,
                TestAudio.SecondHeardAt(sound, at - 0.3, at + 0.3),
                $"output {at:0.00}s sounds like the wrong source second");
        }
    }

    private async Task<(StorageFile File, bool CarriedAudio)> ExportAsync(VideoEffects effects)
    {
        var sound = await TestAudio.WriteToneAsync(_scratch, SourceSeconds);

        return await TestExport.RunAsync(
            _scratch,
            await TestVideo.WriteSecondsAsync(_scratch, SourceSeconds, sound),
            SourceSeconds,
            effects,
            captions: null,
            hasAudio: true);
    }
}
