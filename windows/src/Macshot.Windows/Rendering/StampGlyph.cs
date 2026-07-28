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
    /// <summary>Large enough to read on a screenshot without covering what it points at.</summary>
    private const double MinimumSize = 40;

    private const double SizePerStrokeUnit = 14;

    /// <summary>
    /// A short curated set rather than the whole emoji catalogue: this is the row a
    /// screenshot annotator actually reaches for, and a full picker is a feature of
    /// its own.
    /// </summary>
    public static IReadOnlyList<string> Choices { get; } =
    [
        "\U0001F44D", "\U0001F44E", "✅", "❌", "⚠️", "⭐",
        "\U0001F525", "\U0001F389", "❤️", "\U0001F440", "\U0001F914", "\U0001F44F",
        "\U0001F680", "\U0001F41B", "\U0001F512", "\U0001F4A1",
    ];

    public static string Default => Choices[0];

    public static FrameworkElement Build(string emoji, AnnotationStyle style, double rasterizationScale)
    {
        ArgumentException.ThrowIfNullOrEmpty(emoji);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rasterizationScale);

        // Frame pixels first, then divided by the scale, for the same reason the badge
        // and the text do it: the sprite is composited one to one into the capture.
        var size = Math.Max(MinimumSize, style.StrokeWidth * SizePerStrokeUnit) / rasterizationScale;

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
