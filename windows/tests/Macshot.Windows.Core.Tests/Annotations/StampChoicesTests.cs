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
    /// The quick set opens the picker's list, in the same order.
    /// </summary>
    /// <remarks>
    /// Not merely a containment: the picker is a grid, and a user who learned where the
    /// tick is on the row would have to find it again somewhere else in the grid if the
    /// two orders drifted apart. Anything added later goes after them.
    /// </remarks>
    [TestMethod]
    public void ThePicker_StartsWithTheRowInTheRowsOrder()
    {
        CollectionAssert.AreEqual(
            StampChoices.Quick.ToArray(),
            StampChoices.All.Take(StampChoices.Quick.Count).ToArray());
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
