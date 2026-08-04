using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class VideoEffectsTests
{
    private const double Tolerance = 1e-9;

    /// <summary>
    /// Which export path runs is decided from these questions, and the difference between
    /// them is the difference between one call to the platform and decoding every frame
    /// by hand. A band holding nothing must take neither of the expensive paths.
    /// </summary>
    [TestMethod]
    public void NeedsFramePipeline_IsFalseForABandWithNothingOnIt()
    {
        var effects = new VideoEffects();

        Assert.IsTrue(effects.IsEmpty);
        Assert.IsFalse(effects.NeedsFramePipeline);
        Assert.IsFalse(effects.ChangesAnything);
    }

    /// <summary>
    /// A cut alone must not force the hand-built pipeline. Windows can express a cut as
    /// several clips in one composition, which keeps the platform's encoder and — the
    /// reason it matters — the recording's audio, at a fraction of the time.
    /// </summary>
    [TestMethod]
    public void NeedsFramePipeline_StaysFalseForACutWhichThePlatformCanDoItself()
    {
        var effects = new VideoEffects();
        effects.Cuts.Add(new VideoCutSegment(2, 4));

        Assert.IsFalse(effects.NeedsFramePipeline);
        Assert.IsTrue(effects.HasCuts);
        Assert.IsTrue(effects.ChangesAnything);
    }

    /// <summary>
    /// A speed or a freeze has to take the frame pipeline even with no pixel effect
    /// beside it, because nothing in Windows re-times a track. Reading this the other way
    /// would produce an export the length of the source with the segments simply absent.
    /// </summary>
    [TestMethod]
    public void NeedsFramePipeline_IsTrueForRetimingEvenWithNoPixelWorkToJustifyIt()
    {
        var sped = new VideoEffects();
        sped.Speeds.Add(new VideoSpeedSegment(2, 4, 2));
        Assert.IsTrue(sped.NeedsFramePipeline);
        Assert.IsFalse(sped.NeedsPixelWork);

        var held = new VideoEffects();
        held.Freezes.Add(new VideoFreezeSegment(2, 1));
        Assert.IsTrue(held.NeedsFramePipeline);
    }

    /// <summary>
    /// A zoom the user dragged down to no magnification is not pixel work, and paying for
    /// a frame-by-frame export because of one would make an untouched recording several
    /// times slower to save for no visible difference.
    /// </summary>
    [TestMethod]
    public void NeedsPixelWork_IgnoresAZoomThatDoesNotMagnify()
    {
        var effects = new VideoEffects();
        effects.Zooms.Add(VideoZoomSegment.Placed(5, 20) with { Level = 1 });

        Assert.IsFalse(effects.NeedsPixelWork);
        Assert.IsFalse(effects.NeedsFramePipeline);
    }

    /// <summary>
    /// Only a speed segment costs the recording its sound, and the editor says so before
    /// writing rather than leaving it to be found on playback. A freeze is silent by
    /// design on both products, so it is not something to warn about.
    /// </summary>
    [TestMethod]
    public void SilencesAnything_NamesTheSpeedSegmentAndNotTheFreeze()
    {
        var sped = new VideoEffects();
        sped.Speeds.Add(new VideoSpeedSegment(2, 4, 2));
        Assert.IsTrue(sped.SilencesAnything);

        var held = new VideoEffects();
        held.Freezes.Add(new VideoFreezeSegment(2, 1));
        Assert.IsFalse(held.SilencesAnything);

        var cut = new VideoEffects();
        cut.Cuts.Add(new VideoCutSegment(2, 4));
        Assert.IsFalse(cut.SilencesAnything);
    }

    /// <summary>
    /// The length shown beside the export buttons and the length the encoder is told to
    /// expect both come from here, so the three temporal effects have to compose in one
    /// answer rather than in three places that could drift apart.
    /// </summary>
    [TestMethod]
    public void OutputSeconds_AccountsForTheTrimTheCutsTheSpeedsAndTheFreezesAtOnce()
    {
        var effects = new VideoEffects();
        effects.Cuts.Add(new VideoCutSegment(8, 10));
        effects.Speeds.Add(new VideoSpeedSegment(2, 4, 2));
        effects.Freezes.Add(new VideoFreezeSegment(6, 1));

        Assert.AreEqual(8, effects.OutputSeconds(new VideoTrim(0, 10)), Tolerance);
    }

    /// <summary>
    /// Deleting a pill has to remove the one that was selected. Selection is by kind and
    /// position, so an index that no longer exists — the band asking twice, a stale
    /// click — must do nothing rather than take out the wrong segment.
    /// </summary>
    [TestMethod]
    public void Remove_TakesTheSegmentThatWasPointedAtAndIgnoresAnIndexThatIsGone()
    {
        var effects = new VideoEffects();
        effects.Cuts.Add(new VideoCutSegment(1, 2));
        effects.Cuts.Add(new VideoCutSegment(5, 6));

        effects.Remove(VideoEffectKind.Cut, 0);
        Assert.AreEqual(1, effects.Cuts.Count);
        Assert.AreEqual(5, effects.Cuts[0].Start, Tolerance);

        effects.Remove(VideoEffectKind.Cut, 7);
        Assert.AreEqual(1, effects.Cuts.Count);
    }
}

[TestClass]
public sealed class VideoBandRowsTests
{
    /// <summary>
    /// Effects that do not overlap in time all belong on one row. A band that grew a row
    /// per effect would push the export controls off a window macshot sizes to 420 points
    /// tall.
    /// </summary>
    [TestMethod]
    public void Assign_KeepsEffectsThatDoNotOverlapOnTheSameRow()
    {
        var rows = VideoBandRows.Assign([new VideoTimeRange(0, 2), new VideoTimeRange(3, 5)]);

        CollectionAssert.AreEqual(new[] { 0, 0 }, rows.ToArray());
        Assert.AreEqual(1, VideoBandRows.RowCount(rows));
    }

    /// <summary>
    /// Two effects running at the same moment must stack, because a pill drawn over
    /// another is one the user cannot click on — and both of them are things they placed
    /// deliberately.
    /// </summary>
    [TestMethod]
    public void Assign_StacksEffectsThatRunAtTheSameMoment()
    {
        var rows = VideoBandRows.Assign([new VideoTimeRange(0, 5), new VideoTimeRange(2, 7)]);

        CollectionAssert.AreEqual(new[] { 0, 1 }, rows.ToArray());
        Assert.AreEqual(2, VideoBandRows.RowCount(rows));
    }

    /// <summary>
    /// Rows are packed in time order rather than in the order effects were added, so
    /// placing one at the beginning of the recording does not shuffle everything already
    /// on the band up a row in front of the user.
    /// </summary>
    [TestMethod]
    public void Assign_PacksInTimeOrderSoAddingAnEarlyEffectDoesNotReshuffleTheBand()
    {
        var rows = VideoBandRows.Assign(
            [new VideoTimeRange(6, 9), new VideoTimeRange(0, 2), new VideoTimeRange(7, 10)]);

        Assert.AreEqual(0, rows[1]);
        Assert.AreEqual(0, rows[0]);
        Assert.AreEqual(1, rows[2]);
    }

    /// <summary>
    /// An empty band is still one row tall. A band that collapsed when its last effect
    /// was deleted would take the place the user is about to click on with it.
    /// </summary>
    [TestMethod]
    public void RowCount_IsOneEvenWithNothingOnTheBand()
    {
        Assert.AreEqual(1, VideoBandRows.RowCount(VideoBandRows.Assign([])));
    }
}
