using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

/// <summary>
/// The picture a capture can be framed on, which is the whole reason the options row has
/// a Blur slider: macshot shows it only while a custom background is in use.
/// </summary>
[TestClass]
public sealed class BeautifyBackdropTests
{
    private static byte[] Solid(int width, int height, byte blue, byte green, byte red)
    {
        var pixels = new byte[width * height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = blue;
            pixels[offset + 1] = green;
            pixels[offset + 2] = red;
            pixels[offset + 3] = byte.MaxValue;
        }

        return pixels;
    }

    /// <summary>
    /// The sentinel only survives while the picture behind it does. A settings file that
    /// still says "custom background" after the image has gone must draw a gradient the
    /// user can see and change — the alternative is a style index that indexes nothing,
    /// which is a crash on the next capture rather than a frame anyone can act on.
    /// </summary>
    [TestMethod]
    public void CustomBackgroundStyle_FallsBackToAGradientWhenThePictureIsGone()
    {
        var withPicture = new BeautifyOptions(
            BeautifyOptions.CustomBackgroundStyle,
            Backdrop: new BeautifyBackdrop(4, 4, Solid(4, 4, 10, 20, 30))).Normalized();

        var without = new BeautifyOptions(BeautifyOptions.CustomBackgroundStyle).Normalized();

        Assert.AreEqual(BeautifyOptions.CustomBackgroundStyle, withPicture.StyleIndex);
        Assert.AreEqual(0, without.StyleIndex);
    }

    /// <summary>
    /// A hand-edited settings file must not be able to ask for a blur the slider cannot
    /// ask back down again, which is the same reason every other frame measurement is
    /// clamped to its slider's ends.
    /// </summary>
    [TestMethod]
    public void BackgroundBlur_IsHeldToTheSlidersEnds()
    {
        Assert.AreEqual(
            BeautifyOptions.MaximumBackgroundBlur,
            new BeautifyOptions(BackgroundBlur: 5000).Normalized().BackgroundBlur);

        Assert.AreEqual(0, new BeautifyOptions(BackgroundBlur: -1).Normalized().BackgroundBlur);
    }

    /// <summary>
    /// The picture is what the frame is drawn on, not merely something the options row
    /// remembers. If the renderer ignored it the setting would look applied everywhere
    /// except in the file, which is the one place it matters.
    /// </summary>
    [TestMethod]
    public void Render_PaintsTheBackgroundFromThePictureRatherThanTheGradient()
    {
        var picture = new BeautifyBackdrop(8, 8, Solid(8, 8, 0, 0, 255));
        var options = new BeautifyOptions(
            BeautifyOptions.CustomBackgroundStyle,
            Padding: 20,
            ShadowRadius: 0,
            Backdrop: picture);

        var (width, _, pixels) = BeautifyRenderer.Render(
            20, 20, Solid(20, 20, 255, 255, 255), options);

        // The top-left corner is padding, so it is background and nothing else.
        var (blue, green, red) = (pixels[0], pixels[1], pixels[2]);
        Assert.AreEqual(0, blue);
        Assert.AreEqual(0, green);
        Assert.AreEqual(255, red);
        Assert.IsTrue(width > 20);
    }

    /// <summary>
    /// Blurring is asked for on every repaint of a slider drag, and a screen-sized picture
    /// takes long enough that doing it each time is the difference between a preview that
    /// follows the thumb and one that does not. The memo is the feature, so it is tested.
    /// </summary>
    [TestMethod]
    public void PixelsBlurredBy_ReturnsTheSameBufferWhileTheRadiusHoldsStill()
    {
        var picture = new BeautifyBackdrop(16, 16, Solid(16, 16, 30, 60, 90));

        Assert.AreSame(picture.PixelsBlurredBy(8), picture.PixelsBlurredBy(8));
        Assert.AreNotSame(picture.PixelsBlurredBy(8), picture.PixelsBlurredBy(12));

        // And no radius at all hands back the picture itself rather than a copy of it.
        Assert.AreSame(picture.PixelsBlurredBy(0), picture.PixelsBlurredBy(0));
    }
}
