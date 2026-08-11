using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class CaptureEditStateTests
{
    [TestMethod]
    public void Write_ThenRead_GivesBackEverySliderSoAReopenedCaptureOpensWhereItLeftOff()
    {
        // The whole point of keeping the adjustment as numbers: the popover has to open
        // holding what it was closed holding. A field lost in the round trip reads as a
        // slider the user never moved, over an image that is visibly adjusted.
        var state = new CaptureEditState(new ImageEffectsOptions(
            ImageEffectPreset.Sepia, Brightness: 0.25, Contrast: 1.4, Saturation: 0.6, Sharpness: 0.3));

        Assert.AreEqual(state, CaptureEditState.Read(CaptureEditState.Write(state)));
    }

    [TestMethod]
    public void Read_AnswersNoneForAnythingItCannotUnderstand()
    {
        // The folder is one the user can edit, delete from and copy into. Every way this
        // file can be wrong has to end as "no adjustment" rather than as an exception over
        // a capture they only wanted to look at.
        Assert.AreEqual(CaptureEditState.None, CaptureEditState.Read(null));
        Assert.AreEqual(CaptureEditState.None, CaptureEditState.Read("   "));
        Assert.AreEqual(CaptureEditState.None, CaptureEditState.Read("{"));
        Assert.AreEqual(CaptureEditState.None, CaptureEditState.Read("{\"effects\":null}"));
    }

    [TestMethod]
    public void Read_ClampsWhatAHandEditedFileAsksFor()
    {
        // A contrast of 40 typed into the sidecar would drive the reopened capture to two
        // colours, with no slider position that could explain it or undo it.
        var read = CaptureEditState.Read("{\"effects\":{\"contrast\":40,\"saturation\":-5}}");

        Assert.AreEqual(2, read.Effects.Contrast);
        Assert.AreEqual(0, read.Effects.Saturation);
    }

    [TestMethod]
    public void HasPostProcessing_IsFalseForAnAdjustmentThatChangesNothing()
    {
        // It is what decides whether an entry is archived in pieces at all. True for the
        // default would give every capture a raw copy and a sidecar it has no use for, and
        // the raw copy is the larger of the two files.
        Assert.IsFalse(CaptureEditState.None.HasPostProcessing);
        Assert.IsFalse(new CaptureEditState(ImageEffectsOptions.Default).HasPostProcessing);
        Assert.IsTrue(new CaptureEditState(
            ImageEffectsOptions.Default with { Brightness = 0.2 }).HasPostProcessing);
    }
}
