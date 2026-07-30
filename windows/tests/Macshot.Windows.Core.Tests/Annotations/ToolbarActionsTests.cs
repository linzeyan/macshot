using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Annotations;

/// <summary>
/// What each strip carries, and in what order.
/// </summary>
[TestClass]
public sealed class ToolbarActionsTests
{
    [TestMethod]
    public void EveryToolTheRendererCanDraw_HasAPlaceInTheOrder()
    {
        // A tool added to the renderer without being placed here would otherwise appear
        // nowhere at all, and nothing else would notice.
        foreach (var tool in AnnotationRasterizer.SupportedTools)
        {
            Assert.IsTrue(
                ToolbarActions.ToolOrder.Contains(tool),
                $"{tool} can be drawn but has no place on the toolbar");
        }
    }

    [TestMethod]
    public void TheOrderStartsTheWayMacshotDoes()
    {
        // Muscle memory is the whole point of matching it: someone who reaches for the
        // third button expecting an arrow must get an arrow.
        CollectionAssert.AreEqual(
            new[]
            {
                AnnotationTool.Pencil,
                AnnotationTool.Line,
                AnnotationTool.Arrow,
                AnnotationTool.Rectangle,
                AnnotationTool.Ellipse,
                AnnotationTool.Marker,
            },
            ToolbarActions.ToolOrder.Take(6).ToArray());
    }

    [TestMethod]
    public void ThereIsNoPointerButton()
    {
        // A click grabs a mark whatever tool is in hand, so a button for it would be a
        // button for something that already happens.
        Assert.IsFalse(ToolbarActions.ToolOrder.Contains(AnnotationTool.Select));
        Assert.IsFalse(ToolbarActions.Tools(AnnotationTool.Arrow).Any(item => item.Tool == AnnotationTool.Select));
    }

    [TestMethod]
    public void TheToolInHand_IsTheOneMarkedSelected()
    {
        var items = ToolbarActions.Tools(AnnotationTool.Ellipse);

        var selected = items.Where(item => item.IsSelected).ToArray();
        Assert.AreEqual(1, selected.Length, "exactly one button can be the current tool");
        Assert.AreEqual(AnnotationTool.Ellipse, selected[0].Tool);
    }

    [TestMethod]
    public void TheColourAndTheUndoPair_ComeAfterTheTools()
    {
        var items = ToolbarActions.Tools(AnnotationTool.Arrow);
        var commands = items.Select(item => item.Command).ToArray();

        var lastTool = Array.LastIndexOf(commands, ToolbarCommand.PickTool);
        Assert.IsTrue(Array.IndexOf(commands, ToolbarCommand.PickColor) > lastTool);
        Assert.IsTrue(Array.IndexOf(commands, ToolbarCommand.Undo) > lastTool);
        Assert.IsTrue(
            Array.IndexOf(commands, ToolbarCommand.Redo) > Array.IndexOf(commands, ToolbarCommand.Undo),
            "redo follows undo, the way every program puts them");
    }

    [TestMethod]
    public void ATurnedOffTool_IsNotOnTheStripAtAll()
    {
        var kept = new[] { AnnotationTool.Arrow, AnnotationTool.Text };

        var items = ToolbarActions.Tools(AnnotationTool.Arrow, kept);

        CollectionAssert.AreEqual(
            kept,
            items.Where(item => item.Command == ToolbarCommand.PickTool).Select(item => item.Tool).ToArray());
    }

    [TestMethod]
    public void TheActionStrip_StartsWithCancelAndEndsWithWhatToDoWithIt()
    {
        var items = ToolbarActions.Actions(editorMode: false);

        Assert.AreEqual(ToolbarCommand.Cancel, items[0].Command, "the way out comes first");
        CollectionAssert.IsSubsetOf(
            new[] { ToolbarCommand.Copy, ToolbarCommand.Save, ToolbarCommand.Pin },
            items.Select(item => item.Command).ToArray());
    }

    [TestMethod]
    public void TheEditorHasNoRegionToCancelOrMove()
    {
        var items = ToolbarActions.Actions(editorMode: true).Select(item => item.Command).ToArray();

        CollectionAssert.DoesNotContain(items, ToolbarCommand.Cancel);
        CollectionAssert.DoesNotContain(items, ToolbarCommand.MoveSelection);
        CollectionAssert.DoesNotContain(items, ToolbarCommand.OpenEditor, "it is already open");
        CollectionAssert.Contains(items, ToolbarCommand.Copy);
    }

    [TestMethod]
    public void EveryButtonSaysWhatItIs()
    {
        var items = ToolbarActions.Tools(AnnotationTool.Arrow).Concat(ToolbarActions.Actions(false));

        foreach (var item in items)
        {
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(item.Tooltip),
                $"{item.Command} {item.Tool} has no tooltip, and an icon with no name is a guess");
        }
    }
}
