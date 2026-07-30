using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Annotations;

/// <summary>
/// Where the toolbars land around the selection.
/// </summary>
/// <remarks>
/// The screen here is 1920x1080 at the origin, and the sizes are close to the real
/// strips: a wide row of tool buttons, a tall column of action buttons, and an options
/// row the width of the tools.
/// </remarks>
[TestClass]
public sealed class ToolbarPlacementTests
{
    private static readonly CaptureRegion Screen = new(0, 0, 1920, 1080);

    private static readonly ToolbarSizes Sizes = new(
        Tools: new CaptureRegion(0, 0, 420, 40),
        Actions: new CaptureRegion(0, 0, 40, 280),
        OptionsRow: new CaptureRegion(0, 0, 300, 36));

    [TestMethod]
    public void ToolsSitUnderTheSelection_CentredOnIt()
    {
        var layout = ToolbarPlacement.For(new CaptureRegion(600, 400, 400, 300), Screen, Sizes);

        Assert.AreEqual(400 + 300 + ToolbarPlacement.Gap, layout.Tools.Y, "below the selection");
        Assert.AreEqual(600 + ((400 - 420) / 2d), layout.Tools.X, "centred on it");
    }

    [TestMethod]
    public void TheOptionsRowFollowsTheToolsRatherThanTheSelection()
    {
        // Two surfaces for one tool. Placed separately they end up on opposite sides of
        // the selection the moment one of them runs out of room.
        var layout = ToolbarPlacement.For(new CaptureRegion(600, 400, 400, 300), Screen, Sizes);

        Assert.AreEqual(layout.Tools.Bottom + ToolbarPlacement.RowGap, layout.OptionsRow.Y);
        Assert.AreEqual(layout.Tools.Width, layout.OptionsRow.Width, "as wide as the strip above it");
    }

    [TestMethod]
    public void AToolWithNoOptions_GetsNoRowAndNoRoomReservedForOne()
    {
        var bare = Sizes with { OptionsRow = default };

        // This selection leaves 60 below it: enough for the 40-high strip, not enough
        // for the strip and a 36-high row.
        var selection = new CaptureRegion(600, 850, 400, 170);

        var withRow = ToolbarPlacement.For(selection, Screen, Sizes);
        var withoutRow = ToolbarPlacement.For(selection, Screen, bare);

        Assert.IsTrue(withoutRow.OptionsRow.IsEmpty);
        Assert.AreEqual(selection.Bottom + ToolbarPlacement.Gap, withoutRow.Tools.Y, "the strip alone fits below");
        Assert.IsTrue(withRow.Tools.Bottom < selection.Y, "with a row to carry, the pair goes above");
    }

    [TestMethod]
    public void ASelectionAtTheBottomOfTheScreen_PutsTheToolsAboveIt()
    {
        var layout = ToolbarPlacement.For(new CaptureRegion(600, 700, 400, 380), Screen, Sizes);

        Assert.IsTrue(layout.Tools.Bottom < 700, "clear of the selection's top edge");
        Assert.IsTrue(layout.OptionsRow.Bottom <= 700, "and so is the row it carries");
    }

    [TestMethod]
    public void ASelectionCoveringTheWholeScreen_KeepsTheToolsOnScreen()
    {
        var layout = ToolbarPlacement.For(Screen, Screen, Sizes);

        Assert.IsTrue(layout.Tools.Y >= ToolbarPlacement.ScreenMargin);
        Assert.IsTrue(layout.OptionsRow.Bottom <= Screen.Bottom - ToolbarPlacement.ScreenMargin);
    }

    [TestMethod]
    public void ActionsSitDownTheRightEdge_TopAlignedWithTheSelection()
    {
        var layout = ToolbarPlacement.For(new CaptureRegion(600, 400, 400, 300), Screen, Sizes);

        Assert.AreEqual(1000 + ToolbarPlacement.Gap, layout.Actions.X);
        Assert.AreEqual(400, layout.Actions.Y, "the eye finds a column of actions at the top");
    }

