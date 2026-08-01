using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

/// <summary>
/// The size of the picture made from copied text.
/// </summary>
/// <remarks>
/// Pinning text means pinning a picture of it, so these numbers are the difference
/// between a readable note and a window bigger than the screen it is pinned to.
/// </remarks>
[TestClass]
public sealed class TextPinLayoutTests
{
    [TestMethod]
    public void TextWrapsWellShortOfTheWidthOfAWideDisplay()
    {
        // 72% of 3440 is 2476, which is a line nobody can read the end of.
        Assert.AreEqual(980, TextPinLayout.MaxContentWidth(3440));
    }

    [TestMethod]
    public void ANarrowDisplayStillGetsAWidthWorthWrappingAt()
    {
        // 72% of 400 is 288, which would break most sentences twice.
        Assert.AreEqual(320, TextPinLayout.MaxContentWidth(400));
    }

    [TestMethod]
    public void ShortTextIsItsOwnSizePlusThePadding()
    {
        var (width, height) = TextPinLayout.Fit(300, 100, 1920, 1080);

        Assert.AreEqual(348, width, "24 either side");
        Assert.AreEqual(144, height, "22 above and below");
    }

    /// <summary>
    /// The whole point of the height limit: a paste of a thousand lines is a pin the
    /// user cannot see past, so the top of it is what they get.
    /// </summary>
    [TestMethod]
    public void ALongPasteIsCutOffRatherThanShrunk()
    {
        var (width, height) = TextPinLayout.Fit(600, 40_000, 1920, 1080);

        Assert.AreEqual(648, width, "cutting the height leaves the width alone");
        Assert.AreEqual((int)(1080 * 0.82), height);
    }

    /// <summary>
    /// Both limits at once. A pin that passes the area limit is scaled after the height
    /// has already been cut, so the check has to be against what the cut left.
    /// </summary>
    [TestMethod]
    public void SomethingEnormousComesBackUnderTheAreaLimit()
    {
        var (width, height) = TextPinLayout.Fit(200_000, 200_000, 40_000, 40_000);

        Assert.IsTrue(
            (double)width * height <= TextPinLayout.MaxArea,
            $"{width}x{height} is past the limit");
        Assert.IsTrue(width >= 320 && height >= 180, "and is still big enough to read");
    }
}
