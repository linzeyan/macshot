using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class CaptureEditStateTests
{
    private static CaptureEditState Adjusted(ImageEffectsOptions effects) =>
        new(effects, BeautifyState.Default);

    [TestMethod]
    public void Write_ThenRead_GivesBackEverySliderSoAReopenedCaptureOpensWhereItLeftOff()
    {
        // The whole point of keeping the adjustment as numbers: the popover has to open
        // holding what it was closed holding. A field lost in the round trip reads as a
        // slider the user never moved, over an image that is visibly adjusted.
        var state = Adjusted(new ImageEffectsOptions(
            ImageEffectPreset.Sepia, Brightness: 0.25, Contrast: 1.4, Saturation: 0.6, Sharpness: 0.3));

        Assert.AreEqual(state, CaptureEditState.Read(CaptureEditState.Write(state)));
    }

    [TestMethod]
    public void Write_ThenRead_GivesBackTheFrameSoAFramedCaptureReopensInsideIt()
    {
        // The frame is not one of the marks and not one of the pixels: archived without it,
        // a capture delivered on a gradient reopens as the bare screenshot, which is not the
        // picture that was approved. Every field has to survive, including the scale — the
        // padding is in points and the capture is in pixels, so the number between them is
        // what decides whether the frame comes back the width it was delivered at.
        var state = new CaptureEditState(
            ImageEffectsOptions.Default,
            new BeautifyState
            {
                Enabled = true,
                Mode = BeautifyMode.Rounded,
                StyleIndex = 12,
                Padding = 32,
                CornerRadius = 18,
                ShadowRadius = 40,
                BackgroundBlur = 6,
                IsWindowSnap = true,
                Scale = 2,
            });

        Assert.AreEqual(state, CaptureEditState.Read(CaptureEditState.Write(state)));
    }

    [TestMethod]
    public void Write_ThenRead_GivesBackTheBackgroundPictureRatherThanWhicheverIsCurrent()
    {
        // There is one custom background on the machine and the user may replace it
        // tomorrow. Carrying the bytes is what makes a capture reopen on the picture it was
        // delivered on rather than on whatever the setting now names — the difference
        // between reopening a capture and reopening a different one.
        var state = new CaptureEditState(
            ImageEffectsOptions.Default,
            new BeautifyState
            {
                Enabled = true,
                StyleIndex = BeautifyOptions.CustomBackgroundStyle,
                Background = [1, 2, 3, 4, 250],
            });

        var read = CaptureEditState.Read(CaptureEditState.Write(state));

        Assert.AreEqual(state, read);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 250 }, read.Beautify.Background);
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
    public void Read_TreatsASidecarWithNoFrameInItAsACaptureThatWasNeverFramed()
    {
        // Every sidecar written before the frame was part of this names only the
        // adjustment, and those entries are still in people's history folders. Absent has
        // to read as "no frame" rather than as a null nobody guarded, because the first
        // thing the editor does with it is ask whether it is on.
        var read = CaptureEditState.Read("{\"effects\":{\"brightness\":0.2}}");

        Assert.AreEqual(BeautifyState.Default, read.Beautify);
        Assert.AreEqual(0.2, read.Effects.Brightness);
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
    public void Read_ClampsAFrameAHandEditedFileAsksFor()
    {
        // The frame is drawn around the capture at whatever it is told, so a padding of
        // 4000 read from a file would reopen the capture as a speck in the middle of a
        // gradient, with no slider anywhere that could bring it back.
        var read = CaptureEditState.Read(
            "{\"beautify\":{\"enabled\":true,\"padding\":4000,\"shadowRadius\":-7,\"scale\":0}}");

        Assert.AreEqual(BeautifyOptions.MaximumPadding, read.Beautify.Padding);
        Assert.AreEqual(0, read.Beautify.ShadowRadius);
        Assert.AreEqual(1, read.Beautify.Scale);
    }

    [TestMethod]
    public void HasPostProcessing_IsFalseForAnAdjustmentThatChangesNothing()
    {
        // It is what decides whether an entry is archived in pieces at all. True for the
        // default would give every capture a raw copy and a sidecar it has no use for, and
        // the raw copy is the larger of the two files.
        Assert.IsFalse(CaptureEditState.None.HasPostProcessing);
        Assert.IsFalse(Adjusted(ImageEffectsOptions.Default).HasPostProcessing);
        Assert.IsTrue(Adjusted(ImageEffectsOptions.Default with { Brightness = 0.2 }).HasPostProcessing);
    }

    [TestMethod]
    public void HasPostProcessing_IsTrueForAFramedCaptureNobodyElseTouched()
    {
        // The commonest framed capture is one with no marks and no adjustment on it. Judged
        // by the adjustment alone, that capture would be archived as the finished image and
        // nothing else — and reopening it would find the frame baked into the pixels, which
        // is the state this whole file exists to prevent.
        Assert.IsTrue(new CaptureEditState(
            ImageEffectsOptions.Default,
            BeautifyState.Default with { Enabled = true }).HasPostProcessing);
    }
}
