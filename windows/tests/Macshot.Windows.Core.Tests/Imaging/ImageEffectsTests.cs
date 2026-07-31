using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

/// <summary>
/// The Adjust popover's arithmetic: what each slider does, and what it leaves alone.
/// </summary>
[TestClass]
public sealed class ImageEffectsTests
{
    private const int Width = 4;
    private const int Height = 4;

    [TestMethod]
    public void AskedForNothing_TheCaptureComesBackUntouched()
    {
        // The common case is a capture nobody opened the popover for, and every byte of
        // a 4K frame is worth not touching.
        var frame = Frame(200, 120, 40);

        var result = ImageEffects.Apply(Width, Height, frame, ImageEffectsOptions.Default);

        CollectionAssert.AreEqual(frame, result);
    }

    [TestMethod]
    public void Mono_LeavesNoColourBehind()
    {
        var result = ImageEffects.Apply(Width, Height, Frame(200, 120, 40), new ImageEffectsOptions(ImageEffectPreset.Mono));

        Assert.AreEqual(result[0], result[1], "blue and green");
        Assert.AreEqual(result[1], result[2], "green and red");
    }

    [TestMethod]
    public void SaturationAtZero_IsTheSameAsMono()
    {
        // The slider and the preset are two ways to ask for one thing, and a user who
        // finds they disagree has found a bug rather than a subtlety.
        var frame = Frame(200, 120, 40);

        var drained = ImageEffects.Apply(Width, Height, frame, new ImageEffectsOptions(Saturation: 0));
        var mono = ImageEffects.Apply(Width, Height, frame, new ImageEffectsOptions(ImageEffectPreset.Mono));

        CollectionAssert.AreEqual(mono, drained);
    }

    [TestMethod]
    public void Contrast_TurnsAboutMidGreyRatherThanAboutBlack()
    {
        // Turned about black, contrast would be a brightness slider that also clipped:
        // every value would rise, and the picture would only ever get lighter.
        var grey = Frame(128, 128, 128);

        var result = ImageEffects.Apply(Width, Height, grey, new ImageEffectsOptions(Contrast: 2));

        Assert.AreEqual(128, result[0], 1);
    }

    [TestMethod]
    public void Brightness_MovesEveryChannelByTheSameAmount()
    {
        var result = ImageEffects.Apply(Width, Height, Frame(100, 100, 100), new ImageEffectsOptions(Brightness: 0.2));

        Assert.AreEqual(151, result[0], 1, "0.2 of full scale is 51");
    }

    [TestMethod]
    public void Sharpening_LeavesAFlatAreaAlone()
    {
        // Sharpening works on how much a pixel differs from what is around it, so an
        // even field has nothing to sharpen. A field that moved would mean the whole
        // capture had shifted in brightness.
        var flat = Frame(120, 120, 120);

        var result = ImageEffects.Apply(Width, Height, flat, new ImageEffectsOptions(Sharpness: 2));

        CollectionAssert.AreEqual(flat, result);
    }

    [TestMethod]
    public void Vivid_IsAFixedLookRatherThanSliderState()
    {
        // macshot applies Vivid instead of the sliders, so switching to it cannot
        // compound with whatever the last preset left them on.
        var frame = Frame(200, 120, 40);

        var alone = ImageEffects.Apply(Width, Height, frame, new ImageEffectsOptions(ImageEffectPreset.Vivid));
        var withSliders = ImageEffects.Apply(
            Width,
            Height,
            frame,
            new ImageEffectsOptions(ImageEffectPreset.Vivid, Brightness: 0.5, Contrast: 2, Saturation: 0));

        CollectionAssert.AreEqual(alone, withSliders);
    }

    [TestMethod]
    public void Adjustments_NeverTouchTransparency()
    {
        // A capture that became partly transparent could not be saved as a JPEG, and
        // nothing on this popover is about transparency.
        var frame = Frame(200, 120, 40, alpha: 128);

        var result = ImageEffects.Apply(Width, Height, frame, new ImageEffectsOptions(Brightness: 0.5, Sharpness: 1));

        Assert.AreEqual(128, result[3]);
    }

    [TestMethod]
    public void Options_ClampToWhatTheSlidersOffer()
    {
        var wild = new ImageEffectsOptions(ImageEffectPreset.None, 9, 9, 9, 9).Normalized();

        Assert.AreEqual(0.5, wild.Brightness);
        Assert.AreEqual(2, wild.Contrast);
        Assert.AreEqual(2, wild.Saturation);
        Assert.AreEqual(2, wild.Sharpness);
    }

    [TestMethod]
    public void ASwatch_IsPutThroughTheSameCodeTheCaptureWillBe()
    {
        // A swatch drawn by other means is a promise the result may not keep.
        var (width, height, pixels) = ImageEffects.Swatch(ImageEffectPreset.Mono, 8);

        Assert.AreEqual(8, width);
        Assert.AreEqual(8, height);
        Assert.AreEqual(8 * 8 * 4, pixels.Length);
        Assert.AreEqual(pixels[0], pixels[2], "a mono swatch has no colour left in it");
    }

    private static byte[] Frame(byte red, byte green, byte blue, byte alpha = 255)
    {
        var pixels = new byte[Width * Height * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = blue;
            pixels[index + 1] = green;
            pixels[index + 2] = red;
            pixels[index + 3] = alpha;
        }

        return pixels;
    }
}
