using Macshot.Windows.Core.Annotations;

namespace Macshot.Windows.Core.Tests.Annotations;

[TestClass]
public sealed class StampChoicesTests
{
    /// <summary>
    /// Everything reachable in one click is also in the picker.
    /// </summary>
    /// <remarks>
    /// The row and the picker are two views of one set, and the failure this catches is
    /// quiet: an emoji added to the row alone still stamps, so nothing looks broken until
    /// the picker is opened afterwards and shows nothing chosen — the user is then looking
    /// at a picker that disagrees with the mark they just placed.
    /// </remarks>
    [TestMethod]
    public void EveryQuickStamp_IsAlsoInThePicker()
    {
        foreach (var emoji in StampChoices.Quick)
        {
            Assert.IsTrue(StampChoices.All.Contains(emoji), $"{emoji} is on the row but not in the picker");
        }

        Assert.IsTrue(StampChoices.All.Contains(StampChoices.Default));
        Assert.IsTrue(StampChoices.Quick.Contains(StampChoices.Default), "the default must be reachable in one click");
    }

    /// <summary>
    /// A tab shows the first emoji of the group behind it.
    /// </summary>
    /// <remarks>
    /// The picker has no words on it — the tab is a picture of what is inside, which is how
    /// macshot labels them and what lets the picker need no translating. That only works
    /// while the picture is actually taken from the group: a tab drawn with an emoji the
    /// group does not hold is a label that lies, and nothing else in the app would catch
    /// it. The rule is "the first" rather than "any of them" because the first is what the
    /// group leads with, so the tab and the top-left cell agree.
    /// </remarks>
    [TestMethod]
    public void EachTab_ShowsTheFirstEmojiOfItsOwnGroup()
    {
        foreach (var category in StampChoices.Categories)
        {
            Assert.AreEqual(
                category.Emoji[0],
                category.Tab,
                "a tab has to be a picture of what is behind it");
        }
    }

    /// <summary>
    /// Every group has something in it.
    /// </summary>
    /// <remarks>
    /// An empty group is a tab that opens onto nothing, which reads as the picker having
    /// failed to load rather than as a group nobody filled in.
    /// </remarks>
    [TestMethod]
    public void NoGroupIsEmpty()
    {
        Assert.IsTrue(StampChoices.Categories.Count > 0);

        foreach (var category in StampChoices.Categories)
        {
            Assert.IsTrue(category.Emoji.Count > 0, $"the {category.Tab} group is empty");
        }
    }

    /// <summary>
    /// Nothing is offered twice.
    /// </summary>
    /// <remarks>
    /// A duplicate in the picker is a second cell that selects the first one, which reads
    /// as a click that did not take.
    /// </remarks>
    [TestMethod]
    public void NoStampIsOfferedTwice()
    {
        CollectionAssert.AreEqual(
            StampChoices.All.ToArray(),
            StampChoices.All.Distinct(StringComparer.Ordinal).ToArray());
    }
}
