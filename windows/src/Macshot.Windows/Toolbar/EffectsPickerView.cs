using System.Runtime.InteropServices.WindowsRuntime;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using static Macshot.Windows.Services.Localization;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// The Adjust popover: the named looks along the top, then brightness, contrast,
/// saturation and sharpness.
/// </summary>
/// <remarks>
/// <para>
/// The same contents as macshot's <c>EffectsPickerView</c>, in the same order and at its
/// measurements — <c>EffectsPickerView.swift:12–19</c>. Four 52-point swatches to a row
/// under a "Presets" heading, a rule, then four rows of a 72-point label beside a
/// 150-point slider, and Reset in the corner.
/// </para>
/// <para>
/// The swatches are rendered by <see cref="ImageEffects.Swatch"/> rather than drawn here,
/// so the square the user picks from has been through the same code the capture will
/// be: a swatch produced any other way is a promise the result may not keep.
/// </para>
/// <para>
/// The grid is laid out by hand for the reasons <see cref="BeautifySwatchGrid"/> gives.
/// A <c>GridView</c> wraps at whatever width it happens to be measured at, which broke
/// macshot's four and four into five and three; and the cell it fills to say which item
/// is selected is not the accent ring macshot strokes round the look in use.
/// </para>
/// <para>
/// It reports every move rather than waiting for the popover to close. An adjustment
/// is chosen by eye, and a slider whose answer arrives later is one nobody can aim.
/// </para>
/// </remarks>
internal sealed class EffectsPickerView : StackPanel
{
    private const double Inset = 10;

    private const int SwatchExtent = 52;

    private const double SwatchRadius = 6;

    private const double SwatchGap = 6;

    private const int Columns = 4;

    /// <summary>How far the ring sits outside a swatch, and how thick it is.</summary>
    private const double Ring = 2;

    /// <summary>How far the name sits above the bottom of its swatch.</summary>
    private const double CaptionInset = 4;

    private const double LabelWidth = 72;

    private const double SliderWidth = 150;

    private const double SliderRowHeight = 24;

    private const double SectionGap = 10;

    private const double HeadingFontSize = 10;

    private const double RowFontSize = 10;

    private const double SwatchFontSize = 8;

    private const double ResetWidth = 60;

    private const double ResetHeight = 22;

    private readonly Grid _presets = new()
    {
        // The rings live outside the swatches, so the gap they share is what is left of
        // macshot's six once both have taken their two.
        ColumnSpacing = SwatchGap - (Ring * 2),
        RowSpacing = SwatchGap - (Ring * 2),

        // And the row is pulled back by a ring's thickness, so it is the swatches rather
        // than the rings that sit on the popover's padding line.
        Margin = new Thickness(-Ring, -Ring, -Ring, SectionGap - Ring),
    };

    private readonly List<Border> _rings = [];

    private readonly Slider _brightness = NewSlider(-0.5, 0.5, 0.01);
    private readonly Slider _contrast = NewSlider(0.5, 2, 0.01);
    private readonly Slider _saturation = NewSlider(0, 2, 0.01);
    private readonly Slider _sharpness = NewSlider(0, 2, 0.01);

    private ImageEffectPreset _preset;

    /// <summary>Set while the controls are being filled in, so echoes are not reported back.</summary>
    private bool _loading;

