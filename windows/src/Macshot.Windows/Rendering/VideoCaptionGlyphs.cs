using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Macshot.Windows.Rendering;

/// <summary>
/// Turns a caption into the pixels the export composites onto every frame it covers.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>VideoTextRasterizer</c>, and the same bargain: font shaping happens once
/// per export rather than once per frame, because doing it at video frame rate would cost
/// more than the encode does. It is also decision D7 again — glyphs become pixels in the
/// UI half, where <c>RenderTargetBitmap</c> means DirectWrite and therefore correct font
/// fallback, and Core composites those pixels without a font engine.
/// </para>
/// <para>
/// The face is <see cref="AppFonts.Family"/> rather than macOS's system font, because a
/// caption is typed by the user and may be in a language Segoe UI has no glyphs for. The
/// weight is the segment's own and not <see cref="AppFonts.Heavier"/>: a caption's bold
/// is a switch the user set, and deciding it from the script instead would make that
/// switch do nothing for half the world's captions.
/// </para>
/// </remarks>
internal static class VideoCaptionGlyphs
{
    /// <summary>
    /// How far the glyphs sit inside the pill, as a fraction of the font size.
    /// </summary>
    /// <remarks>
    /// macshot's 0.18, and half of it vertically. Relative to the size rather than fixed,
    /// so a caption set at 104 points is not crowded by the padding a 32-point one wants.
    /// </remarks>
    private const double PadFraction = 0.18;

    /// <summary>
    /// The pill's corner: a quarter of the shorter side, never more than three tenths of
    /// the height. macshot's two rules, which together give a proper pill on a short
    /// caption and a softly rounded box on a tall one.
    /// </summary>
    private const double CornerOfShortSide = 0.25;

    private const double CornerOfHeight = 0.30;

    /// <summary>
    /// Rasterizes <paramref name="caption"/> at <paramref name="pixelWidth"/> by
    /// <paramref name="pixelHeight"/> pixels of the export.
    /// </summary>
    /// <param name="framePixelHeight">
    /// How tall the exported frame is. The caption's size is stated against a 1080-tall
    /// frame, so this is what turns it into pixels — see
    /// <see cref="VideoTextSegment.PixelFontSize"/>.
    /// </param>
    /// <param name="rasterizationScale">
    /// The display's scale. <c>RenderTargetBitmap</c> rasterizes layout units at it, so
    /// every measurement here is divided by it — asking for a 96-unit box on a 200%
    /// display would otherwise produce a 192-pixel raster.
    /// </param>
    public static async Task<FrameOverlay.VideoCaptionRaster?> RenderAsync(
        Canvas host,
        VideoTextSegment caption,
        int pixelWidth,
        int pixelHeight,
        int framePixelHeight,
        double rasterizationScale)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rasterizationScale);

        if (pixelWidth < 1 || pixelHeight < 1)
        {
            return null;
        }

        var fontPixels = VideoTextSegment.PixelFontSize(caption.FontSize, framePixelHeight, pixelHeight);
        var toLayout = 1 / rasterizationScale;
        var pad = fontPixels * PadFraction * toLayout;

        var glyphs = new TextBlock
        {
            Text = caption.Text,
            FontSize = fontPixels * toLayout,
            FontFamily = AppFonts.Family,
            FontWeight = caption.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = caption.Italic
                ? global::Windows.UI.Text.FontStyle.Italic
                : global::Windows.UI.Text.FontStyle.Normal,
            Foreground = new SolidColorBrush(GlyphSpriteFactory.ToBrushColor(caption.TextColor, 1)),
            TextAlignment = caption.Alignment switch
            {
                VideoTextAlignment.Centre => TextAlignment.Center,
                VideoTextAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            },

            // Wrapped inside the rectangle the user drew, and cut off rather than allowed
            // to overflow it. macshot truncates the tail for the same reason: a caption
            // that spilled past its own pill would look like a bug in the export.
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,

            // Centred in the box whatever it turns out to be, which is what makes a
            // one-line caption sit in the middle of the pill rather than at its top.
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var shortSide = Math.Min(pixelWidth, pixelHeight) * toLayout;
        var corner = caption.Background is VideoTextBackground.Rounded
            ? Math.Min(shortSide * CornerOfShortSide, pixelHeight * toLayout * CornerOfHeight)
            : 0;

        var pill = new Border
        {
            Width = pixelWidth * toLayout,
            Height = pixelHeight * toLayout,
            Padding = new Thickness(pad, pad / 2, pad, pad / 2),
            CornerRadius = new CornerRadius(corner),
            Background = caption.Background is VideoTextBackground.None
                ? null
                : new SolidColorBrush(GlyphSpriteFactory.ToBrushColor(caption.BackgroundColor, 1)),
            Child = glyphs,
        };

        var sprite = await GlyphSpriteFactory.RenderAsync(host, pill);

        return new FrameOverlay.VideoCaptionRaster(sprite.Width, sprite.Height, sprite.Pixels.ToArray());
    }
}
