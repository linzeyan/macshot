using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class CaptureMenuItemsTests
{
    [TestMethod]
    public void Resolve_GivesMacshotsOrderWhenNothingIsStored()
    {
        // A settings file written before this setting existed must produce the menu the
        // app has always had, not an empty one.
        CollectionAssert.AreEqual(
            CaptureMenuItems.DefaultOrder.ToArray(),
            CaptureMenuItems.Resolve(null).ToArray());
    }

    [TestMethod]
    public void Resolve_KeepsWhatWasStoredAndAddsBackWhatWasMissing()
    {
        var order = CaptureMenuItems.Resolve(["ScrollCapture", "CaptureArea"]);

        Assert.AreEqual(CaptureMenuItem.ScrollCapture, order[0]);
        Assert.AreEqual(CaptureMenuItem.CaptureArea, order[1]);

        // Every command exactly once, or the menu would be missing a way to start a
        // capture that nothing in the interface could put back.
        Assert.AreEqual(CaptureMenuItems.DefaultOrder.Count, order.Count);
        Assert.AreEqual(order.Count, order.Distinct().Count());
    }

    [TestMethod]
    public void Resolve_IgnoresRepeatsAndNamesItDoesNotKnow()
    {
        // A hand-edited file, or one from a version with a command this build does not
        // have. Neither may produce a menu with a command twice or a blank line in it.
        var order = CaptureMenuItems.Resolve(["QuickCapture", "QuickCapture", "TeleportCapture"]);

        Assert.AreEqual(CaptureMenuItem.QuickCapture, order[0]);
        Assert.AreEqual(CaptureMenuItems.DefaultOrder.Count, order.Count);
        Assert.AreEqual(order.Count, order.Distinct().Count());
    }

    [TestMethod]
    public void Store_WritesTheWholeMenuRatherThanThePartThatMoved()
    {
        var stored = CaptureMenuItems.Store([CaptureMenuItem.ScrollCapture]);

        Assert.AreEqual(CaptureMenuItems.DefaultOrder.Count, stored.Count);
        Assert.AreEqual(nameof(CaptureMenuItem.ScrollCapture), stored[0]);
    }

    [TestMethod]
    public void Move_SwapsWithTheNeighbour()
    {
        var moved = CaptureMenuItems.Move(CaptureMenuItems.DefaultOrder, 0, 1);

        Assert.AreEqual(CaptureMenuItems.DefaultOrder[1], moved[0]);
        Assert.AreEqual(CaptureMenuItems.DefaultOrder[0], moved[1]);
    }

    [TestMethod]
    public void Move_LeavesTheEndsAlone()
    {
        // The topmost item's Up button does nothing rather than wrapping it round to the
        // bottom, which is what the button being greyed out promises.
        Assert.AreSame(
            CaptureMenuItems.DefaultOrder,
            CaptureMenuItems.Move(CaptureMenuItems.DefaultOrder, 0, -1));

        Assert.AreSame(
            CaptureMenuItems.DefaultOrder,
            CaptureMenuItems.Move(CaptureMenuItems.DefaultOrder, CaptureMenuItems.DefaultOrder.Count - 1, 1));
    }
}
