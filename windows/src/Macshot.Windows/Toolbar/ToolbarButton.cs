using Macshot.Windows.Core.Annotations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// One square button on a toolbar strip.
/// </summary>
/// <remarks>
/// <para>
/// Not a <see cref="Button"/>. A themed Windows button brings its own background,
/// border, corner radius, focus ring and press animation, and the whole point here is
/// the look macshot has on macOS: a flat 32-point square that is transparent until the
/// pointer is over it, the accent colour while it is the tool in hand, and nothing else.
/// Restyling a Button down to that is more markup than drawing it.
/// </para>
/// <para>
/// The states are the four <c>ToolbarButtonView</c> draws: pressed, selected, hovered,
/// and plain, in that order of precedence.
/// </para>
/// </remarks>
internal sealed partial class ToolbarButton : UserControl
{
    private readonly Border _surface;
    private bool _isHovered;
    private bool _isPressed;

    public ToolbarButton(ToolbarItem item)
    {
        Item = item;

        _surface = new Border
        {
            Width = ToolbarPalette.ButtonSize,
            Height = ToolbarPalette.ButtonSize,
            CornerRadius = new CornerRadius(ToolbarPalette.ButtonRadius),

            // Transparent rather than unset: a Border with no background is not hit
            // testable, so the gaps between the icon's strokes would swallow the click.
            Background = ToolbarPalette.TransparentBrush,
            Child = Content(item),
        };

        Width = ToolbarPalette.ButtonSize;
        Height = ToolbarPalette.ButtonSize;
        base.Content = _surface;

        ToolTipService.SetToolTip(this, item.Tooltip);

        PointerEntered += (_, _) => { _isHovered = true; Repaint(); };
        PointerExited += (_, _) => { _isHovered = false; _isPressed = false; Repaint(); };
        PointerPressed += Surface_PointerPressed;
        PointerReleased += Surface_PointerReleased;
        PointerCaptureLost += (_, _) => { _isPressed = false; Repaint(); };
        RightTapped += (_, _) => Alternate?.Invoke(this, Item);

        Repaint();
    }

    /// <summary>Raised on a click.</summary>
    public event EventHandler<ToolbarItem>? Invoked;

    /// <summary>Raised on a right-click, for the buttons that offer a second choice.</summary>
    public event EventHandler<ToolbarItem>? Alternate;

    public ToolbarItem Item { get; private set; }

    /// <summary>
    /// Updates the button in place. Rebuilding the strip on every tool change would
    /// throw away the button under the pointer, and the hover would be lost with it.
    /// </summary>
    public void Update(ToolbarItem item)
    {
        var iconChanged = item.Command != Item.Command || item.Tool != Item.Tool;
        Item = item;

        if (iconChanged)
        {
            _surface.Child = Content(item);
            ToolTipService.SetToolTip(this, item.Tooltip);
        }

        Repaint();
    }

    /// <summary>The colour the swatch button shows, for the one button that is a colour.</summary>
    public void ShowSwatch(Color color)
    {
        if (Item.Command != ToolbarCommand.PickColor)
        {
            return;
        }

        _surface.Child = Swatch(color);
    }

    private void Surface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isPressed = true;
        Repaint();

        // Captured so a press that wanders off the button still ends here rather than
        // leaving it stuck looking pressed.
        CapturePointer(e.Pointer);
    }

    private void Surface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var wasPressed = _isPressed;
        _isPressed = false;
        ReleasePointerCapture(e.Pointer);
        Repaint();

        if (wasPressed && _isHovered)
        {
            Invoked?.Invoke(this, Item);
        }
    }

    private void Repaint()
    {
        _surface.Background = _isPressed
            ? ToolbarPalette.PressedBrush
            : Item.IsSelected
                ? ToolbarPalette.AccentBrush
                : _isHovered
                    ? ToolbarPalette.HoverBrush
                    : ToolbarPalette.TransparentBrush;
    }

    private static UIElement Content(ToolbarItem item) =>
        ToolbarIcons.For(item) ?? Swatch(ToolbarPalette.Icon);

    /// <summary>
    /// The colour button: the colour itself, inset, with a hairline border so a colour
    /// close to the strip's own is still a square rather than a hole in it.
    /// </summary>
    private static Border Swatch(Color color) => new()
    {
        Width = ToolbarPalette.ButtonSize - 12,
        Height = ToolbarPalette.ButtonSize - 12,
        CornerRadius = new CornerRadius(4),
        Background = new SolidColorBrush(color),
        BorderThickness = new Thickness(1),
        BorderBrush = ToolbarPalette.IconBrush(0.4),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
