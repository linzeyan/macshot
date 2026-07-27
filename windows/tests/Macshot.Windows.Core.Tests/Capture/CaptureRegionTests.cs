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
}
