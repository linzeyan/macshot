using Macshot.Windows.Core.Recognition;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Windows.UI.Color resolves
// to Macshot.Windows.UI.Color and does not compile.
using Windows.Foundation;
using Windows.UI;

namespace Macshot.Windows.Rendering;

/// <summary>
/// The element a translated line's sprite is rasterized from: the box in the colour of
/// what it covers, with the translation set inside it.
/// </summary>
/// <remarks>
/// <para>
/// One element rather than a rectangle annotation with a text annotation on top. macshot
/// draws the pair as a single mark, and a pair here would be a pair the user has to drag
/// twice and a box that can be left behind when the words above it are deleted.
/// </para>
/// <para>
/// Every measurement is macshot's own: three pixels of margin at the sides, two above
/// and below, corners rounded by three, and type that shrinks a point at a time until
/// the translation fits the space the original took. A translation that runs longer than
/// its original is the normal case, not the exception.
/// </para>
/// </remarks>
internal static class TranslationGlyphs
{
    private const double HorizontalPadding = 3;
    private const double VerticalPadding = 2;
    private const double CornerRadius = 3;

    /// <summary>
    /// Builds the box for one line. Sizes are in layout units, which is what XAML lays
    /// out in and what <c>RenderTargetBitmap</c> multiplies by
    /// <paramref name="rasterizationScale"/> to reach capture pixels.
    /// </summary>
    public static FrameworkElement Build(TranslatedLine line, Color background, double rasterizationScale)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rasterizationScale);

        var width = line.Bounds.Width / rasterizationScale;
        var height = line.Bounds.Height / rasterizationScale;

        var text = new TextBlock
        {
            Text = line.Text,
            Foreground = new SolidColorBrush(GlyphSpriteFactory.ReadableOn(background)),

            // Medium, the weight macshot sets: regular reads as a caption laid over the
            // page, and the box is meant to read as the page's own words.
            FontWeight = FontWeights.Medium,

            // Wrapped and top-aligned, because a translation that outgrows one line
            // should take a second one inside the box rather than run off the side of it.
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
        };

        Fit(
            text,
            TranslationOverlay.FontSizeFor(line.Bounds.Height) / rasterizationScale,
            Math.Max(1, width - (HorizontalPadding * 2)),
            Math.Max(1, height - (VerticalPadding * 2)),
            TranslationOverlay.MinimumFontSize / rasterizationScale);

        return new Border
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(background),
            CornerRadius = new CornerRadius(CornerRadius),
            Padding = new Thickness(HorizontalPadding, VerticalPadding, HorizontalPadding, VerticalPadding),
            Child = text,
        };
    }

    /// <summary>
    /// Shrinks the type until the wrapped text fits the box, down to a floor below which
    /// a translation is a smudge hiding the original rather than a replacement for it.
    /// </summary>
    /// <remarks>
    /// Measured rather than estimated from the character count: the point of the box is
    /// that it covers the words underneath, and a guess that came out one line short
    /// would leave half the original showing.
    /// </remarks>
    private static void Fit(TextBlock text, double startingSize, double width, double height, double floor)
    {
        var size = Math.Max(startingSize, floor);
        while (true)
        {
            text.FontSize = size;
            text.Measure(new Size(width, double.PositiveInfinity));
            if (text.DesiredSize.Height <= height || size <= floor)
            {
                return;
            }

            size = Math.Max(floor, size - 1);
        }
    }
}
