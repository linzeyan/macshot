using Macshot.Windows.Core.Capture;
using Macshot.Windows.Services;
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

    /// <summary>The footer's padding above and below its rows.</summary>
    private const double FooterPad = 8;

    /// <summary>
    /// One row of the footer: macshot's 78-tall footer less its padding, halved. Written
    /// this way round because the panel is offered with one row as well as two — the
    /// pre-selection button has no size to report, so it has no unit to choose
    /// (<c>OverlayView.swift:2809</c>) — and a hard 78 would stretch a single row to fill it.
    /// </summary>
    private const double FooterRow = (78 - (FooterPad * 2)) / 2;

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

    /// <summary>Held so the unit row can be taken out of the footer and it can shrink.</summary>
    private readonly Grid _footer;
    private readonly RowDefinition _unitsRow = new() { Height = new GridLength(FooterRow) };
    private readonly TextBlock _unitsLabel;

    public ResolutionPresetsView()
    {
        _units.SetSegments([new StyleSegment(null, "px", 40), new StyleSegment(null, "pt", 40)]);
        _units.SelectionChanged += (_, index) => UnitPicked?.Invoke(this, index == 1);
        _keepRatio.Toggled += (_, _) => KeepRatioToggled?.Invoke(this, _keepRatio.IsOn);
        _unitsLabel = Label(L("Units"));

        var columns = new Grid { ColumnDefinitions = { Fixed(), Fixed(1), Fixed() } };
        Add(columns, _ratios, 0, 0);
        Add(columns, Rule(vertical: true), 0, 1);
        Add(columns, _sizes, 0, 2);

        _footer = new Grid
        {
            RowDefinitions = { new RowDefinition { Height = new GridLength(FooterRow) }, _unitsRow },
            ColumnDefinitions = { new ColumnDefinition(), Fixed(80) },
            Padding = new Thickness(RowIndent, FooterPad, 12, FooterPad),
        };

        Add(_footer, Label(L("Keep ratio for next captures")), 0, 0);
        Add(_footer, _keepRatio, 0, 1);
        Add(_footer, _unitsLabel, 1, 0);
        Add(_footer, _units, 1, 1);

        var rule = Rule(vertical: false);
        rule.Margin = new Thickness(12, 0, 12, 0);

        var body = new StackPanel { Padding = new Thickness(0, VerticalPad, 0, VerticalPad) };
        body.Children.Add(columns);
        body.Children.Add(rule);
        body.Children.Add(_footer);

        Content = new Border { RequestedTheme = ElementTheme.Dark, Child = body };
    }

    /// <summary>
    /// Whether the panel offers the unit choice. Off for the pre-selection button, which is
    /// opened when there is no size on screen for a unit to be about.
    /// </summary>
    /// <remarks>
    /// Read by <see cref="Show"/> rather than applied when it is set, because the panel is
    /// filled from scratch every time the flyout opens and nothing else about it survives
    /// between two openings either.
    /// </remarks>
    public bool ShowsUnits { get; set; } = true;

    /// <summary>Raised when a shape or a size is chosen.</summary>
    public event EventHandler<ResolutionPreset>? PresetPicked;

    /// <summary>Raised when the keep-ratio switch is moved, with its new state.</summary>
    public event EventHandler<bool>? KeepRatioToggled;

    /// <summary>Raised when the unit is changed. True for points, false for pixels.</summary>
    public event EventHandler<bool>? UnitPicked;

    /// <summary>
    /// Fills both columns and the footer from the state of the capture underneath.
    /// </summary>
    /// <param name="active">
    /// The shape or size being held, which is what carries the tick — one row across both
    /// columns. Freeform is a row rather than the absence of one: a list where nothing is
    /// ticked reads as a list that has not loaded.
    /// </param>
    public void Show(PreSelectionPreset active, bool keepRatio, bool points)
    {
        _ratios.Children.Clear();
        _sizes.Children.Clear();

        _ratios.Children.Add(Header(L("Aspect ratio")));
        foreach (var preset in ResolutionPresets.Ratios)
        {
            _ratios.Children.Add(Row(preset, active.Selects(preset)));
        }

        _sizes.Children.Add(Header(L("Resolution")));
        foreach (var preset in ResolutionPresets.Sizes)
        {
            _sizes.Children.Add(Row(preset, active.Selects(preset)));
        }

        _keepRatio.IsOn = keepRatio;
        _units.SelectedIndex = points ? 1 : 0;

        // The row is collapsed to nothing rather than merely hidden: a Grid row keeps its
        // height whatever its children do, so leaving it there would put a band of empty
        // footer under the switch where macshot has none.
        var visibility = ShowsUnits ? Visibility.Visible : Visibility.Collapsed;
        _unitsLabel.Visibility = visibility;
        _units.Visibility = visibility;
        _unitsRow.Height = new GridLength(ShowsUnits ? FooterRow : 0);
        _footer.Height = (FooterPad * 2) + (ShowsUnits ? FooterRow * 2 : FooterRow);
    }

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
        FontWeight = AppFonts.Heavier(text, FontWeights.SemiBold),
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

        // Localized here rather than by the page-wide pass, which has long finished by the
        // time a flyout fills itself. Only Freeform is a word — the ratios and the sizes are
        // numbers and come back unchanged — and it was the one row left in English.
        Add(line, new TextBlock
        {
            Text = L(preset.Label),
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
