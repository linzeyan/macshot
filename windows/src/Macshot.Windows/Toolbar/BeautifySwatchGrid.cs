using Macshot.Windows.Services;
using static Macshot.Windows.Services.Localization;
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

    private readonly Border _custom;

    private readonly Button _choose;

    private int _selected;

    public BeautifySwatchGrid()
    {
        _custom = CustomSwatch();
        _choose = ChooseButton();

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

        // The forty-ninth: whatever picture the user last chose. macshot puts the same
        // thing in its picker (OverlayView+Popovers.swift:166) rather than on the options
        // row, because it is a background like the others — it just is not a gradient.
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SetColumn(_custom, BeautifyRenderer.Styles.Count % Columns);
        SetRow(_custom, BeautifyRenderer.Styles.Count / Columns);
        Children.Add(_custom);
        _rings.Add(_custom);

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SetRow(_choose, RowDefinitions.Count - 1);
        SetColumnSpan(_choose, Columns);
        Children.Add(_choose);

        Show(0);
    }

    /// <summary>Raised with the style that was clicked.</summary>
    public event EventHandler<int>? Picked;

    /// <summary>Raised when the user asks to choose a picture rather than a gradient.</summary>
    public event EventHandler? ImageRequested;

    /// <summary>Rings the style currently in use, so the grid says where it is.</summary>
    public void Show(int styleIndex)
    {
        _rings[_selected].BorderBrush = ToolbarPalette.TransparentBrush;

        // The custom background's sentinel is negative and its swatch is the last one, so
        // it cannot be clamped into range like the rest.
        _selected = styleIndex == BeautifyOptions.CustomBackgroundStyle
            ? _rings.Count - 1
            : Math.Clamp(styleIndex, 0, _rings.Count - 2);

        _rings[_selected].BorderBrush = ToolbarPalette.AccentBrush;
    }

    /// <summary>
    /// Paints the custom swatch as the picture now stored, or leaves it empty when there
    /// is none.
    /// </summary>
    public void ShowPicture(ImageBrush? picture)
    {
        if (_custom.Child is not Border face)
        {
            return;
        }

        face.Background = picture ?? (Brush)ToolbarPalette.IconBrush(0.15);
        ((TextBlock)face.Child).Visibility = picture is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The picture's own swatch. Empty until one is chosen, and marked with a plus rather
    /// than left blank: an unringed empty square in a grid of colours reads as a gradient
    /// that failed to paint.
    /// </summary>
    private Border CustomSwatch()
    {
        var face = new Border
        {
            Width = Extent,
            Height = Extent,
            CornerRadius = new CornerRadius(6),
            Background = ToolbarPalette.IconBrush(0.15),
            Child = new TextBlock
            {
                Text = "+",
                FontSize = 14,
                Foreground = ToolbarPalette.IconBrush(0.7),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var ring = new Border
        {
            BorderThickness = new Thickness(Ring),
            BorderBrush = ToolbarPalette.TransparentBrush,
            CornerRadius = new CornerRadius(6 + Ring),
            Margin = new Thickness(Gap / 2.0),
            Child = face,
        };

        ToolTipService.SetToolTip(ring, AppFonts.Tip(L("Your own picture")));

        ring.PointerPressed += (_, _) =>
        {
            // Nothing stored yet means the click can only have meant "let me choose one":
            // selecting a background that is not there would ring an empty square.
            if (!BeautifyBackgroundStore.Exists)
            {
                ImageRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            Show(BeautifyOptions.CustomBackgroundStyle);
            Picked?.Invoke(this, BeautifyOptions.CustomBackgroundStyle);
        };

        return ring;
    }

    /// <summary>
    /// How a different picture is chosen once one is already in use, which the swatch
    /// alone cannot offer: clicking it then means "use this one".
    /// </summary>
    private Button ChooseButton()
    {
        var button = new Button
        {
            Content = L("Choose an image..."),
            FontSize = 11,
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(Gap / 2.0, Gap, Gap / 2.0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        button.Click += (_, _) => ImageRequested?.Invoke(this, EventArgs.Empty);
        return button;
    }

    /// <summary>
    /// A style painted at <paramref name="extent"/>, for anything that has to show one.
    /// </summary>
    /// <remarks>
    /// Here rather than beside each caller because the grid and the button on the frame's
    /// options row have to agree on what a style looks like: the one on the row is how you
    /// know which of the forty-eight is on, and a second way of painting it is a second
    /// thing to keep in step with the export.
    /// </remarks>
    internal static ImageBrush Paint(int styleIndex, int extent)
    {
        var (width, height, pixels) = BeautifyRenderer.Swatch(styleIndex, extent);
        var bitmap = new WriteableBitmap(width, height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels, 0, pixels.Length);
        }

        return new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
    }

    /// <summary>
    /// The custom background as a brush, for the swatch on the row and the one in the
    /// grid. Sharp rather than blurred: the swatch says which background is on, and a
    /// blurred thumbnail 28 points across says nothing at all.
    /// </summary>
    internal static ImageBrush? PaintPicture(BeautifyBackdrop? picture)
    {
        if (picture is null)
        {
            return null;
        }

        var bitmap = new WriteableBitmap(picture.Width, picture.Height);
        var pixels = picture.PixelsBlurredBy(0);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels, 0, pixels.Length);
        }

        return new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
    }

    private Border Swatch(int styleIndex)
    {
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
                Background = Paint(styleIndex, Extent),
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
