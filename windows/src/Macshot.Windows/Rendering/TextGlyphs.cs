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
/// The entry box and the sprite share <see cref="FontSizeFor"/>, <see cref="FamilyFor"/>
/// and the rest of these on purpose: they are two views of one mark, and a face or a size
/// chosen twice is one that drifts. What the user typed is then what the capture gets.
/// See <c>docs/windows-port/architecture.md</c>, decision D7.
/// </remarks>
internal static class TextGlyphs
{
    /// <summary>macshot's pill: 4 clear of the glyphs on every side — <c>Annotation.swift:1647</c>.</summary>
    private const double PillPadding = 4;

    /// <summary>And its corner, and the width of the line round it — <c>:1649, 1661</c>.</summary>
    private const double PillCorner = 4;

    private const double PillOutline = 2;

    /// <summary>
    /// How many copies of the glyphs the outline is laid down as, spaced evenly round a
    /// circle.
    /// </summary>
    /// <remarks>
    /// macshot strokes the glyph paths themselves through a layout manager of its own
    /// (<c>OutlineTextRenderer.swift</c>). WinUI hands out no glyph outlines and no text
    /// stroke, so the line is built the way it is built everywhere else in XAML: the text
    /// drawn round itself in the outline colour, with the real one on top hiding the
    /// inside. Eight is where the ring stops looking like a ring — at four the corners of
    /// a letter show the gaps, and past eight nothing changes but the work.
    /// </remarks>
    private const int OutlineCopies = 8;

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

    /// <summary>Whether the glyphs slant. Qualified for the reason the weight is.</summary>
    public static global::Windows.UI.Text.FontStyle SlantFor(AnnotationStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        return style.Italic
            ? global::Windows.UI.Text.FontStyle.Italic
            : global::Windows.UI.Text.FontStyle.Normal;
    }

    /// <summary>
    /// Which edge the lines are hung from. Only a label of more than one line shows it,
    /// which is the case it is there for.
    /// </summary>
    public static TextAlignment AlignmentFor(AnnotationStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        return style.TextAlignment switch
        {
            LabelAlignment.Centre => TextAlignment.Center,
            LabelAlignment.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
    }

    /// <summary>
    /// The rules through and under the glyphs. Both at once when both are asked for: they
    /// are two switches on the row rather than a choice between two.
    /// </summary>
    public static global::Windows.UI.Text.TextDecorations DecorationsFor(AnnotationStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        var marks = global::Windows.UI.Text.TextDecorations.None;
        if (style.Underline)
        {
            marks |= global::Windows.UI.Text.TextDecorations.Underline;
        }

        if (style.Strikethrough)
        {
            marks |= global::Windows.UI.Text.TextDecorations.Strikethrough;
        }

        return marks;
    }

    /// <summary>
    /// The label as it will be rasterized: the glyphs, the line round them when one is
    /// asked for, and macshot's pill behind the lot when that is.
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

        var body = style.TextGlyphStroke is { } edge
            ? Outlined(text, style, rasterizationScale, edge)
            : Glyphs(text, style, rasterizationScale, GlyphSpriteFactory.ToBrushColor(style));

        if (style.TextBackground is null && style.TextOutline is null)
        {
            return body;
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
            BorderBrush = style.TextOutline is { } rim
                ? new SolidColorBrush(GlyphSpriteFactory.ToBrushColor(rim, style.Opacity))
                : null,
            Child = body,
        };
    }

    private static FrameworkElement Outlined(
        string text,
        AnnotationStyle style,
        double rasterizationScale,
        AnnotationColor edge) =>
        Ringed(
            colour => Glyphs(text, style, rasterizationScale, colour),
            GlyphSpriteFactory.ToBrushColor(edge, style.Opacity),
            GlyphSpriteFactory.ToBrushColor(style),
            AnnotationStyle.GlyphStrokeWidth(style.FontSize) / rasterizationScale);

    /// <summary>
    /// Glyphs with a line round them: <paramref name="glyphs"/> laid down
    /// <see cref="OutlineCopies"/> times in <paramref name="stroke"/> on a circle of radius
    /// <paramref name="reach"/>, then once more in <paramref name="fill"/> on top.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The copies are moved by their margins rather than by a transform, so the block ends
    /// up as wide as the outline actually reaches — a transform leaves layout alone, and
    /// the sprite is cut to the layout, so the left edge of the line would be sliced off.
    /// Each copy carries the same total margin, which is what keeps every one of them
    /// measuring and breaking its lines identically.
    /// </para>
    /// <para>
    /// Taking the glyphs as a factory rather than being written twice, because a video
    /// caption's outline has to be the same mark as a label's. They arrive at
    /// <paramref name="reach"/> from different places — a label's is a fraction of its font
    /// size, a caption's is a width the user set — but what an outline <em>looks like</em>
    /// must not be two answers.
    /// </para>
    /// </remarks>
    public static FrameworkElement Ringed(
        Func<global::Windows.UI.Color, FrameworkElement> glyphs,
        global::Windows.UI.Color stroke,
        global::Windows.UI.Color fill,
        double reach)
    {
        ArgumentNullException.ThrowIfNull(glyphs);

        var block = new Grid();

        for (var copy = 0; copy < OutlineCopies; copy++)
        {
            var angle = copy * 2 * Math.PI / OutlineCopies;
            var across = reach * Math.Cos(angle);
            var down = reach * Math.Sin(angle);

            var ghost = glyphs(stroke);
            ghost.Margin = new Thickness(reach + across, reach + down, reach - across, reach - down);
            block.Children.Add(ghost);
        }

        var face = glyphs(fill);
        face.Margin = new Thickness(reach);
        block.Children.Add(face);

        return block;
    }

    private static TextBlock Glyphs(
        string text,
        AnnotationStyle style,
        double rasterizationScale,
        global::Windows.UI.Color color) => new()
        {
            // A TextBox hands back carriage returns; a TextBlock breaks on line feeds.
            // Left alone, a label typed on two lines would be rasterized as one long
            // line with a box glyph in the middle of it.
            Text = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n'),
            FontSize = FontSizeFor(style, rasterizationScale),
            FontFamily = FamilyFor(style),
            FontWeight = WeightFor(style),
            FontStyle = SlantFor(style),
            TextDecorations = DecorationsFor(style),
            TextAlignment = AlignmentFor(style),
            Foreground = new SolidColorBrush(color),

            // The entry box does not wrap either, so a long line stays one long line
            // and the sprite is as wide as what was typed. Explicit breaks still break:
            // NoWrap only means nothing is broken that the user did not break.
            TextWrapping = TextWrapping.NoWrap,
        };
}
