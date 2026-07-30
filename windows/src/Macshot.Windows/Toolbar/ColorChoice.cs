using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// A labelled swatch that opens a colour picker when it is clicked.
/// </summary>
/// <remarks>
/// A button rather than a picker sitting in the page: three pickers open at once is most
/// of a settings page spent on a choice that is made twice ever, and the swatch says what
/// the current answer is without any of that room.
/// </remarks>
internal sealed partial class ColorChoice : UserControl
{
    private readonly Border _swatch;
    private readonly ColorPicker _picker;

    public ColorChoice(string label)
    {
        _swatch = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(96, 128, 128, 128)),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var face = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        face.Children.Add(_swatch);
        face.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });

        // Alpha is not offered: a translucent toolbar over a screenshot is a toolbar the
        // screenshot shows through, which is the one thing this bar must never be.
        _picker = new ColorPicker { IsAlphaEnabled = false };
        _picker.ColorChanged += (_, args) => Show(args.NewColor);

        Content = new Button
        {
            Content = face,
            Flyout = new Flyout { Content = _picker },
        };
    }

    /// <summary>
    /// Raised whenever the swatch's colour changes, including when it is assigned.
    /// </summary>
    /// <remarks>
    /// Assignment included because separating the two would mean tracking which of the
    /// picker's own notifications came from the user — and the one place that listens is
    /// loading these controls when it assigns, so it already knows to ignore them.
    /// </remarks>
    public event EventHandler? Changed;

    /// <summary>The colour chosen, opaque.</summary>
    public Color Color
    {
        get => _picker.Color;
        set
        {
            _picker.Color = value;
            Show(value);
        }
    }

    private void Show(Color color)
    {
        _swatch.Background = new SolidColorBrush(color);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
