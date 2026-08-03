using Macshot.Windows.Core.Annotations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// Everything the stamp tool can place, in macshot's five groups with a tab each.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>EmojiPickerView</c>: a strip of category tabs, a hairline under it, and a
/// grid of the chosen category below (<c>UI/Popover/EmojiPickerView.swift</c>). The port
/// showed one flat <c>GridView</c> of twenty-two, which was both a fifth of what macshot
/// offers and a list rather than something to pick from.
/// </para>
/// <para>
/// Laid out by hand for the reason <see cref="BeautifySwatchGrid"/> is: a GridView's cells
/// are wider than an emoji and its selection visual is not the one wanted here — what marks
/// the chosen thing in this picker is the tab, not the glyph.
/// </para>
/// </remarks>
internal sealed class EmojiPickerView : StackPanel
{
    /// <summary>macshot's cell, and its eight to a row.</summary>
    private const int Cell = 30;

    private const int Columns = 8;

    private readonly List<Button> _tabs = [];

    private readonly Grid _grid = new();

    public EmojiPickerView()
    {
        RequestedTheme = ElementTheme.Dark;
        Spacing = 4;
        Padding = new Thickness(8);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        for (var index = 0; index < StampChoices.Categories.Count; index++)
        {
            var tab = Tab(StampChoices.Categories[index].Tab);
            var which = index;
            tab.Click += (_, _) => Show(which);

            _tabs.Add(tab);
            tabs.Children.Add(tab);
        }

        // macshot's half-point rule between the tabs and the grid, at the icon colour's
        // tenth — the same hairline the options row separates its groups with.
        var rule = new Border { Height = 1, Background = ToolbarPalette.IconBrush(0.1) };

        for (var column = 0; column < Columns; column++)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        Children.Add(tabs);
        Children.Add(rule);
        Children.Add(_grid);

        Show(0);
    }

    /// <summary>Raised with the emoji chosen.</summary>
    public event EventHandler<string>? Picked;

    /// <summary>
    /// Puts one category on show and marks its tab.
    /// </summary>
    private void Show(int category)
    {
        for (var index = 0; index < _tabs.Count; index++)
        {
            _tabs[index].Background = index == category
                ? ToolbarPalette.AccentBrush
                : ToolbarPalette.TransparentBrush;
        }

        var emoji = StampChoices.Categories[category].Emoji;
        var rows = (emoji.Count + Columns - 1) / Columns;

        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        for (var row = 0; row < rows; row++)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var index = 0; index < emoji.Count; index++)
        {
            var glyph = emoji[index];
            var cell = Cellular(glyph);
            cell.Click += (_, _) => Picked?.Invoke(this, glyph);

            Grid.SetRow(cell, index / Columns);
            Grid.SetColumn(cell, index % Columns);
            _grid.Children.Add(cell);
        }
    }

    private static Button Tab(string glyph) => Bare(glyph, 16, 34, 30);

    private static Button Cellular(string glyph) => Bare(glyph, 18, Cell, Cell);

    /// <summary>
    /// A button with nothing round it but its glyph — WinUI's own chrome would put a border
    /// and a fill behind every one of a hundred emoji.
    /// </summary>
    private static Button Bare(string glyph, double size, double width, double height) => new()
    {
        Content = new TextBlock
        {
            Text = glyph,
            FontSize = size,

            // Named, so the colour glyph is what gets drawn rather than whatever the app's
            // own face happens to have for these code points.
            FontFamily = new FontFamily("Segoe UI Emoji"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
        Width = width,
        Height = height,
        MinWidth = 0,
        MinHeight = 0,
        Padding = new Thickness(0),
        CornerRadius = new CornerRadius(5),
        BorderThickness = new Thickness(0),
        Background = ToolbarPalette.TransparentBrush,
    };
}
