using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class ImageUndoBudgetTests
{
    /// <summary>
    /// A 4K screenshot, which is the size the whole cap exists for: thirty-three
    /// megabytes a step.
    /// </summary>
    private const long Screenshot4K = 3840L * 2160 * 4;

    /// <summary>
    /// The ordinary case has to be indistinguishable from macOS, which caps nothing. A
    /// history that fits is a history that is kept entire — otherwise the cap would be
    /// taking undo steps away from people whose captures were never the problem.
    /// </summary>
    [TestMethod]
    public void OldestToDrop_KeepsEveryStepThatFits()
    {
        Assert.AreEqual(0, ImageUndoBudget.OldestToDrop([100, 100, 100], budget: 1000));
    }

    /// <summary>
    /// The oldest end goes first. Undo is walked backwards, so the steps nearest the
    /// present are the ones anybody reaches — dropping from the other end is what makes
    /// the cap invisible until it is hit.
    /// </summary>
    [TestMethod]
    public void OldestToDrop_TakesFromTheEndNobodyIsWalkingTowards()
    {
        Assert.AreEqual(2, ImageUndoBudget.OldestToDrop([100, 100, 100, 100], budget: 250));
    }

    /// <summary>
    /// An image bigger than the whole budget still leaves one step. Dropping it would
    /// make the operation that just ran unundoable, which is a worse thing to have done
    /// to the user than holding the memory it costs.
    /// </summary>
    [TestMethod]
    public void OldestToDrop_NeverLeavesTheLastOperationUnundoable()
    {
        Assert.AreEqual(0, ImageUndoBudget.OldestToDrop([Screenshot4K], budget: 1024));
        Assert.AreEqual(2, ImageUndoBudget.OldestToDrop([10, 10, Screenshot4K], budget: 1024));
    }

    /// <summary>
    /// What the number is actually for: editing a 4K capture has to stay under a
    /// gigabyte however long the session runs. The count is generous — more image
    /// operations than one sitting performs — but it is finite, which unbounded was not.
    /// </summary>
    [TestMethod]
    public void OldestToDrop_HoldsAFourKSessionToAWorkableNumberOfSteps()
    {
        var history = new List<long>();

        for (var step = 0; step < 200; step++)
        {
            history.Add(Screenshot4K);
            history.RemoveRange(0, ImageUndoBudget.OldestToDrop(history));
        }

        Assert.IsTrue(history.Count is >= 8 and <= 20, $"kept {history.Count} steps");
        Assert.IsTrue(history.Sum() <= ImageUndoBudget.Bytes, $"kept {history.Sum()} bytes");
    }

    /// <summary>
    /// A small capture is not what the budget is aimed at, and must not feel aimed at:
    /// a 500x400 selection has to survive far more editing than anyone does before a
    /// single step is let go of.
    /// </summary>
    [TestMethod]
    public void OldestToDrop_LeavesAnOrdinaryCaptureAlone()
    {
        var small = 500L * 400 * 4;
        var history = Enumerable.Repeat(small, 100).ToList();

        Assert.AreEqual(0, ImageUndoBudget.OldestToDrop(history));
    }
}
