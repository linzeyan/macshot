using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class ScrollSpeedsTests
{
    [TestMethod]
    public void NotchesPerStep_LeavesTheTestedStepWhereItWas()
    {
        // Fast is the default and is what the driver has always sent. A change here is a
        // change to every scroll capture taken without touching the setting.
        Assert.AreEqual(3, ScrollSpeeds.NotchesPerStep(ScrollSpeed.Fast));
        Assert.AreEqual(ScrollSpeed.Fast, CaptureSettings.Default.ScrollSpeed);
    }

    [TestMethod]
    public void NotchesPerStep_RisesWithTheSpeedAndNeverReachesNothing()
    {
        // Monotonic, because the labels promise it: picking "Fast" over "Medium" and
        // getting a shorter step would be the setting doing the opposite of what it says.
        var steps = Enum.GetValues<ScrollSpeed>()
            .OrderBy(speed => (int)speed)
            .Select(ScrollSpeeds.NotchesPerStep)
            .ToArray();

        CollectionAssert.AreEqual(steps.OrderBy(step => step).ToArray(), steps);

        // Nought notches is a capture that scrolls nowhere and never finishes, and the
        // driver refuses it outright.
        Assert.IsTrue(steps.All(step => step > 0));
    }

    [TestMethod]
    public void ScrollMaxHeight_TurnsANegativeLimitIntoNoLimit()
    {
        // A hand-edited file. A negative limit would stop every scroll capture before its
        // first frame, where nought is the value the person writing it was reaching for.
        var settings = (CaptureSettings.Default with { ScrollMaxHeight = -5000 }).Normalized();

        Assert.AreEqual(0, settings.ScrollMaxHeight);
    }

    [TestMethod]
    public void ScrollMaxHeight_KeepsALimitTheUserSet()
    {
        var settings = (CaptureSettings.Default with { ScrollMaxHeight = 15000 }).Normalized();

        Assert.AreEqual(15000, settings.ScrollMaxHeight);
    }
}
