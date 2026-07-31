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
    public void WhatChangesThePicture_ComesAfterWhatDrawsOnIt()
    {
        // macshot ends the bottom strip with the actions that rewrite the pixels rather
        // than mark them — invert, adjust, beautify, remove background. Beautify is the
        // only one the port has, so it has to land after undo and redo, where macshot's
        // block begins, and not among the tools.
        var commands = ToolbarActions.Tools(AnnotationTool.Arrow).Select(item => item.Command).ToArray();

        Assert.IsTrue(
            Array.IndexOf(commands, ToolbarCommand.InvertColors) > Array.IndexOf(commands, ToolbarCommand.Redo),
            "the block that rewrites the pixels comes after the undo pair, not among the tools");
        Assert.IsTrue(
            Array.IndexOf(commands, ToolbarCommand.Beautify) > Array.IndexOf(commands, ToolbarCommand.InvertColors),
            "macshot lists invert before beautify, and the gaps between them are for the two the port lacks");
        Assert.IsTrue(
            Array.IndexOf(commands, ToolbarCommand.Redact) > Array.IndexOf(commands, ToolbarCommand.Beautify),
            "the port's own redact button follows macshot's block rather than sitting inside it");
    }

    [TestMethod]
    public void TheSwitchesSayWhetherTheyAreOn()
    {
        // Nothing in the overlay can show the gradient frame — it is bigger than the
        // region — so for that one the lit button is the only thing that says so. Invert
        // is previewed, and still lights, because a switch whose button looks the same
        // both ways reads as a button that did nothing the second time.
        Assert.IsFalse(Item(ToolbarActions.Tools(AnnotationTool.Arrow), ToolbarCommand.Beautify).IsSelected);
        Assert.IsFalse(Item(ToolbarActions.Tools(AnnotationTool.Arrow), ToolbarCommand.InvertColors).IsSelected);

        var on = ToolbarActions.Tools(AnnotationTool.Arrow, null, beautified: true, inverted: true);
        Assert.IsTrue(Item(on, ToolbarCommand.Beautify).IsSelected);
        Assert.IsTrue(Item(on, ToolbarCommand.InvertColors).IsSelected);

        static ToolbarItem Item(IReadOnlyList<ToolbarItem> items, ToolbarCommand command) =>
            items.Single(item => item.Command == command);
    }

    [TestMethod]
    public void ScrollCaptureAndRecording_ComeLastAndOnlyOverALiveScreen()
    {
        var overlay = ToolbarActions.Actions(editorMode: false).Select(item => item.Command).ToArray();

        Assert.AreEqual(ToolbarCommand.Record, overlay[^1], "macshot ends the strip with it");
        Assert.AreEqual(ToolbarCommand.ScrollCapture, overlay[^2]);
        Assert.IsTrue(
            Array.IndexOf(overlay, ToolbarCommand.ScrollCapture) > Array.IndexOf(overlay, ToolbarCommand.ReadText),
            "both follow the output actions, the way macshot orders them");

        // There is no window behind an image in the editor to scroll, and nothing there
        // to record: both aim at a screen that is still moving.
        var editor = ToolbarActions.Actions(editorMode: true).Select(item => item.Command).ToArray();
        CollectionAssert.DoesNotContain(editor, ToolbarCommand.ScrollCapture);
        CollectionAssert.DoesNotContain(editor, ToolbarCommand.Record);
    }

    [TestMethod]
    public void Share_FollowsSaving_InBothHosts()
    {
        // macshot puts it between saving and pinning, and it is offered wherever there
        // are finished pixels — the editor has those too.
        foreach (var editorMode in new[] { false, true })
        {
            var items = ToolbarActions.Actions(editorMode).Select(item => item.Command).ToArray();

            Assert.AreEqual(
                Array.IndexOf(items, ToolbarCommand.Save) + 1,
                Array.IndexOf(items, ToolbarCommand.Share),
                $"editorMode {editorMode}");
        }
    }

    [TestMethod]
    public void Translate_FollowsReadingTheText_AndIsAbsentWithoutATranslator()
    {
        // macshot's own order: pin, read, translate, then the two that aim at a live
        // screen. The two that read words sit next to each other because they are
        // answered from the same recognition pass.
        var overlay = ToolbarActions.Actions(editorMode: false).Select(item => item.Command).ToArray();

        Assert.AreEqual(
            Array.IndexOf(overlay, ToolbarCommand.ReadText) + 1,
            Array.IndexOf(overlay, ToolbarCommand.Translate));

        // The offline build has no translator compiled into it, so a button for one
        // would be a button that does nothing.
        CollectionAssert.DoesNotContain(
            ToolbarActions.Actions(editorMode: false, translation: false).Select(item => item.Command).ToArray(),
            ToolbarCommand.Translate);
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
