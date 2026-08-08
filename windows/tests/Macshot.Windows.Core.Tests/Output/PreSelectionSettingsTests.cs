using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Output;

/// <summary>
/// What the overlay reads before its first drag: the shape or the size the next region will
/// come out as. It is decided from a settings file that predates the feature, so what an
/// unasked file resolves to is as much of the behaviour as what a chosen preset does.
/// </summary>
[TestClass]
public sealed class PreSelectionSettingsTests
{
    [TestMethod]
    public void ActivePreSelection_FallsBackToTheShapeKeepRatioIsAlreadyHolding()
    {
        // A file written before this existed has no preset in it, and its owner may well
        // have been working in 16 : 9 through the size box for months. Starting them
        // freeform would read as the shipped feature having lost their setting.
        var settings = new CaptureSettings { KeepAspectRatio = true, KeepAspectRatioValue = 16d / 9d };

        Assert.AreEqual(PreSelectionPresetKind.Inherited, settings.PreSelectionKind);
        Assert.AreEqual(16d / 9d, settings.ActivePreSelection.Ratio!.Value, 1e-9);
    }

    [TestMethod]
    public void ActivePreSelection_IsFreeformWhenKeepRatioIsOffAndNothingWasPicked()
    {
        // The ordinary case, and the one every new install starts in: a drag is whatever
        // the pointer makes it. The held value survives the switch being off, so reading
        // it here would silently constrain a drag nobody asked to constrain.
        var settings = new CaptureSettings { KeepAspectRatio = false, KeepAspectRatioValue = 2 };

        Assert.AreEqual(PreSelectionPreset.Freeform, settings.ActivePreSelection);
    }

    [TestMethod]
    public void WithPreSelection_TellsFreeformApartFromNeverHavingBeenAsked()
    {
        // The two are the same numbers and opposite intentions. Someone who picks Freeform
        // after picking 16 : 9 wants the next drag free, not handed back to whatever the
        // size box happens to be holding.
        var settings = new CaptureSettings { KeepAspectRatio = true, KeepAspectRatioValue = 2 }
            .WithPreSelection(PreSelectionPreset.Freeform);

        Assert.AreEqual(PreSelectionPresetKind.Freeform, settings.PreSelectionKind);
        Assert.AreEqual(PreSelectionPreset.Freeform, settings.ActivePreSelection);
    }

    [TestMethod]
    public void WithPreSelection_RoundTripsAShapeAndASizeThroughTheThreeStoredNumbers()
    {
        // The overlay reads this back on the next hotkey press. A kind that disagreed with
        // its numbers would start the drag as something the menu never offered.
        var ratio = new CaptureSettings().WithPreSelection(PreSelectionPreset.OfRatio(4d / 3d));
        var size = new CaptureSettings().WithPreSelection(PreSelectionPreset.OfSize(1280, 720));

        Assert.AreEqual(PreSelectionPresetKind.Ratio, ratio.PreSelectionKind);
        Assert.AreEqual(4d / 3d, ratio.ActivePreSelection.Ratio!.Value, 1e-9);
        Assert.IsFalse(ratio.ActivePreSelection.IsExact);

        Assert.AreEqual(PreSelectionPresetKind.Resolution, size.PreSelectionKind);
        Assert.AreEqual(PreSelectionPreset.OfSize(1280, 720), size.ActivePreSelection);
    }

    [TestMethod]
    public void ActivePreSelection_RefusesStoredNumbersThatCannotShapeADrag()
    {
        // Hand-edited files reach this, and so does an upgrade that changes what a field
        // means. A ratio of zero would collapse the marquee to a line, and a size with no
        // area would deliver a region nobody can see.
        var ratio = new CaptureSettings
        {
            PreSelectionKind = PreSelectionPresetKind.Ratio,
            PreSelectionAspect = 0,
        };

        var size = new CaptureSettings
        {
            PreSelectionKind = PreSelectionPresetKind.Resolution,
            PreSelectionWidth = 1920,
            PreSelectionHeight = -1,
        };

        Assert.AreEqual(PreSelectionPreset.Freeform, ratio.ActivePreSelection);
        Assert.AreEqual(PreSelectionPreset.Freeform, size.ActivePreSelection);
    }

    [TestMethod]
    public void Normalized_HandsAnUnknownKindBackToKeepRatioRatherThanGuessing()
    {
        // Inherited is the only fallback that loses nothing: it asks the question the file
        // was answering before this key existed, instead of picking one of three shapes
        // the user never chose.
        var settings = new CaptureSettings
        {
            PreSelectionKind = (PreSelectionPresetKind)77,
            KeepAspectRatio = true,
            KeepAspectRatioValue = 1,
        }.Normalized();

        Assert.AreEqual(PreSelectionPresetKind.Inherited, settings.PreSelectionKind);
        Assert.AreEqual(1, settings.ActivePreSelection.Ratio!.Value, 1e-9);
    }
}
