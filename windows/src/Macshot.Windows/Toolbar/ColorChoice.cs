using Macshot.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// A swatch that opens a colour picker when it is clicked, named beside itself or by
/// its tooltip.
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

    /// <param name="label">
    /// What the well is called. Shown beside the swatch unless <paramref name="named"/>
    /// says otherwise, and either way the tooltip and the name a screen reader announces.
    /// </param>
    /// <param name="transparency">
    /// Whether the picker offers an alpha channel. Off for the toolbar's own colours — a
    /// translucent toolbar over a screenshot is a toolbar the screenshot shows through,
    /// which is the one thing that bar must never be. On for a caption, whose default
    /// background is macshot's black at seven tenths.
    /// </param>
    /// <param name="named">
    /// Whether the label is drawn. False where the row already carries a shorter caption of
    /// its own — the caption panel writes "Aa" and "BG" beside its wells, because a row that
    /// spelled out "Text Color" and "Background" between the font and the alignment would be
    /// most of the row spent on two words.
    /// </param>
    public ColorChoice(string label, bool transparency = false, bool named = true)
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

        if (named)
        {
            face.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        }

        _picker = new ColorPicker { IsAlphaEnabled = transparency };
        _picker.ColorChanged += (_, args) => Show(args.NewColor);

        var button = new Button
        {
            Content = face,
            Flyout = new Flyout { Content = _picker },
        };

        if (!named)
        {
            // Only where the label is not drawn. Beside a well that already spells its name
            // out, a tooltip saying the same thing is a second copy of a visible string.
            ToolTipService.SetToolTip(button, AppFonts.Tip(label));
        }

        // The label is inside the button rather than being it, and a swatch beside a
        // TextBlock is not a name WinUI will infer — the automation tree showed all three
        // of these wells as an unnamed button. Whatever the caller passed, which for the
        // settings page is the English key its page-wide pass then reaches.
        AutomationProperties.SetName(button, label);
        Content = button;
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

    /// <summary>The colour chosen, with its alpha where the well offers one.</summary>
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