    public EffectsPickerView()
    {
        // Dark whatever the system theme is, the way macshot's popovers force darkAqua.
        // The slab underneath is the flyout's, from ToolbarPalette.BareFlyoutStyle; both
        // halves are needed, because a dark-themed panel on WinUI's own light card is
        // white text nobody can read.
        RequestedTheme = ElementTheme.Dark;
        Background = ToolbarPalette.BackgroundBrush;
        Padding = new Thickness(Inset);
        Width = Math.Max(
            (Inset * 2) + (Columns * SwatchExtent) + ((Columns - 1) * SwatchGap),
            (Inset * 2) + LabelWidth + SliderWidth + 8);

        var presets = Enum.GetValues<ImageEffectPreset>();
        for (var column = 0; column < Columns; column++)
        {
            _presets.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        for (var row = 0; row * Columns < presets.Length; row++)
        {
            _presets.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var index = 0; index < presets.Length; index++)
        {
            var swatch = BuildSwatch(presets[index]);
            Grid.SetColumn(swatch, index % Columns);
            Grid.SetRow(swatch, index / Columns);
            _presets.Children.Add(swatch);
            _rings.Add(swatch);
        }

        Children.Add(Heading(L("Presets"), new Thickness(0, 0, 0, 4)));
        Children.Add(_presets);
        Children.Add(Rule());
        Children.Add(Heading(L("Adjustments"), new Thickness(0, 0, 0, 6)));
        Children.Add(Labelled(L("Brightness"), _brightness));
        Children.Add(Labelled(L("Contrast"), _contrast));
        Children.Add(Labelled(L("Saturation"), _saturation));
        Children.Add(Labelled(L("Sharpness"), _sharpness));

        var reset = new Button
        {
            Content = L("Reset"),
            Width = ResetWidth,
            Height = ResetHeight,
            Padding = new Thickness(0),
            FontSize = RowFontSize,
            FontWeight = FontWeights.Medium,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };

        AppFonts.Weigh(reset);

        reset.Click += (_, _) => Options = ImageEffectsOptions.Default;
        Children.Add(reset);

        _brightness.ValueChanged += (_, _) => Report();
        _contrast.ValueChanged += (_, _) => Report();
        _saturation.ValueChanged += (_, _) => Report();
        _sharpness.ValueChanged += (_, _) => Report();

        Options = ImageEffectsOptions.Default;
    }

    /// <summary>Raised on every move, with what the whole popover now asks for.</summary>
    public event EventHandler<ImageEffectsOptions>? Changed;

    public ImageEffectsOptions Options
    {
        get => new(
            _preset,
            _brightness.Value,
            _contrast.Value,
            _saturation.Value,
            _sharpness.Value);

        set
        {
            Fill(value);
            Report();
        }
    }

    /// <summary>
    /// Fills the popover in without telling anyone — for the state carried over from the
    /// last capture.
    /// </summary>
    /// <remarks>
    /// The host applied that itself before the first frame was drawn, so a report here
    /// would only have it redraw the capture into what it already shows.
    /// </remarks>
    public void Load(ImageEffectsOptions options) => Fill(options);

    private void Fill(ImageEffectsOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var resolved = value.Normalized();

        // One report at the end rather than five as each control is filled in: the
        // host would otherwise redraw the capture four times for a state nobody
        // asked for.
        _loading = true;
        try
        {
            Show(resolved.Preset);
            _brightness.Value = resolved.Brightness;
            _contrast.Value = resolved.Contrast;
            _saturation.Value = resolved.Saturation;
            _sharpness.Value = resolved.Sharpness;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Rings the look currently in use, so the grid says which one it is.</summary>
    private void Show(ImageEffectPreset preset)
    {
        _rings[(int)_preset].BorderBrush = ToolbarPalette.TransparentBrush;
        _preset = preset;
        _rings[(int)_preset].BorderBrush = ToolbarPalette.AccentBrush;
    }

    /// <summary>
    /// One adjustment's slider. Its height is left to WinUI for the reason
    /// <c>AnnotationToolbarView.OptionSlider</c> gives: a Slider told to be shorter than
    /// its template's three rows puts its track below its own centre.
    /// </summary>
    private static Slider NewSlider(double minimum, double maximum, double step) => new()
    {
        Width = SliderWidth,
        Minimum = minimum,
        Maximum = maximum,
        StepFrequency = step,
        SmallChange = step,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A section name, in the weight and the muted grey the rest of the chrome uses.</summary>
    private static TextBlock Heading(string text, Thickness margin) => new()
    {
        Text = text,
        FontSize = HeadingFontSize,
        FontWeight = AppFonts.Heavier(text, FontWeights.SemiBold),
        Foreground = ToolbarPalette.IconBrush(0.5),
        Margin = margin,
    };

    private static Border Rule() => new()
    {
        Height = 1,
        Background = ToolbarPalette.IconBrush(0.12),
        Margin = new Thickness(0, 0, 0, SectionGap),
    };

    /// <summary>One adjustment: its name, then its slider, on macshot's two column widths.</summary>
    private static FrameworkElement Labelled(string label, Slider slider)
    {
        var row = new Grid
        {
            // A floor rather than a height: the slider now sizes itself, and a row fixed
            // to macshot's 24 would have four of them overlapping each other's thumbs.
            MinHeight = SliderRowHeight,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(LabelWidth) },
                new ColumnDefinition { Width = new GridLength(SliderWidth) },
            },
        };

        var name = new TextBlock
        {
            Text = label,
            FontSize = RowFontSize,
            FontWeight = AppFonts.Heavier(label, FontWeights.Medium),
            Foreground = ToolbarPalette.IconBrush(1),
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(name, 0);
        Grid.SetColumn(slider, 1);
        row.Children.Add(name);
        row.Children.Add(slider);
        return row;
    }

    /// <summary>
    /// One swatch: the sample put through the preset, with the preset's name across it.
    /// </summary>
    private Border BuildSwatch(ImageEffectPreset preset)
    {
        var (width, height, pixels) = ImageEffects.Swatch(preset, SwatchExtent);
        var bitmap = new WriteableBitmap(width, height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels, 0, pixels.Length);
        }

        var ring = new Border
        {
            BorderThickness = new Thickness(Ring),
            BorderBrush = ToolbarPalette.TransparentBrush,
            CornerRadius = new CornerRadius(SwatchRadius + Ring),
            Child = new Border
            {
                Width = SwatchExtent,
                Height = SwatchExtent,
                CornerRadius = new CornerRadius(SwatchRadius),

                // The bitmap goes in as a brush rather than as a child Image, because a
                // child would square off the corners the radius rounded — and because the
                // name has to go on top of it.
                Background = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill },
                Child = Caption(ImageEffects.DisplayName(preset)),
            },
        };

        ring.PointerPressed += (_, _) =>
        {
            Show(preset);
            Report();
        };

        return ring;
    }

    /// <summary>
    /// The preset's name, written across the bottom of its own swatch the way macshot
    /// writes it: white over a dark copy of itself, because the swatch behind it runs
    /// from a blue through an orange and no single colour reads on all of it.
    /// </summary>
    private static FrameworkElement Caption(string name)
    {
        var stack = new Grid();
        stack.Children.Add(NameText(name, shadow: true));
        stack.Children.Add(NameText(name, shadow: false));
        return stack;
    }

    private static TextBlock NameText(string name, bool shadow) => new()
    {
        Text = name,
        FontSize = SwatchFontSize,
        FontWeight = AppFonts.Heavier(name, FontWeights.SemiBold),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Bottom,
        Foreground = new SolidColorBrush(shadow ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White),
        Opacity = shadow ? 0.6 : 1,

        // Half a point right and half a point down, which is macshot's offset. A centred
        // block moves by half what a one-sided margin says, hence the 1.
        Margin = shadow
            ? new Thickness(1, 0, 0, CaptionInset - 0.5)
            : new Thickness(0, 0, 0, CaptionInset),
    };

    private void Report()
    {
        if (!_loading)
        {
            Changed?.Invoke(this, Options);
        }
    }
}
