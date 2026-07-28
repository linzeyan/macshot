using System.Globalization;
using Macshot.Windows.Core.Annotations;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Windows.UI.Color
// resolves to Macshot.Windows.UI.Color and does not compile.
using Windows.UI;

namespace Macshot.Windows.Rendering;

/// <summary>
/// The element a numbered badge's sprite is rasterized from.
/// </summary>
/// <remarks>
/// It is a real XAML element rather than geometry the Core rasterizer draws because
/// the digits need a font engine, and building the badge around them in XAML keeps
/// the circle and its number in one piece. See
/// <c>docs/windows-port/architecture.md</c>, decision D7.
/// </remarks>
internal static class NumberBadge
{
    /// <summary>Small enough to sit on a UI element, large enough to read at capture resolution.</summary>
    private const double MinimumDiameter = 28;

    /// <summary>
    /// Size follows the width slider, the same way the pixelate and blur strengths
    /// do, so the toolbar already controls it and no new option is needed.
    /// </summary>
    private const double DiameterPerStrokeUnit = 10;

    public static FrameworkElement Build(int value, AnnotationStyle style, double rasterizationScale)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rasterizationScale);

        // Sizes are chosen in frame pixels and then divided by the scale, because
        // RenderTargetBitmap rasterizes layout units at that scale. Choosing them in
        // layout units directly would make the badge come out half the intended size
        // on a 200% display, which is D7's coordinate trap.
        var diameter = Math.Max(MinimumDiameter, style.StrokeWidth * DiameterPerStrokeUnit) / rasterizationScale;
        var fill = GlyphSpriteFactory.ToBrushColor(style);

        return new Border
        {
            Height = diameter,

            // A minimum rather than a fixed width, with a fully rounded corner: a
            // circle at "9" that grows into a pill at "10" instead of clipping the
            // second digit.
            MinWidth = diameter,
            Padding = new Thickness(diameter * 0.2, 0, diameter * 0.2, 0),
            CornerRadius = new CornerRadius(diameter / 2),
            Background = new SolidColorBrush(fill),
            Child = new TextBlock
            {
                Text = value.ToString(CultureInfo.InvariantCulture),
                FontSize = diameter * 0.6,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ReadableOn(fill)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    /// <summary>
    /// Picks the digit colour from the badge's perceived luminance, so a yellow badge
    /// gets dark digits instead of white ones nobody can read.
    /// </summary>
    private static Color ReadableOn(Color background)
    {
        var luminance = ((0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B)) / byte.MaxValue;

        // Both branches are built the same way rather than one of them reaching for a
        // named colour: WinUI 3 keeps the Color struct in Windows.UI but moved the
        // Colors palette to Microsoft.UI, and inside namespace Macshot.Windows that is
        // one import too easy to get wrong.
        return luminance > 0.6
            ? Color.FromArgb(byte.MaxValue, 26, 26, 26)
            : Color.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
    }
}
