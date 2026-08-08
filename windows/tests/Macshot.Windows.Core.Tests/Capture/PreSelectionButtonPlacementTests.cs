using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class PreSelectionButtonPlacementTests
{
    private static readonly CaptureRegion Button =
        new(0, 0, PreSelectionButtonPlacement.Width, PreSelectionButtonPlacement.Height);

    [TestMethod]
    public void For_CentresTheButtonUnderTheInstructionItBelongsTo()
    {
        // It is the instruction's own control rather than a floating one: off to a side it
        // would read as belonging to whatever else is on that half of the screen.
        var pill = new CaptureRegion(400, 300, 520, 100);

        var button = PreSelectionButtonPlacement.For(pill, Button);

        Assert.AreEqual(
            pill.X + (pill.Width / 2),
            button.X + (button.Width / 2),
            1e-9,
            "the button and the pill must share a centre line");
    }

    [TestMethod]
    public void For_SitsInsideThePillsPaddingRatherThanOnItsEdge()
    {
        // macshot reserves the strip inside the padding for it. Placed on the edge the
        // button would touch the rounded corner and read as hanging off the pill.
        var pill = new CaptureRegion(0, 0, 520, 100);

        var button = PreSelectionButtonPlacement.For(pill, Button);

        Assert.AreEqual(pill.Bottom - PreSelectionButtonPlacement.Padding, button.Bottom, 1e-9);
        Assert.IsTrue(button.Y > pill.Y, "the button must stay below the text it sits under");
    }

    [TestMethod]
    public void Reserved_LeavesRoomForTheButtonAndTheGapAboveIt()
    {
        // The pill is measured from its text alone, so whatever this answers is the only
        // thing keeping the button off the last line of the instruction.
        Assert.AreEqual(
            PreSelectionButtonPlacement.Height + PreSelectionButtonPlacement.Gap,
            PreSelectionButtonPlacement.Reserved(PreSelectionButtonPlacement.Height),
            1e-9);
    }

    [TestMethod]
    public void LeastWidth_KeepsAShortPillWideEnoughToHoldTheButton()
    {
        // The instruction is wider than the button in every language macshot ships, so
        // this only binds on a pill carrying something short — where, without it, the
        // button would be wider than the slab it is drawn on.
        var least = PreSelectionButtonPlacement.LeastWidth(PreSelectionButtonPlacement.Width);
        var pill = new CaptureRegion(0, 0, least, 80);

        var button = PreSelectionButtonPlacement.For(pill, Button);

        Assert.AreEqual(PreSelectionButtonPlacement.Padding, button.X - pill.X, 1e-9);
        Assert.AreEqual(PreSelectionButtonPlacement.Padding, pill.Right - button.Right, 1e-9);
    }
}
