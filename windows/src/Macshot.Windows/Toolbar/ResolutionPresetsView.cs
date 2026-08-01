using Macshot.Windows.Core.Capture;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Macshot.Windows.Services.Localization;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// What the presets button opens: the shapes in one column, the exact sizes in the other,
/// and underneath them the two things that outlive this capture.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>ResolutionPresetsView</c>, at its numbers — two 140 columns either side of
/// a 1 px rule, 26-tall rows under a 22 header, 8 of padding, and a 78 footer behind a
/// second rule. Two columns rather than one list because the two do different things: one
/// holds a shape while the region is dragged, the other sets a size once.
/// </para>
/// <para>
/// The footer is the part that was missing here. Keep-ratio decides whether the shape
/// picked survives into the <em>next</em> capture, which is the difference between
/// choosing 16 : 9 for this screenshot and working in 16 : 9; and the unit decides whether
/// the box reads in device pixels or in the layout points macOS calls them. Both are
/// preferences rather than properties of the region, which is why they are stored and the
/// shape is not.
/// </para>
/// </remarks>
internal sealed partial class ResolutionPresetsView : UserControl
{
    private const double ColumnWidth = 140;
    private const double RowHeight = 26;
    private const double HeaderHeight = 22;
    private const double VerticalPad = 8;
    private const double FooterHeight = 78;
    private const double RowIndent = 14;

    /// <summary>The footer's two labels — <c>ResolutionPresetsView.swift:96, 115</c>.</summary>
    private const double FooterFontSize = 11;

    private const double HeaderFontSize = 10;

    private const double RowFontSize = 12;

    private readonly StackPanel _ratios = new();
    private readonly StackPanel _sizes = new();
    /// <summary>
    /// The switch, with its labels emptied rather than left at On and Off: macshot's is a
    /// bare <c>NSSwitch</c>, and the words would double the footer's width for something
    /// the label to its left already says.
    /// </summary>
    private readonly ToggleSwitch _keepRatio = new()
    {
        OnContent = string.Empty,
        OffContent = string.Empty,
        MinWidth = 0,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly StyleSegments _units = new();

    public ResolutionPresetsView()
    {
        _units.SetSegments([new StyleSegment(null, "px", 40), new StyleSegment(null, "pt", 40)]);
        _units.SelectionChanged += (_, index) => UnitPicked?.Invoke(this, index == 1);
        _keepRatio.Toggled += (_, _) => KeepRatioToggled?.Invoke(this, _keepRatio.IsOn);

        var columns = new Grid { ColumnDefinitions = { Fixed(), Fixed(1), Fixed() } };
        Add(columns, _ratios, 0, 0);
        Add(columns, Rule(vertical: true), 0, 1);
        Add(columns, _sizes, 0, 2);

        var footer = new Grid
        {
            Height = FooterHeight,
            RowDefinitions = { new RowDefinition(), new RowDefinition() },
            ColumnDefinitions = { new ColumnDefinition(), Fixed(80) },
            Padding = new Thickness(RowIndent, 8, 12, 8),
        };

        Add(footer, Label(L("Keep ratio for next captures")), 0, 0);
        Add(footer, _keepRatio, 0, 1);
        Add(footer, Label(L("Units")), 1, 0);
        Add(footer, _units, 1, 1);

        var rule = Rule(vertical: false);
        rule.Margin = new Thickness(12, 0, 12, 0);

        var body = new StackPanel { Padding = new Thickness(0, VerticalPad, 0, VerticalPad) };
        body.Children.Add(columns);
        body.Children.Add(rule);
        body.Children.Add(footer);

        Content = new Border { RequestedTheme = ElementTheme.Dark, Child = body };
    }

    /// <summary>Raised when a shape or a size is chosen.</summary>
    public event EventHandler<ResolutionPreset>? PresetPicked;

    /// <summary>Raised when the keep-ratio switch is moved, with its new state.</summary>
    public event EventHandler<bool>? KeepRatioToggled;

    /// <summary>Raised when the unit is changed. True for points, false for pixels.</summary>
    public event EventHandler<bool>? UnitPicked;

    /// <summary>
    /// Fills both columns and the footer from the state of the capture underneath.
    /// </summary>
    /// <param name="lockedAspect">
    /// The shape being held, which is what carries the tick. Null is Freeform, and Freeform
    /// is a row rather than the absence of one — a list where nothing is ticked reads as a
    /// list that has not loaded.
    /// </param>
    public void Show(double? lockedAspect, double width, double height, bool keepRatio, bool points)
    {
        _ratios.Children.Clear();
        _sizes.Children.Clear();

        _ratios.Children.Add(Header(L("Aspect ratio")));
        foreach (var preset in ResolutionPresets.Ratios)
        {
            _ratios.Children.Add(Row(preset, Holds(preset.Aspect, lockedAspect)));
        }

        _sizes.Children.Add(Header(L("Resolution")));
        foreach (var preset in ResolutionPresets.Sizes)
        {
            var already = Math.Abs(preset.Width - width) < 0.5 && Math.Abs(preset.Height - height) < 0.5;
            _sizes.Children.Add(Row(preset, already));
        }

        _keepRatio.IsOn = keepRatio;
        _units.SelectedIndex = points ? 1 : 0;
    }

    /// <summary>Whether a preset's shape is the one being held, to within a rounding.</summary>
    /// <remarks>
    /// Compared with a tolerance because an aspect is a division: 1920 / 1080 and 16 / 9
    /// are the same shape and not the same double.
    /// </remarks>
    private static bool Holds(double? preset, double? locked) => (preset, locked) switch
    {
        (null, null) => true,
        (double a, double b) => Math.Abs(a - b) < 0.001,
        _ => false,
    };

    private static ColumnDefinition Fixed(double width = ColumnWidth) =>
        new() { Width = new GridLength(width) };

    private static void Add(Grid grid, FrameworkElement child, int row, int column)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = FooterFontSize,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = ToolbarPalette.IconBrush(1),
    };

    private static TextBlock Header(string text) => new()
    {
        // Capitals in code rather than in the string, so the translation is looked up by
        // the words macshot ships and not by a shouted version of them.
        Text = text.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
        Height = HeaderHeight,
        Padding = new Thickness(RowIndent, 0, 2, 0),
        FontSize = HeaderFontSize,
        FontWeight = FontWeights.SemiBold,
        Foreground = ToolbarPalette.IconBrush(0.5),
    };

    private static Border Rule(bool vertical) => new()
    {
        Width = vertical ? 1 : double.NaN,
        Height = vertical ? double.NaN : 1,
        HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
        Background = ToolbarPalette.IconBrush(0.12),
    };

    private Button Row(ResolutionPreset preset, bool selected)
    {
        var line = new Grid { ColumnDefinitions = { Fixed(RowIndent), new ColumnDefinition() } };

        Add(line, new TextBlock
        {
            Text = selected ? "✓" : string.Empty,
            FontSize = RowFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ToolbarPalette.AccentBrush,
        }, 0, 0);

        Add(line, new TextBlock
        {
            Text = preset.Label,
            FontSize = RowFontSize,
            VerticalAlignment = VerticalAlignment.Center,
        }, 0, 1);

        var row = new Button
        {
            Content = line,
            Width = ColumnWidth,
            Height = RowHeight,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = ToolbarPalette.TransparentBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
        };

        row.Click += (_, _) => PresetPicked?.Invoke(this, preset);
        return row;
    }
}
