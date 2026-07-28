using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class CaptureRegionTests
{
    [TestMethod]
    public void FromPoints_NormalizesReverseDragDirection()
    {
        var region = CaptureRegion.FromPoints(30, 50, 10, 20);

        Assert.AreEqual(new CaptureRegion(10, 20, 20, 30), region);
    }

    [TestMethod]
    public void IsEmpty_RejectsZeroSizedSelection()
    {
        Assert.IsTrue(new CaptureRegion(0, 0, 0, 10).IsEmpty);
    }

    [TestMethod]
    public void Intersect_KeepsOnlyTheOverlap()
    {
        var overlap = new CaptureRegion(0, 0, 100, 100).Intersect(new CaptureRegion(60, 40, 100, 100));

        Assert.AreEqual(new CaptureRegion(60, 40, 40, 60), overlap);
    }

    [TestMethod]
    public void Intersect_ReturnsNothingForRegionsThatDoNotMeet()
    {
        // The failure this guards against is an absolute-width intersection, which
        // would answer with a plausible rectangle sitting in the gap between them.
        var apart = new CaptureRegion(0, 0, 10, 10).Intersect(new CaptureRegion(100, 100, 10, 10));

        Assert.IsTrue(apart.IsEmpty);
    }
}
