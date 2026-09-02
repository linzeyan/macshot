using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

/// <summary>
/// What the band writes on a pill, and what it asks the string table for.
/// </summary>
[TestClass]
public sealed class VideoEffectLabelsTests
{
    /// <summary>
    /// A whole number reads as one. macshot's pills say "2x", not "2.0x": the band is 22
    /// points tall and a trailing ".0" on every pill spends a fifth of a short pill's
    /// width saying nothing.
    /// </summary>
    [TestMethod]
    public void Zoom_DropsTheDecimalOnAWholeNumber()
    {
        Assert.AreEqual("2x", VideoEffectLabels.Zoom(2.0));
        Assert.AreEqual("1.5x", VideoEffectLabels.Zoom(1.5));
    }

    /// <summary>
    /// A factor that arrived from arithmetic rather than from the preset list still reads
    /// as the preset. Widening a segment recomputes its factor, and a pill that then said
    /// "2.0000001×" would look like a fault in the export rather than in the label.
    /// </summary>
    [TestMethod]
    public void Speed_RoundsBeforeDecidingWhetherToShowADecimal()
    {
        Assert.AreEqual("2×", VideoEffectLabels.Speed(1.9999999));
        Assert.AreEqual("0.25×", VideoEffectLabels.Speed(0.25));
    }

    /// <summary>
    /// A cut always shows a decimal, unlike every other label. A cut's whole statement is
    /// how much is being removed, and the shortest the band allows is a tenth of a second
    /// — rounded to "0s" the pill would claim to do nothing.
    /// </summary>
    [TestMethod]
    public void Cut_KeepsOneDecimalSoTheShortestCutStillReadsAsOne()
    {
        Assert.AreEqual("0.1s", VideoEffectLabels.Cut(VideoCutSegment.MinDuration));
        Assert.AreEqual("1.0s", VideoEffectLabels.Cut(1.0));
    }

    /// <summary>
    /// Every kind has its own Add and Delete wording, and no two share one. The band's
    /// two buttons are relabelled from these as the picker changes, so a kind that fell
    /// through to another's key would put the wrong verb on the button acting on it.
    /// </summary>
    [TestMethod]
    public void AddAndDeleteKeys_AreDistinctForEveryKind()
    {
        var kinds = Enum.GetValues<VideoEffectKind>();

        CollectionAssert.AllItemsAreUnique(kinds.Select(VideoEffectLabels.AddKey).ToList());
        CollectionAssert.AllItemsAreUnique(kinds.Select(VideoEffectLabels.DeleteKey).ToList());
    }

    /// <summary>
    /// The keys are the Mac app's own menu titles verbatim, which is the whole reason this
    /// band arrives translated into forty languages without a string being written here. A
    /// key edited to read better would fall back to English in every one of them.
    /// </summary>
    [TestMethod]
    public void Keys_AreTheMacMenuTitlesVerbatim()
    {
        Assert.AreEqual("Add Zoom", VideoEffectLabels.AddKey(VideoEffectKind.Zoom));
        Assert.AreEqual("Delete Censor", VideoEffectLabels.DeleteKey(VideoEffectKind.Censor));
        Assert.AreEqual("Pixelate", VideoEffectLabels.StyleKey(VideoCensorStyle.Pixelate));
        Assert.AreEqual("Rounded", VideoEffectLabels.BackgroundKey(VideoTextBackground.Rounded));
    }

    /// <summary>
    /// Every caption background has its own word. The picker beside the band is filled from
    /// this in enum order and read back by position, so two kinds sharing a key would be two
    /// entries reading the same and one of them unreachable.
    /// </summary>
    [TestMethod]
    public void BackgroundKeys_AreDistinctForEveryKind()
    {
        var backgrounds = Enum.GetValues<VideoTextBackground>();

        CollectionAssert.AllItemsAreUnique(backgrounds.Select(VideoEffectLabels.BackgroundKey).ToList());
    }
}
