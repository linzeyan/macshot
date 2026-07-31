using System.Runtime.InteropServices.WindowsRuntime;
using Macshot.Windows.Core.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// The Adjust popover: the named looks along the top, then brightness, contrast,
/// saturation and sharpness.
/// </summary>
/// <remarks>
/// <para>
/// The same contents as macshot's <c>EffectsPickerView</c>, in the same order. The
/// swatches are rendered by <see cref="ImageEffects.Swatch"/> rather than drawn here,
/// so the square the user picks from has been through the same code the capture will
/// be: a swatch produced any other way is a promise the result may not keep.
/// </para>
/// <para>
/// It reports every move rather than waiting for the popover to close. An adjustment
/// is chosen by eye, and a slider whose answer arrives later is one nobody can aim.
/// </para>
/// </remarks>
internal sealed class EffectsPickerView : StackPanel
{
    private const int SwatchExtent = 44;

    private readonly GridView _presets = new()
    {
        SelectionMode = ListViewSelectionMode.Single,
        MaxWidth = 240,
        IsItemClickEnabled = false,
    };

    private readonly Slider _brightness = NewSlider(-0.5, 0.5, 0.01);
    private readonly Slider _contrast = NewSlider(0.5, 2, 0.01);
    private readonly Slider _saturation = NewSlider(0, 2, 0.01);
    private readonly Slider _sharpness = NewSlider(0, 2, 0.01);

    /// <summary>Set while the controls are being filled in, so echoes are not reported back.</summary>
    private bool _loading;

    public EffectsPickerView()
    {
        RequestedTheme = ElementTheme.Dark;
        Spacing = 6;
        Width = 260;

        foreach (var preset in Enum.GetValues<ImageEffectPreset>())
        {
            _presets.Items.Add(BuildSwatch(preset));
        }

        Children.Add(_presets);
        Children.Add(Labelled("Brightness", _brightness));
        Children.Add(Labelled("Contrast", _contrast));
        Children.Add(Labelled("Saturation", _saturation));
        Children.Add(Labelled("Sharpness", _sharpness));

        var reset = new Button { Content = "Reset", HorizontalAlignment = HorizontalAlignment.Right };
        reset.Click += (_, _) => Options = ImageEffectsOptions.Default;
        Children.Add(reset);

        _presets.SelectionChanged += (_, _) => Report();
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
            (ImageEffectPreset)Math.Max(0, _presets.SelectedIndex),
            _brightness.Value,
            _contrast.Value,
            _saturation.Value,
            _sharpness.Value);

        set
        {
            ArgumentNullException.ThrowIfNull(value);

            var resolved = value.Normalized();

            // One report at the end rather than five as each control is filled in: the
            // host would otherwise redraw the capture four times for a state nobody
            // asked for.
            _loading = true;
            try
            {
                _presets.SelectedIndex = (int)resolved.Preset;
                _brightness.Value = resolved.Brightness;
                _contrast.Value = resolved.Contrast;
                _saturation.Value = resolved.Saturation;
                _sharpness.Value = resolved.Sharpness;
            }
            finally
            {
                _loading = false;
            }

            Report();
        }
    }

    private static Slider NewSlider(double minimum, double maximum, double step) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        StepFrequency = step,
        SmallChange = step,
    };

    private static FrameworkElement Labelled(string label, Slider slider)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.8 });
        panel.Children.Add(slider);
        return panel;
    }

    /// <summary>
    /// One swatch: the sample put through the preset, with the preset's name under it.
    /// </summary>
    private static FrameworkElement BuildSwatch(ImageEffectPreset preset)
    {
        var (width, height, pixels) = ImageEffects.Swatch(preset, SwatchExtent);
        var bitmap = new WriteableBitmap(width, height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels, 0, pixels.Length);
        }

        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new Image
        {
            Source = bitmap,
            Width = SwatchExtent,
            Height = SwatchExtent,
        });
        panel.Children.Add(new TextBlock
        {
            Text = ImageEffects.DisplayName(preset),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        return panel;
    }

    private void Report()
    {
        if (!_loading)
        {
            Changed?.Invoke(this, Options);
        }
    }
}
