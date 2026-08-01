using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Output;

[TestClass]
public sealed class RememberedSelectionTests
{
    private const string Display = @"\\.\DISPLAY1";

    private static CaptureSettings Remembering(CaptureRegion selection, string display = Display) =>
        CaptureSettings.Default with
        {
            RememberLastSelection = true,
            LastSelection = selection,
            LastSelectionDisplay = display,
        };

    [TestMethod]
    public void RememberedSelectionFor_OffersTheRegionBackOnTheDisplayItCameFrom()
    {
        var settings = Remembering(new CaptureRegion(100, 100, 400, 300));

        Assert.AreEqual(new CaptureRegion(100, 100, 400, 300), settings.RememberedSelectionFor(Display, 1920, 1080));
    }

    [TestMethod]
    public void RememberedSelectionFor_OffersNothingWhenTheSettingIsOff()
    {
        var settings = Remembering(new CaptureRegion(100, 100, 400, 300)) with { RememberLastSelection = false };

        Assert.IsNull(settings.RememberedSelectionFor(Display, 1920, 1080));
    }

    [TestMethod]
    public void RememberedSelectionFor_OffersNothingOnADifferentDisplay()
    {
        var settings = Remembering(new CaptureRegion(100, 100, 400, 300));

        // The same rectangle on another monitor is not the region the user drew.
        Assert.IsNull(settings.RememberedSelectionFor(@"\\.\DISPLAY2", 1920, 1080));
    }

    [TestMethod]
    public void RememberedSelectionFor_RefusesARegionThatNoLongerFits()
    {
        var settings = Remembering(new CaptureRegion(100, 100, 400, 300));

        // The display was replaced by a smaller one. Squashing the rectangle to fit
        // would offer back a selection nobody chose.
        Assert.IsNull(settings.RememberedSelectionFor(Display, 320, 240));
    }

    [TestMethod]
    public void RememberedSelectionFor_RefusesTheResidueOfAClick()
    {
        var settings = Remembering(new CaptureRegion(100, 100, 2, 2));

        Assert.IsNull(settings.RememberedSelectionFor(Display, 1920, 1080));
    }

    [TestMethod]
    public void WithLastSelection_RecordsTheRegionAndItsDisplay()
    {
        var stored = CaptureSettings.Default.WithLastSelection(new CaptureRegion(10, 20, 300, 200), Display);

        Assert.AreEqual(new CaptureRegion(10, 20, 300, 200), stored.LastSelection);
        Assert.AreEqual(Display, stored.LastSelectionDisplay);
    }

    [TestMethod]
    public void WithLastSelection_KeepsTheOldOneRatherThanRecordingAClick()
    {
        var stored = CaptureSettings.Default
            .WithLastSelection(new CaptureRegion(10, 20, 300, 200), Display)
            .WithLastSelection(new CaptureRegion(0, 0, 1, 1), Display);

        // A click that took the whole screen must not overwrite the region the user
        // last actually dragged out.
        Assert.AreEqual(new CaptureRegion(10, 20, 300, 200), stored.LastSelection);
    }

    [TestMethod]
    public void Normalized_DropsASelectionWithNoDisplayToBelongTo()
    {
        var normalized = (CaptureSettings.Default with
        {
            LastSelection = new CaptureRegion(1, 2, 3, 4),
            LastSelectionDisplay = null,
        }).Normalized();

        Assert.IsNull(normalized.LastSelection);
        Assert.IsNull(normalized.LastSelectionDisplay);
    }

    [TestMethod]
    public void Normalized_DropsADisplayWithNoSelectionToPlace()
    {
        var normalized = (CaptureSettings.Default with { LastSelectionDisplay = Display }).Normalized();

        Assert.IsNull(normalized.LastSelectionDisplay);
    }

    [TestMethod]
    public void Normalized_DropsASelectionThatCannotBeDrawn()
    {
        var normalized = Remembering(new CaptureRegion(0, 0, double.NaN, 10)).Normalized();

        Assert.IsNull(normalized.LastSelection);
    }

    [TestMethod]
    public void Normalized_PullsTheDelayAndHistoryIntoRange()
    {
        var normalized = (CaptureSettings.Default with
        {
            CaptureDelayChosen = true,
            DelaySeconds = -4,
            HistorySize = 9999,
        }).Normalized();

        // Zero, which is macshot's None: a delay is off until someone turns it on, and
        // the menu ticks None to say so.
        Assert.AreEqual(CaptureSettings.MinDelaySeconds, normalized.DelaySeconds);
        Assert.AreEqual(0, CaptureSettings.MinDelaySeconds);
        Assert.AreEqual(CaptureSettings.MaxHistorySize, normalized.HistorySize);
    }

    [TestMethod]
    public void Normalized_TakesBackADelayNobodyAskedFor()
    {
        // What every settings file written before the flag existed looks like: five
        // seconds, put there by a default that could not be turned off. It goes.
        var inherited = (CaptureSettings.Default with { DelaySeconds = 5 }).Normalized();

        Assert.AreEqual(0, inherited.DelaySeconds);
        Assert.IsTrue(inherited.CaptureDelayChosen, "and it is only taken back once");

        // The same five, chosen from the menu once the flag is set, stays. Otherwise the
        // migration would be a setting nobody could ever hold.
        var chosen = (inherited with { DelaySeconds = 5 }).Normalized();

        Assert.AreEqual(5, chosen.DelaySeconds);
    }

    [TestMethod]
    public void Normalized_RepairsBeautifyValuesAHandEditedFileCouldHold()
    {
        var normalized = (CaptureSettings.Default with
        {
            BeautifyStyleIndex = 9999,
            BeautifyPadding = double.NaN,
            BeautifyShadowRadius = 12,
        }).Normalized();

        Assert.AreEqual(BeautifyRenderer.Styles.Count - 1, normalized.BeautifyStyleIndex);
        Assert.AreEqual(BeautifyOptions.Default.Padding, normalized.BeautifyPadding);
        Assert.AreEqual(0.25, normalized.BeautifyShadowRadius);
    }

    [TestMethod]
    public void ToBeautifyOptions_CarriesTheStoredFrameThrough()
    {
        var options = (CaptureSettings.Default with
        {
            BeautifyStyleIndex = 3,
            BeautifyPadding = 0.2,
        }).ToBeautifyOptions();

        Assert.AreEqual(3, options.StyleIndex);
        Assert.AreEqual(0.2, options.Padding);
    }

    [TestMethod]
    public void DefaultSettings_LeaveTheRememberedSelectionOff()
    {
        // A selection that reappears where the last one was is a surprise until you
        // know the setting exists.
        Assert.IsFalse(CaptureSettings.Default.RememberLastSelection);
        Assert.IsNull(CaptureSettings.Default.LastSelection);
    }
}
