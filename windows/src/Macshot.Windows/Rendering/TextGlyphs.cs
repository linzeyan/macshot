using Macshot.Windows.Core.Annotations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Macshot.Windows.Rendering;

/// <summary>
/// The element a text annotation's sprite is rasterized from, and the font size the
/// on-canvas entry box has to match.
/// </summary>
/// <remarks>
/// The entry box and the sprite share <see cref="FontSizeFor"/> on purpose: they are
/// two views of one mark, and a size chosen twice is a size that drifts. What the
/// user typed is then what the capture gets. See
/// <c>docs/windows-port/architecture.md</c>, decision D7.
/// </remarks>
internal static class TextGlyphs
{
    /// <summary>Readable at capture resolution without the width slider being touched.</summary>
    private const double MinimumFontSize = 24;

    /// <summary>
    /// Size follows the width slider, the same way the badge diameter and the
    /// pixelate strength do, so the toolbar already controls it.
    /// </summary>
    private const double FontSizePerStrokeUnit = 8;

    /// <summary>
    /// The font size in layout units. Chosen in frame pixels and divided by the
    /// scale, because <c>RenderTargetBitmap</c> rasterizes layout units at that
    /// scale: picking it in layout units would halve the text on a 200% display.
    /// </summary>
    public static double FontSizeFor(AnnotationStyle style, double rasterizationScale)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rasterizationScale);

        return Math.Max(MinimumFontSize, style.StrokeWidth * FontSizePerStrokeUnit) / rasterizationScale;
    }

    public static FrameworkElement Build(string text, AnnotationStyle style, double rasterizationScale)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        return new TextBlock
        {
            Text = text,
            FontSize = FontSizeFor(style, rasterizationScale),
            Foreground = new SolidColorBrush(GlyphSpriteFactory.ToBrushColor(style)),

            // The entry box does not wrap either, so a long line stays one long line
            // and the sprite is as wide as what was typed.
            TextWrapping = TextWrapping.NoWrap,
        };
    }
}
