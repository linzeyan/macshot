using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.UI;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// A row or a column of toolbar buttons on a dark rounded slab.
/// </summary>
/// <remarks>
/// Two of these make up macshot's toolbar: the tools run along the bottom of the
/// selection and the actions run down its side. One control for both, because they
/// differ only in which way they run — and because a second copy of the hover, press and
/// selection behaviour would be a second set of bugs.
/// </remarks>
internal sealed partial class ToolbarStrip : UserControl
{
    private readonly StackPanel _row;
    private readonly List<ToolbarButton> _buttons = [];

    public ToolbarStrip(Orientation orientation)
    {
        Orientation = orientation;

        _row = new StackPanel
        {
            Orientation = orientation,
            Spacing = ToolbarPalette.StripSpacing,
        };

        Content = new Border
        {
            Background = ToolbarPalette.BackgroundBrush,
            CornerRadius = new CornerRadius(ToolbarPalette.StripRadius),
            Padding = new Thickness(ToolbarPalette.StripPadding),
            Child = _row,
        };
    }

    /// <summary>Raised when a button is clicked.</summary>
    public event EventHandler<ToolbarItem>? ItemInvoked;

    /// <summary>Raised when a button is right-clicked.</summary>
    public event EventHandler<ToolbarItem>? ItemAlternate;

    public Orientation Orientation { get; }

    /// <summary>How many buttons it is carrying.</summary>
    public int Count => _buttons.Count;

    /// <summary>
    /// The size this strip will be once it is laid out, worked out from the button
    /// count rather than measured.
    /// </summary>
    /// <remarks>
    /// Where the strips go is decided before WinUI has arranged anything, so asking for
    /// a measured size here would give the size it had for the previous selection — and
    /// the strip would be one frame behind the region it is supposed to be anchored to.
    /// </remarks>
    public CaptureRegion Size => Orientation == Orientation.Horizontal
        ? new CaptureRegion(0, 0, ToolbarPalette.StripLength(Count), ToolbarPalette.StripThickness)
        : new CaptureRegion(0, 0, ToolbarPalette.StripThickness, ToolbarPalette.StripLength(Count));

    /// <summary>
    /// Puts <paramref name="items"/> on the strip, reusing the buttons already there.
    /// </summary>
    /// <remarks>
    /// Reused rather than rebuilt because the strip is refreshed on every tool change,
    /// and rebuilding would destroy the button under the pointer mid-hover — the click
    /// that follows would land on a control that no longer exists.
    /// </remarks>
    public void SetItems(IReadOnlyList<ToolbarItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        for (var index = 0; index < items.Count; index++)
        {
            if (index < _buttons.Count)
            {
                _buttons[index].Update(items[index]);
                continue;
            }

            var button = new ToolbarButton(items[index]);
            button.Invoked += (_, item) => ItemInvoked?.Invoke(this, item);
            // The button itself is the sender, because what answers a right-click is a
            // menu and a menu has to be anchored to something the user can see.
            button.Alternate += (source, item) => ItemAlternate?.Invoke(source, item);

            _buttons.Add(button);
            _row.Children.Add(button);
        }

        while (_buttons.Count > items.Count)
        {
            _row.Children.RemoveAt(_buttons.Count - 1);
            _buttons.RemoveAt(_buttons.Count - 1);
        }
    }

    /// <summary>
    /// The button carrying <paramref name="command"/>, for anchoring a flyout to it.
    /// Null when this strip does not carry that command.
    /// </summary>
    public FrameworkElement? ButtonFor(ToolbarCommand command) =>
        _buttons.FirstOrDefault(button => button.Item.Command == command);

    /// <summary>Shows a colour on the swatch button, if this strip has one.</summary>
    public void ShowSwatch(Color color)
    {
        foreach (var button in _buttons)
        {
            button.ShowSwatch(color);
        }
    }
}
