using Macshot.Windows.Core.Capture;
using Windows.Storage;

namespace Macshot.Windows.Recording.Tests;

/// <summary>
/// What the export does to the <em>clock</em>: which source second ends up at which
/// output moment once a cut, a speed change or a freeze has moved it.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic is covered in Core, where it can be tested off Windows — but Core knows
/// only what the timeline <em>should</em> be. Nothing until now checked that the frames
/// actually written match it, and that gap is where an export that silently drops the
/// last second, or applies a cut to the wrong stretch, would live.
/// </para>
/// <para>
/// Every assertion here is read off the picture: <see cref="TestVideo.WriteSecondsAsync"/>
/// makes each source second its own colour, so "which second is on screen at output time
/// t" is a number.
/// </para>
/// </remarks>
[TestClass]
public sealed class VideoEffectsCompositorTests
{
    /// <summary>Seconds in the synthesized recording, one colour each.</summary>
    private const int SourceSeconds = 6;

    private StorageFolder _scratch = null!;

    [TestInitialize]
    public async Task CreateScratchAsync() => _scratch = await TestExport.ScratchAsync();

    [TestCleanup]
    public async Task DeleteScratchAsync() => await _scratch.DeleteAsync();

    /// <summary>
    /// The control. An export with nothing to apply must still be the recording it was
    /// given — if this fails, every other test here is measuring the harness rather than
    /// the compositor.
    /// </summary>
    [TestMethod]
    public async Task WriteAsync_CarriesEverySecondThroughWhenThereIsNothingToApply()
    {
        var written = await ExportAsync(new VideoEffects());

        Assert.AreEqual(SourceSeconds, await TestVideo.SecondsAsync(written), 0.2);
        await AssertSecondsAsync(written, [(0.5, 0), (1.5, 1), (2.5, 2), (3.5, 3), (4.5, 4), (5.5, 5)]);
    }

    /// <summary>
    /// What a cut is for: the covered stretch is not in the file, and what followed it
    /// starts earlier by exactly as much. A cut that shortened the export without moving
    /// what came after would leave the removed second's frame frozen where it had been.
    /// </summary>
    [TestMethod]
    public async Task WriteAsync_LeavesOutTheSecondsACutCoversAndSlidesTheRestForward()
    {
        var effects = new VideoEffects();
        effects.Cuts.Add(new VideoCutSegment(2, 3));

        var written = await ExportAsync(effects);

        Assert.AreEqual(SourceSeconds - 1, await TestVideo.SecondsAsync(written), 0.2);
        await AssertSecondsAsync(written, [(0.5, 0), (1.5, 1), (2.5, 3), (3.5, 4), (4.5, 5)]);
    }

    /// <summary>
    /// A speed segment compresses its own stretch and nothing else. Two source seconds at
    /// 2x occupy one output second, and the seconds on either side of it keep their own
    /// length — the failure this rules out is a rate applied to the whole export.
    /// </summary>
    [TestMethod]
    public async Task WriteAsync_PlaysOnlyItsOwnStretchFasterAndLeavesTheRestAtOneRate()
    {
        var effects = new VideoEffects();
        effects.Speeds.Add(new VideoSpeedSegment(2, 4, 2.0));

        var written = await ExportAsync(effects);

        Assert.AreEqual(SourceSeconds - 1, await TestVideo.SecondsAsync(written), 0.2);
        await AssertSecondsAsync(
            written,
            [
                (0.5, 0),
                (1.5, 1),

                // The two seconds inside the segment, now half a second each.
                (2.25, 2),
                (2.75, 3),

                (3.5, 4),
                (4.5, 5),
            ]);
    }

    /// <summary>
    /// A freeze holds one frame and then goes on from where it paused, rather than
    /// skipping the hold's worth of recording. Losing that second is the bug the freeze
    /// looks identical to until the export is measured.
    /// </summary>
    [TestMethod]
    public async Task WriteAsync_HoldsOneFrameAndThenResumesFromTheSameMoment()
    {
        var effects = new VideoEffects();
        effects.Freezes.Add(new VideoFreezeSegment(2.5, 1.0));

        var written = await ExportAsync(effects);

        Assert.AreEqual(SourceSeconds + 1, await TestVideo.SecondsAsync(written), 0.2);
        await AssertSecondsAsync(
            written,
            [
                (0.5, 0),
                (1.5, 1),
                (2.2, 2),

                // Inside the hold, which is the frame from source 2.5.
                (3.0, 2),

                // …and then the recording continues from 2.5 rather than from 3.5.
                (4.2, 3),
                (5.2, 4),
                (6.2, 5),
            ]);
    }

    private async Task AssertSecondsAsync(
        StorageFile written, (double At, int Second)[] expected)
    {
        foreach (var (at, second) in expected)
        {
            Assert.AreEqual(
                second,
                await TestVideo.SecondShownAtAsync(written, at),
                $"output {at:0.00}s should show source second {second}");
        }
    }

    private async Task<StorageFile> ExportAsync(VideoEffects effects) =>
        await TestExport.RunAsync(
            _scratch,
            await TestVideo.WriteSecondsAsync(_scratch, SourceSeconds),
            SourceSeconds,
            effects);
}
