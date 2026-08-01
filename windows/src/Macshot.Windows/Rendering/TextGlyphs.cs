using Macshot.Windows.Core.Annotations;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Macshot.Windows.Rendering;

/// <summary>
/// The element a text annotation's sprite is rasterized from, and the font size the
/// on-canvas entry box has to match.
/// </summary>
/// <remarks>
/// The entry box and the sprite share <see cref="FontSizeFor"/> and
/// <see cref="FamilyFor"/> on purpose: they are two views of one mark, and a face or a
/// size chosen twice is one that drifts. What the user typed is then what the capture
/// gets. See <c>docs/windows-port/architecture.md</c>, decision D7.
/// </remarks>
internal static class TextGlyphs
{
    /// <summary>macshot's pill: 4 clear of the glyphs on every side — <c>Annotation.swift:1647</c>.</summary>
    private const double PillPadding = 4;

    /// <summary>And its corner, and the width of the line round it — <c>:1649, 1661</c>.</summary>
    private const double PillCorner = 4;

    private const double PillOutline = 2;

    /// <summary>
    /// The font size in layout units. Chosen in frame pixels and divided by the
    /// scale, because <c>RenderTargetBitmap</c> rasterizes layout units at that
    /// scale: picking it in layout units would halve the text on a 200% display.
    /// </summary>
    public static double FontSizeFor(AnnotationStyle style, double rasterizationScale)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rasterizationScale);

        return Math.Clamp(
            style.FontSize,
            AnnotationStyle.MinFontSize,
            AnnotationStyle.MaxFontSize) / rasterizationScale;
    }

    /// <summary>
    /// The face a label is set in. Empty means the system font, and so does a family
    /// this machine does not have — WinUI falls back on its own, which is the behaviour
    /// wanted for a settings file carried over from a machine with different fonts.
    /// </summary>
    public static FontFamily FamilyFor(AnnotationStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        return string.IsNullOrWhiteSpace(style.FontFamily)
            ? FontFamily.XamlAutoFontFamily
            : new FontFamily(style.FontFamily);
    }

    /// <remarks>
    /// Written out in full because the weight <em>struct</em> lives in
    /// <c>Windows.UI.Text</c> while the weight <em>table</em> XAML uses lives in
    /// <c>Microsoft.UI.Text</c>, and importing both makes <c>FontWeights</c> ambiguous.
    /// </remarks>
    public static global::Windows.UI.Text.FontWeight WeightFor(AnnotationStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        return style.Bold ? FontWeights.Bold : FontWeights.Normal;
    }

    /// <summary>
    /// The label as it will be rasterized: the glyphs, and macshot's pill behind them
    /// when one is asked for.
    /// </summary>
    /// <remarks>
    /// The pill is a <c>Border</c> rather than something the Core rasterizer draws,
    /// because the sprite is one bitmap and the padding is measured off the glyphs — a
    /// rectangle placed by Core would have to be told how wide the text came out, which
    /// is the question the sprite exists to avoid asking twice.
    /// </remarks>
    public static FrameworkElement Build(string text, AnnotationStyle style, double rasterizationScale)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentNullException.ThrowIfNull(style);

        var glyphs = new TextBlock
        {
            // A TextBox hands back carriage returns; a TextBlock breaks on line feeds.
            // Left alone, a label typed on two lines would be rasterized as one long
            // line with a box glyph in the middle of it.
            Text = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n'),
            FontSize = FontSizeFor(style, rasterizationScale),
            FontFamily = FamilyFor(style),
            FontWeight = WeightFor(style),
            Foreground = new SolidColorBrush(GlyphSpriteFactory.ToBrushColor(style)),

            // The entry box does not wrap either, so a long line stays one long line
            // and the sprite is as wide as what was typed. Explicit breaks still break:
            // NoWrap only means nothing is broken that the user did not break.
            TextWrapping = TextWrapping.NoWrap,
        };

        if (style.TextBackground is null && style.TextOutline is null)
        {
            return glyphs;
        }

        // Both measured in layout units for the same reason the font size is: the sprite
        // is rasterized at the display's scale, so a padding chosen in frame pixels would
        // be twice macshot's at 200%.
        var scaled = 1 / rasterizationScale;

        return new Border
        {
            Padding = new Thickness(PillPadding * scaled),
            CornerRadius = new CornerRadius(PillCorner * scaled),
            Background = style.TextBackground is { } fill
                ? new SolidColorBrush(GlyphSpriteFactory.ToBrushColor(fill, style.Opacity))
                : null,
            BorderThickness = new Thickness(style.TextOutline is null ? 0 : PillOutline * scaled),
            BorderBrush = style.TextOutline is { } edge
                ? new SolidColorBrush(GlyphSpriteFactory.ToBrushColor(edge, style.Opacity))
                : null,
            Child = glyphs,
        };
    }
}
