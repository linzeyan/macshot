using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

/// <summary>
/// The pill that shows what was typed during a recording.
/// </summary>
/// <remarks>
/// Everything here is about what lands in the buffer, because the buffer is what a viewer
/// of the recording sees. A pill in the wrong place, or one that never goes away, is worse
/// than no pill at all.
/// </remarks>
[TestClass]
public sealed class KeystrokePillTests
{
    private const int BufferWidth = 400;
    private const int BufferHeight = 96;
    private const int TextWidth = 120;
    private const int TextHeight = 34;

    [TestMethod]
    public void ThePillIsTheTextPlusMacshotsPadding()
    {
        var (width, height) = KeystrokePill.SizeFor(TextWidth, TextHeight, 1);

        Assert.AreEqual(TextWidth + 48, width, "24 either side");
        Assert.AreEqual(TextHeight + 28, height, "14 above and below");
    }

    /// <summary>
    /// The caller places the window by the pill's foot, so the foot has to be the
    /// buffer's foot however tall the pill came out.
    /// </summary>
    [TestMethod]
    public void ThePillSitsOnTheBottomEdgeOfTheBuffer()
    {
        var rendered = Render(1);
        var pillHeight = TextHeight + 28;

        Assert.IsTrue(IsPainted(rendered, BufferWidth / 2, BufferHeight - 2), "the foot is at the buffer's foot");
        Assert.IsFalse(
            IsPainted(rendered, BufferWidth / 2, BufferHeight - pillHeight - 2),
            "and nothing is drawn above its head");
    }

    [TestMethod]
    public void ThePillIsCentredAcrossTheBuffer()
    {
        var rendered = Render(1);
        var pillWidth = TextWidth + 48;
        var edge = (BufferWidth - pillWidth) / 2;

        // Half way up, which is the one height at which the left edge is straight rather
        // than curving away into a corner.
        var middle = BufferHeight - ((TextHeight + 28) / 2);

        Assert.IsTrue(IsPainted(rendered, edge + 2, middle), "inside the left edge");
        Assert.IsFalse(IsPainted(rendered, edge - 4, middle), "and nothing outside it");
    }

    /// <summary>
    /// The corners are what make it a pill rather than a box, and a rounded corner is a
    /// corner with nothing in it.
    /// </summary>
    [TestMethod]
    public void TheCornersAreRoundedAway()
    {
        var rendered = Render(1);
        var pillWidth = TextWidth + 48;
        var pillHeight = TextHeight + 28;
        var left = (BufferWidth - pillWidth) / 2;
        var top = BufferHeight - pillHeight;

        Assert.IsFalse(IsPainted(rendered, left, top), "the top-left corner is empty");
        Assert.IsTrue(
            IsPainted(rendered, left + 14, top + 14),
            "and a corner radius in from it is not");
    }

    /// <summary>
    /// The whole point of the mask: the glyphs are the only part of the pill that is
    /// white, and they are opaque where the stroke is solid.
    /// </summary>
    [TestMethod]
    public void TheGlyphsArriveWhiteOverTheDarkPill()
    {
        var rendered = Render(1);
        var pillHeight = TextHeight + 28;
        var middle = ((BufferHeight - (pillHeight / 2)) * BufferWidth) + (BufferWidth / 2);

        // The mask below is solid, so the middle of the pill is the middle of a stroke.
        Assert.AreEqual(byte.MaxValue, rendered[(middle * 4) + 3], "opaque");
        Assert.AreEqual(byte.MaxValue, rendered[middle * 4], "and white");
    }

    /// <summary>
    /// Fading is the only way a keystroke leaves. One that stopped part-way would sit
    /// over the recording for the rest of it.
    /// </summary>
    [TestMethod]
    public void FadingTakesTheWholePillDownWithIt()
    {
        var full = Render(1);
        var half = Render(0.5);
        var gone = Render(0);

        var foot = (((BufferHeight - 3) * BufferWidth) + (BufferWidth / 2)) * 4;

        Assert.IsTrue(half[foot + 3] < full[foot + 3], "half way down is fainter");
        Assert.IsTrue(half[foot + 3] > 0, "and still there");
        Assert.AreEqual(0, gone[foot + 3], "and nothing is left at the end");
    }

    /// <summary>
    /// Every buffer this writes goes to <c>UpdateLayeredWindow</c>, which reads the colour
    /// channels as already multiplied by the alpha. A pixel whiter than it is opaque is one
    /// Windows draws as a bright halo.
    /// </summary>
    [TestMethod]
    public void EveryPixelIsPremultiplied()
    {
        var rendered = Render(0.4);

        for (var offset = 0; offset < rendered.Length; offset += 4)
        {
            Assert.IsTrue(
                rendered[offset] <= rendered[offset + 3],
                $"pixel {offset / 4} is brighter than it is opaque");
        }
    }

    private static byte[] Render(double opacity)
    {
        var mask = new byte[TextWidth * TextHeight];
        Array.Fill(mask, byte.MaxValue);

        var buffer = new byte[BufferWidth * BufferHeight * 4];
        KeystrokePill.Rasterize(
            mask,
            TextWidth,
            TextHeight,
            opacity,
            1,
            buffer,
            BufferWidth,
            BufferHeight);

        return buffer;
    }

    private static bool IsPainted(byte[] pixels, int column, int row) =>
        pixels[((((row * BufferWidth) + column) * 4)) + 3] > 0;
}
