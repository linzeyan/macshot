using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class SelectionHandlesTests
{
    private static readonly CaptureRegion Selection = new(100, 100, 200, 160);

    [TestMethod]
    public void For_OffersEightGripsOnASelectionWithRoomForThem()
    {
        CollectionAssert.AreEquivalent(SelectionHandles.All.ToArray(), SelectionHandles.For(Selection).ToArray());
    }

    [TestMethod]
    public void For_DropsTheEdgeGripsWhenTheyWouldCoverTheWholeSelection()
    {
        // A selection this small has no interior left once eight grips are drawn on
        // it, and the interior is what the user drags to move the selection.
        var offered = SelectionHandles.For(new CaptureRegion(0, 0, 20, 20));

        Assert.AreEqual(4, offered.Count);
        Assert.IsTrue(offered.All(SelectionHandles.IsCorner));
    }

    [TestMethod]
    public void HitTest_PrefersTheCornerWhereTheGripsOverlap()
    {
        // On a narrow selection the top-left corner grip and the top edge grip both
        // cover this point. Someone aiming at a corner means the corner.
        var narrow = new CaptureRegion(0, 0, 60, 200);

        var handle = SelectionHandles.HitTest(narrow, new CapturePoint(0, 0));

        Assert.AreEqual(SelectionHandle.TopLeft, handle);
    }

    [TestMethod]
    public void HitTest_FindsNothingInTheMiddleOfTheSelection()
    {
        var handle = SelectionHandles.HitTest(Selection, new CapturePoint(200, 180));

        Assert.AreEqual(SelectionHandle.None, handle);
    }

    [TestMethod]
    public void Resize_MovesOnlyTheDraggedEdge()
    {
        var resized = SelectionHandles.Resize(Selection, SelectionHandle.Right, new CapturePoint(400, 999));

        // A side grip ignores the pointer's other axis, which is the whole reason the
        // edge grips exist alongside the corners.
        Assert.AreEqual(new CaptureRegion(100, 100, 300, 160), resized);
    }

    [TestMethod]
    public void Resize_FlipsRatherThanCollapsingWhenDraggedPastTheOppositeEdge()
    {
        var resized = SelectionHandles.Resize(Selection, SelectionHandle.Left, new CapturePoint(400, 0));

        // Dragging the left edge past the right one gives a selection from 300 to 400,
        // not an empty one pinned at the far side.
        Assert.AreEqual(new CaptureRegion(300, 100, 100, 160), resized);
    }

    [TestMethod]
    public void Resize_WithShiftSquaresAboutTheCornerThatStayedPut()
    {
        var resized = SelectionHandles.Resize(
            Selection,
            SelectionHandle.BottomRight,
            new CapturePoint(400, 200),
            square: true);

        // The drag asks for 300 x 100; the square takes the larger side and grows away
        // from the top-left corner, which is the one not being dragged.
        Assert.AreEqual(new CaptureRegion(100, 100, 300, 300), resized);
    }

    [TestMethod]
    public void Resize_WithShiftGrowsBackTowardsThePointerFromTheFarCorner()
    {
        var resized = SelectionHandles.Resize(
            Selection,
            SelectionHandle.TopLeft,
            new CapturePoint(50, 150),
            square: true);

        // Bottom-right stays at (300, 260), so the square has to end at that corner
        // rather than start from it.
        Assert.AreEqual(300d, resized.Right, 0.001);
        Assert.AreEqual(260d, resized.Bottom, 0.001);
        Assert.AreEqual(resized.Width, resized.Height, 0.001);
    }

    [TestMethod]
    public void Resize_WithNoHandleLeavesTheSelectionAlone()
    {
        Assert.AreEqual(Selection, SelectionHandles.Resize(Selection, SelectionHandle.None, new CapturePoint(0, 0)));
    }

    [TestMethod]
    public void RectangleOf_CentresTheGripOnItsPoint()
    {
        var box = SelectionHandles.RectangleOf(Selection, SelectionHandle.BottomRight);

        Assert.AreEqual(300 - (SelectionHandles.Size / 2), box.X, 0.001);
        Assert.AreEqual(260 - (SelectionHandles.Size / 2), box.Y, 0.001);
        Assert.AreEqual(SelectionHandles.Size, box.Width, 0.001);
    }

    [TestMethod]
    public void ClampTo_KeepsTheSizeWhenPushedAgainstAnEdge()
    {
        var moved = SelectionHandles.Translate(Selection, -500, -500);

        var clamped = SelectionHandles.ClampTo(moved, new CaptureRegion(0, 0, 1920, 1080));

        // Pushing it off the top-left has to park it at the origin at full size, not
        // shrink it against the edge.
        Assert.AreEqual(new CaptureRegion(0, 0, 200, 160), clamped);
    }

    [TestMethod]
    public void ClampTo_ShrinksASelectionLargerThanTheFrame()
    {
        var clamped = SelectionHandles.ClampTo(
            new CaptureRegion(-10, -10, 4000, 3000),
            new CaptureRegion(0, 0, 1920, 1080));

        Assert.AreEqual(new CaptureRegion(0, 0, 1920, 1080), clamped);
    }

    [TestMethod]
    public void ClampTo_ParksAgainstTheBoundsOwnOriginRatherThanTheDesktops()
    {
        // The second display in a left-to-right pair: a selection nudged off its left
        // edge belongs at x=1920, not at x=0, which is a different monitor entirely.
        var secondDisplay = new CaptureRegion(1920, 0, 1920, 1080);

        var clamped = SelectionHandles.ClampTo(new CaptureRegion(1900, -40, 200, 160), secondDisplay);

        Assert.AreEqual(new CaptureRegion(1920, 0, 200, 160), clamped);
    }
}