    [TestMethod]
    public void ASelectionAgainstTheRightEdge_PutsTheActionsOnItsLeft()
    {
        var layout = ToolbarPlacement.For(new CaptureRegion(1200, 400, 700, 300), Screen, Sizes);

        Assert.IsTrue(layout.Actions.Right <= 1200, "outside the selection, not over it");
    }

    [TestMethod]
    public void ASelectionSpanningTheWholeWidth_KeepsTheActionsInsideItsRightEdge()
    {
        // There is no outside left. Inside is a last resort — it covers part of what is
        // being captured — but it is still beside the selection's edge rather than
        // stranded at the far side of the display.
        var layout = ToolbarPlacement.For(new CaptureRegion(0, 300, 1920, 400), Screen, Sizes);

        Assert.IsTrue(layout.Actions.Right <= 1920 - ToolbarPlacement.ScreenMargin);
        Assert.IsTrue(layout.Actions.X > 1920 - 200, "against the right edge, not the left");
        Assert.IsFalse(Overlaps(layout.Actions, layout.Tools), "and clear of the tools");
    }

    [TestMethod]
    public void TheTwoStripsNeverCoverEachOther()
    {
        foreach (var selection in Selections())
        {
            var layout = ToolbarPlacement.For(selection, Screen, Sizes);

            Assert.IsFalse(
                Overlaps(layout.Tools, layout.Actions),
                $"tools and actions collide for {selection}");
            Assert.IsFalse(
                Overlaps(layout.OptionsRow, layout.Actions),
                $"the row and the actions collide for {selection}");
        }
    }

    [TestMethod]
    public void EveryPieceStaysOnScreen()
    {
        foreach (var selection in Selections())
        {
            var layout = ToolbarPlacement.For(selection, Screen, Sizes);

            foreach (var piece in new[] { layout.Tools, layout.Actions, layout.OptionsRow })
            {
                if (piece.IsEmpty)
                {
                    continue;
                }

                Assert.IsTrue(piece.X >= Screen.X, $"{piece} is off the left of {selection}");
                Assert.IsTrue(piece.Y >= Screen.Y, $"{piece} is off the top of {selection}");
                Assert.IsTrue(piece.Right <= Screen.Right, $"{piece} is off the right of {selection}");
                Assert.IsTrue(piece.Bottom <= Screen.Bottom, $"{piece} is off the bottom of {selection}");
            }
        }
    }

    [TestMethod]
    public void TheActionsDodgeTheSizeBoxRatherThanSittingUnderIt()
    {
        var selection = new CaptureRegion(600, 400, 400, 300);
        var undisturbed = ToolbarPlacement.For(selection, Screen, Sizes);

        // The box sits just outside the selection's bottom-right corner, which is where
        // the actions strip would otherwise reach down to.
        var box = new CaptureRegion(undisturbed.Actions.X - 10, undisturbed.Actions.Y + 20, 140, 30);

        var dodged = ToolbarPlacement.For(selection, Screen, Sizes, box);

        Assert.IsFalse(Overlaps(dodged.Actions, box));
    }

    /// <summary>A sweep of selections, including every corner and edge of the screen.</summary>
    private static IEnumerable<CaptureRegion> Selections()
    {
        double[] positions = [0, 4, 300, 900, 1500, 1880];
        double[] sizes = [20, 200, 900, 1900];

        foreach (var x in positions)
        {
            foreach (var y in positions)
            {
                foreach (var size in sizes)
                {
                    var width = Math.Min(size, Screen.Right - x);
                    var height = Math.Min(size, Screen.Bottom - y);
                    if (width > 0 && height > 0)
                    {
                        yield return new CaptureRegion(x, y, width, height);
                    }
                }
            }
        }
    }

    private static bool Overlaps(CaptureRegion left, CaptureRegion right) =>
        !left.IsEmpty
        && !right.IsEmpty
        && left.X < right.Right
        && right.X < left.Right
        && left.Y < right.Bottom
        && right.Y < left.Bottom;
}
