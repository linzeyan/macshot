using System.Globalization;
using Macshot.Windows.Core.Annotations;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Macshot.Windows.Rendering;

/// <summary>
/// The ruler's reading: the number it reports, and the element that number's sprite is
/// rasterized from.
/// </summary>
/// <remarks>
/// A ruler without a reading is a line with bars on the ends — the measurement is the
/// entire tool. Core draws the line and composites this beside it, but the digits need a
/// font engine, which is why they arrive as a sprite the way a label does. See
/// <c>docs/windows-port/architecture.md</c>, decision D7.
/// </remarks>
internal static class MeasureReading
{
    /// <summary>Readable at capture resolution, and smaller than a label: it annotates the mark rather than being it.</summary>
    private const double MinimumFontSize = 16;

    private const double FontSizePerStrokeUnit = 5;

    /// <summary>
    /// What the ruler says. Whole pixels, because the span is a count of them and a
    /// fractional answer would be reporting precision the screenshot does not have.
    /// </summary>
    public static string Format(double span) =>
        string.Create(CultureInfo.CurrentCulture, $"{Math.Round(span)} px");

    public static FrameworkElement Build(AnnotationStyle style, double span, double rasterizationScale)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rasterizationScale);

        // Chosen in frame pixels and divided by the scale, because RenderTargetBitmap
        // rasterizes layout units at that scale. Picking it in layout units would halve
        // the reading on a 200% display.
        var fontSize = Math.Max(MinimumFontSize, style.StrokeWidth * FontSizePerStrokeUnit) / rasterizationScale;
        var fill = GlyphSpriteFactory.ToBrushColor(style);

        // On a pill in the mark's own colour rather than bare glyphs. A ruler is dragged
        // across whatever it is measuring, which is precisely the busy part of the
        // screenshot, and a bare number lands on it unreadable.
        return new Border
        {
            Padding = new Thickness(fontSize * 0.35, fontSize * 0.1, fontSize * 0.35, fontSize * 0.1),
            CornerRadius = new CornerRadius(fontSize * 0.35),
            Background = new SolidColorBrush(fill),
            Child = new TextBlock
            {
                Text = Format(span),
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(GlyphSpriteFactory.ReadableOn(fill)),
            },
        };
    }
}
