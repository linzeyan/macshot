using Macshot.Windows.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// A named colour that can be turned off: the word toggles it, the square beside it
/// opens a picker.
/// </summary>
/// <remarks>
/// macshot's pairing for a label's fill and its outline —
/// <c>ToolOptionsRowView.swift:1029–1060</c>. Two controls rather than one because they
/// answer two questions that are asked at different times: whether there is a pill at all
/// is decided once, and which colour it is gets fiddled with. A single swatch that meant
/// both would have no way to say "off" except a colour with no alpha, which reads as a
/// bug.
/// </remarks>
internal sealed class ToggleSwatch : StackPanel
{
    /// <summary>macshot's swatch — 18 square at corner 3 under a 1.5 border, <c>:1013–1021</c>.</summary>
    private const double SwatchSize = 18;

    private readonly ToggleButton _label;
    private readonly Button _swatch;
    private readonly Border _fill;

    private Color _color = Color.FromArgb(255, 0, 0, 0);
    private bool _quiet;

    public ToggleSwatch(string name)
    {
        Orientation = Orientation.Horizontal;
        Spacing = 2;
        VerticalAlignment = VerticalAlignment.Center;

        _label = new ToggleButton
        {
            Content = name,
            FontSize = 10,
            FontWeight = AppFonts.Heavier(name, FontWeights.Medium),
            Padding = new Thickness(6, 0, 6, 0),
            MinWidth = 0,
            Height = 22,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _fill = new Border
        {
            Width = SwatchSize,
            Height = SwatchSize,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1.5),
            BorderBrush = ToolbarPalette.IconBrush(0.4),
        };

        _swatch = new Button
        {
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            Background = ToolbarPalette.TransparentBrush,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Content = _fill,
        };

        _label.Checked += (_, _) => Announce();
        _label.Unchecked += (_, _) => Announce();
        _swatch.Click += (_, _) => SwatchPressed?.Invoke(this, EventArgs.Empty);

        Children.Add(_label);
        Children.Add(_swatch);
        Repaint();
    }

    /// <summary>Raised when the user turns it on or off. Not raised by <see cref="Show"/>.</summary>
    public event EventHandler? Toggled;

    /// <summary>Raised when the square is pressed, so the host can open its picker.</summary>
    public event EventHandler? SwatchPressed;

    /// <summary>Whether there is a colour at all, or the thing is switched off.</summary>
    public bool IsOn => _label.IsChecked == true;

    /// <summary>The colour in the square, whether or not it is switched on.</summary>
    public Color Color => _color;

    /// <summary>The button the picker should hang off.</summary>
    public FrameworkElement Anchor => _swatch;

    /// <summary>Sets both without raising <see cref="Toggled"/>.</summary>
    public void Show(bool on, Color color)
    {
        _quiet = true;
        try
        {
            _label.IsChecked = on;
            _color = color;
            Repaint();
        }
        finally
        {
            _quiet = false;
        }
    }

    /// <summary>
    /// Puts a colour in the square, turning it on: a colour picked for something switched
    /// off is a colour nobody can see, so picking one is asking for it.
    /// </summary>
    public void Pick(Color color)
    {
        _color = color;
        if (_label.IsChecked == true)
        {
            Repaint();
            Toggled?.Invoke(this, EventArgs.Empty);
            return;
        }

        _label.IsChecked = true;
    }

    private void Announce()
    {
        Repaint();
        if (!_quiet)
        {
            Toggled?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Repaint()
    {
        _fill.Background = new SolidColorBrush(_color);

        // Dimmed rather than hidden when it is off: the colour is still the one that
        // comes back when it is switched on again, and a square that vanished would make
        // that look like a fresh choice.
        _fill.Opacity = IsOn ? 1 : 0.3;
    }
}
