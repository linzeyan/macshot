using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Output;

[TestClass]
public sealed class HistoryPanelLayoutTests
{
    /// <summary>A 1080p screen with a taskbar along the bottom.</summary>
    private static readonly CaptureRegion Laptop = new(0, 0, 1920, 1032);

    [TestMethod]
    public void ThePanelHangsFromTheTopOfTheWorkArea()
    {
        var (_, y, _, height) = HistoryPanelLayout.For(Laptop, 1);

        Assert.AreEqual(Laptop.Y, y);
        Assert.AreEqual((int)HistoryPanelLayout.Height, height);
    }

    /// <summary>
    /// Narrow enough that the cap does not apply, which is the only case where the
    /// clearance is the one macshot asked for rather than whatever centring leaves.
    /// </summary>
    [TestMethod]
    public void ItKeepsTheScreenEdgeClearOnASmallDisplay()
    {
        var small = new CaptureRegion(0, 0, 1000, 700);

        var (x, _, width, _) = HistoryPanelLayout.For(small, 1);

        Assert.AreEqual((int)HistoryPanelLayout.ScreenInset, x);
        Assert.AreEqual(1000 - (2 * (int)HistoryPanelLayout.ScreenInset), width);
    }

    [TestMethod]
    public void ItIsCentredOnTheWorkArea()
    {
        var (x, _, width, _) = HistoryPanelLayout.For(Laptop, 1);

        Assert.AreEqual(x - Laptop.X, (int)Laptop.Width - width - (x - (int)Laptop.X));
    }

    /// <summary>
    /// The point of the cap: on an ultrawide a full-width strip is mostly empty panel,
    /// and the cards would be a metre apart from the first to the last.
    /// </summary>
    [TestMethod]
    public void OnAVeryWideScreenItStopsGrowingAndStaysCentred()
    {
        var ultrawide = new CaptureRegion(0, 0, 5120, 1400);

        var (x, _, width, _) = HistoryPanelLayout.For(ultrawide, 1);

        Assert.AreEqual((int)HistoryPanelLayout.MaxWidth, width);
        Assert.AreEqual((5120 - (int)HistoryPanelLayout.MaxWidth) / 2, x);
    }

    /// <summary>
    /// A second monitor's work area does not start at the origin, and a panel placed as
    /// though it did lands on the first monitor.
    /// </summary>
    [TestMethod]
    public void ItLandsOnTheMonitorItWasGiven()
    {
        var second = new CaptureRegion(1920, 200, 1600, 1000);

        var (x, y, width, _) = HistoryPanelLayout.For(second, 1);

        Assert.AreEqual(200, y);
        Assert.AreEqual(1920 + ((1600 - width) / 2), x);
    }

    [TestMethod]
    public void EverythingScalesWithTheDisplay()
    {
        var retina = new CaptureRegion(0, 0, 3000, 2000);

        var (_, _, width, height) = HistoryPanelLayout.For(retina, 2);

        Assert.AreEqual((int)(HistoryPanelLayout.MaxWidth * 2), width);
        Assert.AreEqual((int)(HistoryPanelLayout.Height * 2), height);
    }

    /// <summary>
    /// The card row has to agree with the panel: the tab bar, the gap under it and a card
    /// are exactly the panel's height, or the cards sit in a band of empty grey.
    /// </summary>
    [TestMethod]
    public void TheTabBarAndOneRowOfCardsFillThePanel()
    {
        var used = HistoryPanelLayout.TabBarHeight
            + HistoryPanelLayout.CardTopGap
            + HistoryPanelLayout.CardHeight;

        Assert.IsTrue(used <= HistoryPanelLayout.Height, $"{used} > {HistoryPanelLayout.Height}");
        Assert.IsTrue(HistoryPanelLayout.Height - used < HistoryPanelLayout.CardHeight);
    }
}
