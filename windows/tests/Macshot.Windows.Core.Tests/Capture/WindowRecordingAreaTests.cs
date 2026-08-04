using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class WindowRecordingAreaTests
{
    [TestMethod]
    public void Resolve_RecordsTheWindowRatherThanItsInvisibleBorder()
    {
        // The same window WindowFrameCropTests uses: seven pixels of border on three
        // sides. A recording that kept them would carry a transparent band down the
        // side of every frame, which is not what the user pointed at.
        var windowRect = new CaptureRegion(93, 100, 814, 607);
        var visible = new CaptureRegion(100, 100, 800, 600);

        var area = WindowRecordingArea.Resolve(windowRect, visible, 814, 607);

        Assert.AreEqual(new WindowRecordingArea(7, 0, 800, 600), area);
    }

    [TestMethod]
    public void Resolve_RoundsBothSidesDownToWhatTheEncoderWillAccept()
    {
        // H.264 stores colour at half resolution in each direction, so an odd dimension
        // has no whole chroma sample to go in and the encoder refuses the profile — the
        // recording would never start rather than being a pixel narrower.
        var windowRect = new CaptureRegion(0, 0, 801, 603);
        var visible = new CaptureRegion(0, 0, 801, 603);

        var area = WindowRecordingArea.Resolve(windowRect, visible, 801, 603);

        Assert.AreEqual(800, area.Width);
        Assert.AreEqual(602, area.Height);
    }

    [TestMethod]
    public void Resolve_MovesTheCornerRatherThanTheSizeToStayInsideTheFrame()
    {
        // A window whose visible part reaches the last odd column: rounding the size up
        // would read past the buffer, and shrinking it would hand the encoder a sample
        // smaller than the one it was told to expect. The corner is the only one of the
        // three nobody can see move.
        var windowRect = new CaptureRegion(0, 0, 101, 101);
        var visible = new CaptureRegion(100, 100, 1, 1);

        var area = WindowRecordingArea.Resolve(windowRect, visible, 101, 101);

        Assert.AreEqual(2, area.Width);
        Assert.AreEqual(2, area.Height);
        Assert.AreEqual(99, area.Left);
        Assert.AreEqual(99, area.Top);
    }

    [TestMethod]
    public void Fit_TakesThePinnedRectangleOutOfTheFrame()
    {
        var area = new WindowRecordingArea(2, 0, 2, 2);

        // A 4x2 frame whose blue channel counts the columns, so where the copy started
        // is readable rather than merely the right length.
        var frame = new byte[4 * 2 * 4];
        for (var index = 0; index < 8; index++)
        {
            frame[index * 4] = (byte)index;
        }

        var fitted = area.Fit(4, 2, frame, 4, 2);

        Assert.AreEqual(2 * 2 * 4, fitted.Length);
        Assert.AreEqual(2, fitted[0]);
        Assert.AreEqual(3, fitted[4]);
        Assert.AreEqual(6, fitted[8]);
        Assert.AreEqual(7, fitted[12]);
    }

    [TestMethod]
    public void Fit_LeavesWhatAShrunkenWindowVacatedEmptyRatherThanStale()
    {
        // The frame pool is not rebuilt under a running recording, so a window that has
        // been made smaller arrives in the top-left of a buffer still holding the frame
        // before the resize. Reading that remainder would record the moment before the
        // resize for the rest of the file.
        var area = new WindowRecordingArea(0, 0, 4, 2);
        var frame = new byte[4 * 2 * 4];
        Array.Fill(frame, (byte)0xEE);

        var fitted = area.Fit(4, 2, frame, 2, 1);

        // The first two pixels of the first row are the window; everything past them is
        // the band it left behind.
        Assert.AreEqual(0xEE, fitted[0]);
        Assert.AreEqual(0xEE, fitted[4]);
        Assert.AreEqual(0, fitted[8]);
        Assert.AreEqual(0, fitted[16]);
    }

    [TestMethod]
    public void Fit_AnswersTheRightSizeWhenTheWindowIsDeliveringNothing()
    {
        // What a minimized window looks like from here. The sample still has to be the
        // size the encoder was promised, so the recording carries on with a blank picture
        // rather than failing — stopping is what a window being closed means, and that is
        // the capture item's to say.
        var area = new WindowRecordingArea(0, 0, 4, 2);
        var frame = new byte[4 * 2 * 4];
        Array.Fill(frame, (byte)0xEE);

        var fitted = area.Fit(4, 2, frame, 0, 0);

        Assert.AreEqual(4 * 2 * 4, fitted.Length);
        CollectionAssert.AreEqual(new byte[4 * 2 * 4], fitted);
    }

    [TestMethod]
    public void Fit_KeepsTheStartingSizeWhenTheWindowHasGrown()
    {
        // The encoder was told a size once. A window made larger mid-recording is
        // recorded at the size it was started at, with the new pixels outside the
        // rectangle, because the alternative is a file that stops playing.
        var area = new WindowRecordingArea(0, 0, 2, 2);
        var frame = new byte[4 * 4 * 4];
        Array.Fill(frame, (byte)0x11);

        var fitted = area.Fit(4, 4, frame, 4, 4);

        Assert.AreEqual(2 * 2 * 4, fitted.Length);
    }

    [TestMethod]
    public void Fit_RefusesABufferThatIsNotTheFrameItClaimsToBe()
    {
        // The one mistake that would be read as pixels rather than caught: a buffer of
        // the wrong length silently reinterprets every row at a different offset.
        var area = new WindowRecordingArea(0, 0, 2, 2);

        Assert.ThrowsException<ArgumentException>(() => area.Fit(4, 4, new byte[16], 4, 4));
    }
}
