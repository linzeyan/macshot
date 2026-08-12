using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

/// <summary>
/// The frame as numbers beside a capture rather than as the background it was mounted on,
/// which is what makes a framed capture something that can be reopened and edited.
/// </summary>
[TestClass]
public sealed class BeautifyStateTests
{
    [TestMethod]
    public void Of_ThenToOptions_GivesBackTheFrameThatWasDrawn()
    {
        // The round trip the whole feature rests on: what the overlay rendered has to be
        // what the editor renders when the capture is opened again. A field dropped between
        // the two is a reopened capture that is visibly not the one that was delivered, and
        // nothing on screen would say which of the two is wrong.
        var drawn = new BeautifyOptions(
            StyleIndex: 7,
            Padding: 24,
            CornerRadius: 12,
            ShadowRadius: 30,
            ShadowOpacity: BeautifyOptions.Default.ShadowOpacity,
            Enabled: true,
            Mode: BeautifyMode.Rounded,
            BackgroundBlur: 8);

        Assert.AreEqual(drawn, BeautifyState.Of(drawn, isWindowSnap: false, scale: 2).ToOptions());
    }

    [TestMethod]
    public void Of_KeepsThePictureOnlyWhileItIsTheBackground()
    {
        // macshot's own guard (CaptureEditState.swift:68). The bytes are the file the user
        // chose, and a megabyte of it in the sidecar of a capture that was delivered on a
        // gradient is a megabyte nothing will ever read back.
        var picture = new byte[] { 9, 9, 9 };

        Assert.IsNull(BeautifyState
            .Of(BeautifyOptions.Default with { StyleIndex = 4 }, false, 1, picture)
            .Background);

        // The sentinel survives only where there is a picture to honour it, so a backdrop
        // has to be handed in for the style to stay custom on the way through.
        var custom = BeautifyOptions.Default with
        {
            StyleIndex = BeautifyOptions.CustomBackgroundStyle,
            Backdrop = new BeautifyBackdrop(1, 1, new byte[4]),
        };

        CollectionAssert.AreEqual(picture, BeautifyState.Of(custom, false, 1, picture).Background);
    }

    [TestMethod]
    public void ToOptions_FallsBackToAGradientWhenThePictureCannotBeDecoded()
    {
        // A file the decoder now refuses, or one the user replaced with something that is
        // not an image. Drawing the first gradient is a frame they can see and change; a
        // style index of -1 with nothing behind it is a background that indexes nothing.
        var state = new BeautifyState
        {
            Enabled = true,
            StyleIndex = BeautifyOptions.CustomBackgroundStyle,
            Background = [1, 2, 3],
        };

        Assert.AreEqual(0, state.ToOptions(backdrop: null).StyleIndex);
    }

    [TestMethod]
    public void Normalized_DropsAStyleSentinelWithNoPictureBehindIt()
    {
        // Read from a sidecar whose picture was not written, or written by a hand. The
        // clamp has to happen where the bytes are, because the renderer is handed a decoded
        // backdrop and by then the reason the style is -1 has been lost.
        var state = new BeautifyState { StyleIndex = BeautifyOptions.CustomBackgroundStyle };

        Assert.AreEqual(0, state.Normalized().StyleIndex);
    }

    [TestMethod]
    public void Normalized_HoldsTheScaleToWhatTheRendererWouldHaveUsed()
    {
        // The scale is a multiplier on every measurement in the frame, so a zero from a
        // hand-edited file would collapse it to nothing and a hundred would ask for a
        // gradient the size of a wall. Held to the renderer's own band rather than to a
        // second opinion about it.
        Assert.AreEqual(1, (BeautifyState.Default with { Scale = 0 }).Normalized().Scale);
        Assert.AreEqual(8, (BeautifyState.Default with { Scale = 99 }).Normalized().Scale);
        Assert.AreEqual(
            1,
            (BeautifyState.Default with { Scale = double.NaN }).Normalized().Scale);
    }

    [TestMethod]
    public void Equals_ComparesThePictureByItsBytesRatherThanByReference()
    {
        // This value is the editor's answer to "has anything changed since this was last
        // written down". A record compares an array by reference, so a picture read from
        // disk twice would count as an edit and the window would offer to save a capture
        // nobody had touched — the nagging prompt macshot removed for the same reason.
        var one = new BeautifyState
        {
            Enabled = true,
            StyleIndex = BeautifyOptions.CustomBackgroundStyle,
            Background = [4, 5, 6],
        };

        Assert.AreEqual(one, one with { Background = [4, 5, 6] });
        Assert.AreNotEqual(one, one with { Background = [4, 5, 7] });
        Assert.AreNotEqual(one, one with { Background = null });
    }

    [TestMethod]
    public void IsWindowSnap_SurvivesTheRoundTripBecauseThePixelsCannotBeAsked()
    {
        // A snapped window arrives with its own title bar and its own rounded corners in
        // its pixels. Reopened without knowing that, re-arming the frame would draw a
        // second title bar above the first and round corners that are already round.
        var snapped = BeautifyState.Of(
            BeautifyOptions.Default with { Enabled = true },
            isWindowSnap: true,
            scale: 1);

        Assert.IsTrue(snapped.IsWindowSnap);
    }

    [TestMethod]
    public void IsIdentity_IsTheSwitchAloneBecauseEveryOtherFieldIsInertWhileItIsOff()
    {
        // The padding a capture was never framed with is not an edit. Judged on the numbers
        // instead, every capture taken after somebody once moved the padding slider would
        // be archived as though it were carrying a frame.
        Assert.IsTrue(BeautifyState.Default.IsIdentity);
        Assert.IsTrue((BeautifyState.Default with { Padding = 12 }).IsIdentity);
        Assert.IsFalse((BeautifyState.Default with { Enabled = true }).IsIdentity);
    }
}
