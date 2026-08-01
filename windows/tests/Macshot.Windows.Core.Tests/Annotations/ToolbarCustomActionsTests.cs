using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Annotations;

[TestClass]
public sealed class ToolbarCustomActionsTests
{
    [TestMethod]
    public void EveryHideableActionIsOnTheStripItIsListedUnder()
    {
        // A tick box in the "Bottom Toolbar Actions" list that hides something from the
        // right strip would be a tick box that appears to do nothing.
        var bottom = ToolbarActions.Tools(AnnotationTool.Arrow).Select(item => item.Command).ToArray();
        var right = ToolbarActions.Actions(editorMode: false).Select(item => item.Command).ToArray();

        foreach (var action in ToolbarCustomActions.Bottom)
        {
            CollectionAssert.Contains(bottom, action.Command, action.Id);
        }

        foreach (var action in ToolbarCustomActions.Right)
        {
            CollectionAssert.Contains(right, action.Command, action.Id);
        }
    }

    [TestMethod]
    public void EveryActionHasAnIdentifierAndANameOfItsOwn()
    {
        foreach (var action in ToolbarCustomActions.All)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(action.Id), action.Command.ToString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(action.Label), action.Id);
        }

        CollectionAssert.AllItemsAreUnique(ToolbarCustomActions.All.Select(action => action.Id).ToArray());
        CollectionAssert.AllItemsAreUnique(ToolbarCustomActions.All.Select(action => action.Command).ToArray());
    }

    [TestMethod]
    public void HidingOneTakesItOffTheStripAndLeavesTheRest()
    {
        var hidden = new[] { "pin" };

        var shown = ToolbarActions.Actions(editorMode: false, hiddenActions: hidden)
            .Select(item => item.Command)
            .ToArray();

        CollectionAssert.DoesNotContain(shown, ToolbarCommand.Pin);
        CollectionAssert.Contains(shown, ToolbarCommand.ReadText);
    }

    /// <summary>
    /// The floor under the feature: a toolbar with nothing on it is not a preference.
    /// </summary>
    [TestMethod]
    public void WhatCannotBeHiddenIsStillThereWhenEverythingElseIs()
    {
        var everything = ToolbarCustomActions.All.Select(action => action.Id).ToArray();

        var shown = ToolbarActions.Actions(editorMode: false, hiddenActions: everything)
            .Select(item => item.Command)
            .ToArray();

        CollectionAssert.Contains(shown, ToolbarCommand.Copy);
        CollectionAssert.Contains(shown, ToolbarCommand.Save);
        CollectionAssert.Contains(shown, ToolbarCommand.Cancel);
        CollectionAssert.Contains(shown, ToolbarCommand.OpenEditor);

        // And the tools themselves survive their own list being emptied.
        var tools = ToolbarActions.Tools(AnnotationTool.Arrow, hiddenActions: everything);
        Assert.IsTrue(tools.Any(item => item.Command == ToolbarCommand.PickTool));
        Assert.IsTrue(tools.Any(item => item.Command == ToolbarCommand.Undo));
    }

    [TestMethod]
    public void Normalized_DropsAnActionThisBuildHasNoButtonFor()
    {
        var settings = (CaptureSettings.Default with
        {
            HiddenActions = ["pin", "telepathy", "pin"],
        }).Normalized();

        CollectionAssert.AreEqual(new[] { "pin" }, settings.HiddenActions.ToArray());
    }

    [TestMethod]
    public void HidingTravelsToAnotherMachine()
    {
        // Which buttons someone keeps is about them, not about the machine.
        Assert.IsTrue(SettingsPortability.IsPortable("hiddenActions"));
    }
}
