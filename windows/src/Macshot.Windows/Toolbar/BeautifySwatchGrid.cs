using Macshot.Windows.Services;
using System.Runtime.InteropServices.WindowsRuntime;
using Macshot.Windows.Core.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// The backgrounds a capture can be framed on, as a grid of swatches.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>GradientPickerView</c>: six to a row, each one a rounded square of the
/// background itself, with the chosen one ringed in the accent colour. The port listed
/// the same 48 by name in a menu, which is a flyout longer than most screens and asks
/// the reader to know what "Dusk" looks like. Half the names are this port's own
/// invention besides — macshot only names its first eighteen, because a grid of swatches
/// never had to call them anything.
/// </para>
/// <para>
/// The squares are painted by <see cref="BeautifyRenderer.Swatch"/> rather than drawn
/// here, so what is picked from has been through the code the capture will be.
/// </para>
/// <para>
/// Laid out by hand rather than by a <c>GridView</c>: its cells are 44 across before
/// anything is put in them, and its own selection visual is not the ring macshot draws.
/// Fighting both to arrive at a fixed grid of 28-point squares is more markup than
/// placing them.
/// </para>
/// </remarks>
internal sealed class BeautifySwatchGrid : Grid
{
    /// <summary>macshot's 28-point swatch, in the same six columns.</summary>
    private const int Extent = 28;

    private const int Columns = 6;

    private const int Gap = 4;

    /// <summary>How far the ring sits outside the swatch, and how thick it is.</summary>
    private const int Ring = 2;

    private readonly List<Border> _rings = [];

    private int _selected;

    public BeautifySwatchGrid()
    {
        RequestedTheme = ElementTheme.Dark;
        Padding = new Thickness(Gap + Ring);

        var rows = (BeautifyRenderer.Styles.Count + Columns - 1) / Columns;
        for (var column = 0; column < Columns; column++)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        for (var row = 0; row < rows; row++)
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var index = 0; index < BeautifyRenderer.Styles.Count; index++)
        {
            var swatch = Swatch(index);
            SetColumn(swatch, index % Columns);
            SetRow(swatch, index / Columns);
            Children.Add(swatch);
            _rings.Add(swatch);
        }

        Show(0);
    }

    /// <summary>Raised with the style that was clicked.</summary>
    public event EventHandler<int>? Picked;

    /// <summary>Rings the style currently in use, so the grid says where it is.</summary>
    public void Show(int styleIndex)
    {
        _rings[_selected].BorderBrush = ToolbarPalette.TransparentBrush;
        _selected = Math.Clamp(styleIndex, 0, _rings.Count - 1);
        _rings[_selected].BorderBrush = ToolbarPalette.AccentBrush;
    }

    private Border Swatch(int styleIndex)
    {
        var (width, height, pixels) = BeautifyRenderer.Swatch(styleIndex, Extent);
        var bitmap = new WriteableBitmap(width, height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels, 0, pixels.Length);
        }

        var ring = new Border
        {
            // The ring is drawn outside the swatch rather than over it, the way macshot
            // insets its rounded rectangle by -2, so nothing is hidden by being chosen.
            BorderThickness = new Thickness(Ring),
            BorderBrush = ToolbarPalette.TransparentBrush,
            CornerRadius = new CornerRadius(6 + Ring),
            Margin = new Thickness(Gap / 2.0),
            Child = new Border
            {
                Width = Extent,
                Height = Extent,
                CornerRadius = new CornerRadius(6),

                // The bitmap goes in as a brush rather than as a child Image, because a
                // child would square off the corners the radius rounded.
                Background = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill },
            },
        };

        // The name is what a swatch cannot say — several of the 48 are near neighbours —
        // and a tooltip says it without giving 48 rows of text.
        ToolTipService.SetToolTip(ring, AppFonts.Tip(BeautifyRenderer.Styles[styleIndex].Name));

        ring.PointerPressed += (_, _) =>
        {
            Show(styleIndex);
            Picked?.Invoke(this, styleIndex);
        };

        return ring;
    }
}
