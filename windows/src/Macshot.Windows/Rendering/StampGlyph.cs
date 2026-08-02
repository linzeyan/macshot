using Macshot.Windows.Core.Annotations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Macshot.Windows.Rendering;

/// <summary>
/// The element an emoji stamp's sprite is rasterized from, plus the emoji offered.
/// </summary>
/// <remarks>
/// This is the tool that decided D7. <c>RenderTargetBitmap</c> goes through
/// DirectWrite, so the colour glyph is rasterized in colour; GDI+ predates colour
/// font formats and would have produced a monochrome outline, which is not a stamp
/// anybody wants.
/// </remarks>
internal static class StampGlyph
{
    /// <summary>The ones laid straight on the options row.</summary>
    public static IReadOnlyList<string> Quick => StampChoices.Quick;

    /// <summary>Everything the picker behind the row offers.</summary>
    public static IReadOnlyList<string> Choices => StampChoices.All;

    public static string Default => StampChoices.Default;

    public static FrameworkElement Build(string emoji, AnnotationStyle style, double rasterizationScale)
    {
        ArgumentException.ThrowIfNullOrEmpty(emoji);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rasterizationScale);

        // Frame pixels first, then divided by the scale, for the same reason the badge
        // and the text do it: the sprite is composited one to one into the capture.
        var size = style.StampSize / rasterizationScale;

        return new TextBlock
        {
            Text = emoji,
            FontSize = size,

            // Named explicitly rather than left to fallback, so the colour glyph is
            // what gets rasterized whatever the ambient font happens to be. The
            // annotation colour is deliberately not applied: an emoji brings its own.
            FontFamily = new FontFamily("Segoe UI Emoji"),
        };
    }
}
