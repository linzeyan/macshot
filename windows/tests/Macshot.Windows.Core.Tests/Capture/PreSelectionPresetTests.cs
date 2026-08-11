using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class PreSelectionPresetTests
{
    [TestMethod]
    public void OfRatio_RefusesTheZeroThatMeansNoShapeAtAll()
    {
        // Zero is how "nothing held" is spelt in the settings file. Taken as a ratio it
        // would divide every height away to nothing, so the drag would collapse to a line
        // the moment the pointer moved.
        Assert.AreEqual(PreSelectionPreset.Freeform, PreSelectionPreset.OfRatio(0));
        Assert.AreEqual(PreSelectionPreset.Freeform, PreSelectionPreset.OfRatio(-2));
        Assert.AreEqual(PreSelectionPreset.Freeform, PreSelectionPreset.OfRatio(double.NaN));
    }

    [TestMethod]
    public void OfSize_RefusesASizeWithNoAreaSoNoDragIsPinnedToNothing()
    {
        // A width of zero from a hand-edited file would place a box the user cannot see,
        // and every press would deliver it. Freeform at least still takes a drag.
        Assert.AreEqual(PreSelectionPreset.Freeform, PreSelectionPreset.OfSize(0, 1080));
        Assert.AreEqual(PreSelectionPreset.Freeform, PreSelectionPreset.OfSize(1920, 0));
    }

    [TestMethod]
    public void Label_NamesAShapeTheWayTheMenuThatOfferedItDoes()
    {
        // The button and the menu have to be talking about the same thing: a tooltip
        // reading "1.78 : 1" over a menu that says "16 : 9" reads as two settings.
        Assert.AreEqual("16 : 9", PreSelectionPreset.OfRatio(16d / 9d).Label);
        Assert.AreEqual("1920 × 1080", PreSelectionPreset.OfSize(1920, 1080).Label);
    }

    [TestMethod]
    public void Label_FallsBackToTheNumbersForAShapeTheCatalogueDoesNotHave()
    {
        // The stored ratio can outlive the catalogue that named it. A button that says
        // nothing at all would read as holding nothing, which is the one thing it is not.
        Assert.AreEqual("2.40 : 1", PreSelectionPreset.OfRatio(2.4).Label);
        Assert.AreEqual("2560 × 1440", PreSelectionPreset.OfSize(2560, 1440).Label);
    }

    [TestMethod]
    public void Label_SaysNothingWhenNothingIsHeld()
    {
        // The label is what turns the button's active state on. Freeform is the ordinary
        // state, and a button lit up over it would claim a shape nobody chose.
        Assert.IsNull(PreSelectionPreset.Freeform.Label);
    }

    [TestMethod]
    public void Selects_TicksOneRowAcrossBothColumnsSoAHeldSizeIsNotReadAsFreeform()
    {
        // The two columns are one choice. Ticking Freeform beside 800 × 600 told the user
        // nothing was held at the moment every press would place an 800 × 600 box — which
        // is exactly how the fixed marquee came to look like a broken drag.
        var held = PreSelectionPreset.OfSize(800, 600);
        var freeform = ResolutionPresets.Ratios[0];
        var size = ResolutionPresets.Sizes.First(preset => preset.Width == 800);

        Assert.IsTrue(held.Selects(size));
        Assert.IsFalse(held.Selects(freeform));
        Assert.AreEqual(1, ResolutionPresets.Ratios.Concat(ResolutionPresets.Sizes).Count(held.Selects));
    }

    [TestMethod]
    public void Selects_MatchesAShapeThroughTheRoundingTwoDivisionsLeaveBehind()
    {
        // An aspect is a division: 1920 / 1080 and 16 / 9 are the same shape and not the
        // same double. Compared exactly, a ratio picked from the menu would fail to tick
        // the row it was picked from.
        var held = PreSelectionPreset.OfRatio(1920d / 1080d);

        Assert.IsTrue(held.Selects(ResolutionPresets.Ratios.First(preset => preset.Label == "16 : 9")));
    }

    [TestMethod]
    public void Selects_PutsTheTickOnFreeformWhenNothingIsHeld()
    {
        // A list with no tick at all reads as a list that has not loaded, and freeform is
        // the ordinary state rather than an unset one.
        Assert.IsTrue(PreSelectionPreset.Freeform.Selects(ResolutionPresets.Ratios[0]));
        Assert.AreEqual(
            1,
            ResolutionPresets.Ratios.Concat(ResolutionPresets.Sizes)
                .Count(PreSelectionPreset.Freeform.Selects));
    }

    [TestMethod]
    public void Ratio_IsNotOfferedForAnExactSizeSoTheDragIsNotAlsoConstrained()
    {
        // A size is placed, not dragged. Reporting a ratio as well would hand the marquee
        // a lock it should not have and make the box refuse to be the size it says.
        var exact = PreSelectionPreset.OfSize(1920, 1080);

        Assert.IsTrue(exact.IsExact);
        Assert.IsNull(exact.Ratio);
    }
}
