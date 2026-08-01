namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// How big a picture of some copied text should be.
/// </summary>
/// <remarks>
/// macshot's <c>ClipboardTextPinRenderer</c> numbers. Pinning text works by making a
/// picture of it, and the picture has to be readable without being a wall: text wraps at
/// a width that stays under a thousand points however wide the display is, and a long
/// paste is cut off at the bottom rather than allowed to be a pin taller than the screen.
/// </remarks>
public static class TextPinLayout
{
    /// <summary>Above and below the text.</summary>
    public const double PaddingVertical = 22;

    /// <summary>Left and right of it.</summary>
    public const double PaddingHorizontal = 24;

    /// <summary>In points, monospaced — a paste is as often code as prose.</summary>
    public const double FontSize = 18;

    /// <summary>
    /// The most pixels a text pin may be. Past this the window is slower to move than the
    /// text in it is worth, and nothing that big was pinned deliberately.
    /// </summary>
    public const double MaxArea = 24_000_000;

    /// <summary>Where the text wraps, for a display of this width.</summary>
    public static double MaxContentWidth(double screenWidth) =>
        Math.Max(320, Math.Min(980, screenWidth * 0.72));

    /// <summary>The tallest the picture may be, for a display of this height.</summary>
    public static double MaxImageHeight(double screenHeight) =>
        Math.Max(240, screenHeight * 0.82);

    /// <summary>
    /// The picture's size for text that measured this big, padding included.
    /// </summary>
    /// <remarks>
    /// The height is cut rather than scaled, and only then is the whole thing scaled to
    /// fit the area limit. Cutting keeps the top of a long paste at full size, which is
    /// the part anyone pinned it for; scaling first would shrink every line to fit text
    /// nobody is going to read.
    /// </remarks>
    public static (int Width, int Height) Fit(
        double contentWidth,
        double contentHeight,
        double screenWidth,
        double screenHeight)
    {
        var width = Math.Ceiling(contentWidth + (PaddingHorizontal * 2));
        var height = Math.Ceiling(contentHeight + (PaddingVertical * 2));

        height = Math.Min(height, MaxImageHeight(screenHeight));

        if (width * height > MaxArea)
        {
            var scale = Math.Sqrt(MaxArea / (width * height));
            width = Math.Max(320, Math.Floor(width * scale));
            height = Math.Max(180, Math.Floor(height * scale));
        }

        return ((int)Math.Max(1, width), (int)Math.Max(1, height));
    }
}
