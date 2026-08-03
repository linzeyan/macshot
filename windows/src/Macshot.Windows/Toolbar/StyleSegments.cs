using Macshot.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Macshot.Windows.Toolbar;

/// <summary>One choice on a <see cref="StyleSegments"/> row.</summary>
/// <param name="Face">The mark this choice produces, drawn. Null for a worded choice.</param>
/// <param name="Label">The word, for a choice with no mark to draw.</param>
/// <param name="Width">How wide the segment is, or 0 to take the width of its content.</param>
internal readonly record struct StyleSegment(FrameworkElement? Face, string? Label, double Width);

/// <summary>
/// A row of mutually exclusive choices, each showing the mark it produces.
/// </summary>
/// <remarks>
/// <para>
/// macshot uses an <c>NSSegmentedControl</c> carrying drawn images for the line, arrow
/// and fill styles — you pick the picture of what you are about to draw. WinUI has no
/// segmented control, and the two obvious substitutes are both worse: a
/// <c>ComboBox</c> spells the style as an enum name and costs a second click, and
/// <c>RadioButtons</c> is a form control with a dot beside each label.
/// </para>
/// <para>
/// So it is written: a row of borders at macshot's own metrics — 22 tall, the selected
/// one filled with the toolbar accent — sharing the toolbar's palette so the whole row
/// follows a colour change with everything else.
/// </para>
/// </remarks>
internal sealed class StyleSegments : UserControl
{
    /// <summary>macshot's segmented control height — <c>ToolOptionsRowView.swift:447</c>.</summary>
    public const double Height22 = 22;

    private const double Radius = 5;

    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly List<Border> _segments = [];

    private int _selectedIndex = -1;

    public StyleSegments()
    {
        // The track is held rather than inherited: WinUI seals Border, so a control that
        // *is* a rounded slab is not a thing that can be written. A UserControl around
        // one draws identically and keeps the corner radius honest, where a bare
        // ContentControl would depend on whatever its default template happens to bind.
        Content = new Border
        {
            Height = Height22,
            CornerRadius = new CornerRadius(Radius),

            // The unselected track: the icon colour at a tenth, which is what separates
            // the row from the slab behind it without drawing a border around every
            // choice.
            Background = ToolbarPalette.IconBrush(0.1),
            Child = _row,
        };

        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>Raised when a choice is picked, with its index. Not raised by code.</summary>
    public event EventHandler<int>? SelectionChanged;

    /// <summary>Which choice is showing as picked, or -1 before there is one.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value)
            {
                return;
            }

            _selectedIndex = value;
            Repaint();
        }
    }

    /// <summary>Replaces the choices. The selection is kept if it is still in range.</summary>
    public void SetSegments(IReadOnlyList<StyleSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        _row.Children.Clear();
        _segments.Clear();

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var content = segment.Face ?? new TextBlock
            {
                Text = segment.Label ?? string.Empty,
                FontSize = 10,
                FontWeight = AppFonts.Heavier(Microsoft.UI.Text.FontWeights.Medium),
                Foreground = ToolbarPalette.IconBrush(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var button = new Border
            {
                Height = Height22,
                CornerRadius = new CornerRadius(Radius),
                Background = ToolbarPalette.TransparentBrush,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,

                // A worded choice pads itself; a drawn one is given macshot's own width.
                Padding = segment.Width > 0 ? new Thickness(0) : new Thickness(9, 0, 9, 0),
                Child = new Grid { Children = { content } },
            };

            if (segment.Width > 0)
            {
                button.Width = segment.Width;
            }

            var picked = index;
            button.PointerPressed += (_, args) =>
            {
                args.Handled = true;
                if (_selectedIndex == picked)
                {
                    return;
                }

                SelectedIndex = picked;
                SelectionChanged?.Invoke(this, picked);
            };

            button.PointerEntered += (_, _) =>
            {
                if (_selectedIndex != picked)
                {
                    button.Background = ToolbarPalette.HoverBrush;
                }
            };

            button.PointerExited += (_, _) =>
            {
                if (_selectedIndex != picked)
                {
                    button.Background = ToolbarPalette.TransparentBrush;
                }
            };

            _segments.Add(button);
            _row.Children.Add(button);
        }

        Repaint();
    }

    /// <summary>
    /// Turns a choice on or off. Used where one style rules another out — a dashed
    /// outline on a shape that is also filled has nothing to draw the dashes on.
    /// </summary>
    public void SetSegmentEnabled(int index, bool enabled)
    {
        if (index < 0 || index >= _segments.Count)
        {
            return;
        }

        var segment = _segments[index];
        segment.Opacity = enabled ? 1 : 0.35;
        segment.IsHitTestVisible = enabled;
    }

    private void Repaint()
    {
        for (var index = 0; index < _segments.Count; index++)
        {
            _segments[index].Background = index == _selectedIndex
                ? ToolbarPalette.AccentBrush
                : ToolbarPalette.TransparentBrush;
        }
    }
}
