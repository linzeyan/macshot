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
    public void Ratio_IsNotOfferedForAnExactSizeSoTheDragIsNotAlsoConstrained()
    {
        // A size is placed, not dragged. Reporting a ratio as well would hand the marquee
        // a lock it should not have and make the box refuse to be the size it says.
        var exact = PreSelectionPreset.OfSize(1920, 1080);

        Assert.IsTrue(exact.IsExact);
        Assert.IsNull(exact.Ratio);
    }
}
