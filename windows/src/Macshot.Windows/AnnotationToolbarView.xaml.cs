using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Rendering;
using Macshot.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Color resolves to
// Macshot.Color and does not compile.
using Windows.UI;

namespace Macshot.Windows;

/// <summary>
/// The annotation toolbar: which tool is active, undo and redo, the style every new
/// mark is drawn in, and the two recognition actions.
/// </summary>
/// <remarks>
/// <para>
/// A control rather than markup inside one window, because two places need exactly
/// this bar — the capture overlay and the editor window — and a copy of it would drift
/// the moment either grew a tool. It owns the editor's <see cref="AnnotationEditor.Tool"/>
/// and <see cref="AnnotationEditor.Style"/> and nothing else; the host owns the pixels,
/// the pointer, and what Done means.
/// </para>
/// <para>
/// The host is told to redraw through <see cref="Changed"/> rather than being called
/// back for each kind of change. Every one of them has the same consequence — the
/// annotations on screen are no longer what the document says — so distinguishing them
/// would only give the host something else to get wrong.
/// </para>
/// </remarks>
public sealed partial class AnnotationToolbarView : UserControl
{
    /// <summary>
    /// The box every tool icon is drawn in. Small enough that a dozen buttons stay a
    /// strip rather than a row of tiles.
    /// </summary>
    private const double IconExtent = 16;

    private readonly Dictionary<AnnotationTool, ToggleButton> _toolButtons = [];

    private AnnotationEditor? _editor;
    private SettingsStore? _settings;

    /// <summary>The style the toolbar started from, so only a real change is written back.</summary>
    private AnnotationStyle _loadedStyle = AnnotationStyle.Default;

    private bool _isLoadingStyle;

    public AnnotationToolbarView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when what is on the canvas no longer matches the document.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised when the colour sampler is armed or disarmed from the toolbar. The host
    /// answers it because the pixels a sample comes from are the host's: the whole
    /// screenshot under an overlay, the image being edited in an editor.
    /// </summary>
    public event EventHandler<bool>? ColorSamplingToggled;

    /// <summary>
    /// Raised by Undo and Redo, which the host performs rather than this control.
    /// </summary>
    /// <remarks>
    /// The document is not the only thing a host can undo. The editor window can crop,
    /// flip and frame the image itself, and those steps belong on the same timeline as
    /// the marks — so what one press means is the host's to decide, or the button and
    /// Ctrl+Z would do different things.
    /// </remarks>
    public event EventHandler? UndoRequested;

    public event EventHandler? RedoRequested;

    public event EventHandler? ReadTextRequested;

    public event EventHandler? RedactRequested;

    /// <summary>Raised by Done, which means whatever finishing means where this is used.</summary>
    public event EventHandler? DoneRequested;

    /// <summary>The emoji the stamp tool places.</summary>
    public string StampEmoji { get; private set; } = StampGlyph.Default;

    /// <summary>
    /// Whether the sampler is armed, which makes the next click on the host a pick rather
    /// than a mark. The button's own state is the answer, so there is no second copy of it
    /// to disagree.
    /// </summary>
    public bool IsSamplingColor => PickColorButton.IsChecked == true;

    /// <summary>
    /// The host's own buttons, shown between the shared actions and Done. Set from code
    /// rather than markup so a host that adds none pays nothing.
    /// </summary>
    public UIElement? Actions
    {
        get => ActionsSlot.Content as UIElement;
        set => ActionsSlot.Content = value;
    }

    /// <summary>
    /// Attaches the toolbar to an editor and the settings its style is remembered in.
    /// </summary>
    /// <remarks>
    /// Called by the host after construction rather than done in the constructor, and
    /// this is where the change handlers are attached too. Assigning a slider's value
    /// during XAML parsing raises ValueChanged while the rest of the bar does not exist
    /// yet, and WinUI reports the null reference that follows as a failure to set the
    /// property it was in the middle of setting — naming neither the handler nor the
    /// control it tripped over.
    /// </remarks>
    public void Bind(AnnotationEditor editor, SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(settings);

        _editor = editor;
        _settings = settings;

        BuildToolButtons();
        LoadStyle();
        ShowOptionsFor(editor.Tool);

        // Read here rather than by the editor itself, so Core stays free of the settings
        // file and this stays the one place the toolbar's state comes from.
        editor.SmoothStrokes = settings.Current.SmoothPencilStrokes;

        StyleColorPicker.ColorChanged += StyleColor_Changed;
        StrokeWidthSlider.ValueChanged += StrokeWidth_Changed;
        LineStyleBox.SelectionChanged += LineStyle_Changed;
        ArrowStyleBox.SelectionChanged += ArrowStyle_Changed;
        CornerRadiusSlider.ValueChanged += CornerRadius_Changed;
        StampChoices.SelectionChanged += StampChoice_Changed;
    }

    /// <summary>
    /// Shows whether the sampler is armed. Called by the host as well as from the
    /// button, because Escape and choosing a tool both give up a pick.
    /// </summary>
    public void SetColorSampling(bool armed) => PickColorButton.IsChecked = armed;

    /// <summary>
    /// Puts a sampled colour on the toolbar, keeping the opacity already chosen.
    /// </summary>
    /// <remarks>
    /// Applied through the colour picker rather than straight onto the editor's style,
    /// so the swatch, the picker, and what the next mark is drawn in cannot disagree —
    /// the picker's change handler is the one path that keeps all three together.
    /// Opacity is a property of the mark rather than of the pixel, and the pixel has no
    /// opinion about it: a screenshot is opaque everywhere.
    /// </remarks>
    public void ApplySampledColor(AnnotationColor sampled) => StyleColorPicker.Color = Color.FromArgb(
        StyleColorPicker.Color.A,
        sampled.Red,
        sampled.Green,
        sampled.Blue);

    /// <summary>
    /// Remembers the style for next time. A failure is swallowed on purpose: this runs
    /// while the host is being torn down, there is no window left to report into, and
    /// the cost of losing it is that the next capture starts from the previous colour.
    /// </summary>
    public void PersistStyle()
    {
        if (_editor is not { } editor || _settings is not { } settings || editor.Style == _loadedStyle)
        {
            return;
        }

        try
        {
            settings.Save(settings.Current.WithAnnotationStyle(editor.Style));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Makes <paramref name="tool"/> the active one, as clicking its button would.</summary>
    private void SelectTool(AnnotationTool tool)
    {
        if (_editor is not { } editor)
        {
            return;
        }

        // Reaching for a tool is as clear a way of abandoning a pick as Escape is.
        if (PickColorButton.IsChecked == true)
        {
            PickColorButton.IsChecked = false;
            ColorSamplingToggled?.Invoke(this, false);
        }

        editor.Tool = tool;

        // Behaves as a radio group: a tool is always active, so re-clicking the current
        // tool must not leave the toolbar with nothing selected.
        foreach (var (candidate, button) in _toolButtons)
        {
            button.IsChecked = candidate == tool;
        }

        ShowOptionsFor(tool);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shows the style controls this tool actually reads, and hides the rest.
    /// </summary>
    /// <remarks>
    /// A dash pattern on a pixelated block and a colour on a blur are controls that do
    /// nothing, and a row full of those teaches the user that none of them mean anything.
    /// <see cref="AnnotationToolOptions"/> answers because the answers are facts about
    /// the rasterizer, not about this row.
    /// </remarks>
    private void ShowOptionsFor(AnnotationTool tool)
    {
        ColorButton.Visibility = Show(AnnotationToolOptions.UsesColor(tool));

        // The sampler follows the colour: it exists to set the swatch beside it, and
        // offering it while that swatch is hidden would be offering to change something
        // the tool in hand ignores.
        PickColorButton.Visibility = ColorButton.Visibility;

        SizeLabel.Visibility = Show(AnnotationToolOptions.UsesSize(tool));
        StrokeWidthSlider.Visibility = SizeLabel.Visibility;
        SizeLabel.Text = SizeLabelFor(tool);

        LineStyleBox.Visibility = Show(AnnotationToolOptions.UsesLineStyle(tool));
        ArrowStyleBox.Visibility = Show(AnnotationToolOptions.UsesArrowStyle(tool));

        var rounds = Show(AnnotationToolOptions.UsesCornerRadius(tool));
        CornerLabel.Visibility = rounds;
        CornerRadiusSlider.Visibility = rounds;
        StampButton.Visibility = Show(AnnotationToolOptions.UsesStamp(tool));
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// What the size slider is called for this tool. One slider, three jobs — a stroke
    /// width, the size of a glyph, the coarseness of an effect — and a label that says
    /// "Width" over a blur radius is worse than no label.
    /// </summary>
    private static string SizeLabelFor(AnnotationTool tool) => AnnotationToolOptions.SizeMeaning(tool) switch
    {
        AnnotationSizeMeaning.Extent => "Size",
        AnnotationSizeMeaning.Strength => "Strength",
        _ => "Width",
    };

    private void BuildToolButtons()
    {
        foreach (var tool in ToolOrder)
        {
            var button = new ToggleButton
            {
                Content = ToolIcon(tool),
                Tag = tool,
                IsChecked = tool == _editor?.Tool,

                // Square and tight, so a dozen of them are a compact strip rather than
                // a sentence. The default button padding is sized for words.
                MinWidth = 0,
                Padding = new Thickness(8),
            };

            // The name has to remain reachable: a picture is faster once known and
            // useless before that, and hover is where the answer belongs.
            ToolTipService.SetToolTip(button, Label(tool));
            button.Click += ToolButton_Click;
            _toolButtons[tool] = button;
            ToolButtons.Children.Add(button);
        }

        StampChoices.ItemsSource = StampGlyph.Choices;
        StampButton.Content = StampEmoji;
    }

    /// <summary>
    /// Fills the style controls from the remembered style. The flag keeps the change
    /// handlers from writing a half-initialized style back while this runs: setting the
    /// colour before the slider has a value would otherwise commit a stroke width of
    /// zero.
    /// </summary>
    private void LoadStyle()
    {
        if (_editor is not { } editor || _settings is not { } settings)
        {
            return;
        }

        _isLoadingStyle = true;
        try
        {
            _loadedStyle = settings.Current.ToAnnotationStyle();
            editor.Style = _loadedStyle;

            LineStyleBox.ItemsSource = Enum.GetValues<LineStyle>().Select(style => style.ToString()).ToList();
            LineStyleBox.SelectedIndex = (int)_loadedStyle.LineStyle;
            ArrowStyleBox.ItemsSource = Enum.GetValues<ArrowStyle>().Select(style => style.ToString()).ToList();
            ArrowStyleBox.SelectedIndex = (int)_loadedStyle.ArrowStyle;
            CornerRadiusSlider.Value = _loadedStyle.CornerRadius;
            StrokeWidthSlider.Value = _loadedStyle.StrokeWidth;
            StyleColorPicker.Color = ToUiColor(_loadedStyle.Color);
        }
        finally
        {
            _isLoadingStyle = false;
        }

        UpdateColorSwatch();
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => UndoRequested?.Invoke(this, EventArgs.Empty);

    private void Redo_Click(object sender, RoutedEventArgs e) => RedoRequested?.Invoke(this, EventArgs.Empty);

    private void ReadText_Click(object sender, RoutedEventArgs e) =>
        ReadTextRequested?.Invoke(this, EventArgs.Empty);

    private void RedactPii_Click(object sender, RoutedEventArgs e) =>
        RedactRequested?.Invoke(this, EventArgs.Empty);

    private void Confirm_Click(object sender, RoutedEventArgs e) =>
        DoneRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Arms or disarms the colour sampler.
    /// </summary>
    /// <remarks>
    /// Armed rather than made a tool of its own. Sampling is something done in the
    /// middle of drawing — the whole reason to take a colour off the screen is to draw
    /// the next mark in it — so it borrows one click and hands the tool back, instead of
    /// making the user reselect the tool they were already using.
    /// </remarks>
    private void PickColor_Click(object sender, RoutedEventArgs e) =>
        ColorSamplingToggled?.Invoke(this, PickColorButton.IsChecked == true);

    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: AnnotationTool tool })
        {
            SelectTool(tool);
        }
    }

    private void StampChoice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (StampChoices.SelectedItem is not string emoji)
        {
            return;
        }

        StampEmoji = emoji;
        StampButton.Content = emoji;
        StampButton.Flyout?.Hide();

        // Picking a stamp is asking to stamp: leaving the previous tool active would
        // make the choice look like it did nothing.
        if (_toolButtons.ContainsKey(AnnotationTool.Stamp))
        {
            SelectTool(AnnotationTool.Stamp);
        }
    }

    private void StyleColor_Changed(ColorPicker sender, ColorChangedEventArgs args) => ApplyStyle();

    private void StrokeWidth_Changed(object sender, RangeBaseValueChangedEventArgs e) => ApplyStyle();

    private void LineStyle_Changed(object sender, SelectionChangedEventArgs e) => ApplyStyle();

    private void ArrowStyle_Changed(object sender, SelectionChangedEventArgs e) => ApplyStyle();

    private void CornerRadius_Changed(object sender, RangeBaseValueChangedEventArgs e) => ApplyStyle();

    /// <summary>
    /// The style applies to marks drawn from now on. Restyling what is already on the
    /// canvas would need a selection, which is a separate feature.
    /// </summary>
    private void ApplyStyle()
    {
        // Every control this reads, not merely the first two. The handlers are attached
        // after the toolbar is built, so none of these should be null; checking all of
        // them is what keeps a style change from taking the host down with it if one
        // ever is.
        if (_isLoadingStyle
            || _editor is not { } editor
            || StyleColorPicker is null
            || StrokeWidthSlider is null
            || LineStyleBox is null
            || ArrowStyleBox is null
            || CornerRadiusSlider is null)
        {
            return;
        }

        var color = StyleColorPicker.Color;
        editor.Style = new AnnotationStyle(
            new AnnotationColor(color.R, color.G, color.B, color.A),
            Math.Max(1, StrokeWidthSlider.Value),
            LineStyleBox.SelectedIndex >= 0 ? (LineStyle)LineStyleBox.SelectedIndex : LineStyle.Solid,
            ArrowStyle: ArrowStyleBox.SelectedIndex >= 0
                ? (ArrowStyle)ArrowStyleBox.SelectedIndex
                : ArrowStyle.Filled,
            CornerRadius: Math.Max(0, CornerRadiusSlider.Value));
        UpdateColorSwatch();
    }

    private void UpdateColorSwatch()
    {
        if (_editor is { } editor)
        {
            ColorSwatch.Background = new SolidColorBrush(ToUiColor(editor.Style.Color));
        }
    }

    private static Color ToUiColor(AnnotationColor color) =>
        new() { A = color.Alpha, R = color.Red, G = color.Green, B = color.Blue };

    /// <summary>
    /// The tools the strip offers, in the order they appear.
    /// </summary>
    /// <remarks>
    /// The pointer first, then everything the rasterizer can draw. Select is added here
    /// rather than to <see cref="AnnotationRasterizer.SupportedTools"/> because that list
    /// is what the rasterizer draws, and selecting draws nothing — it reshapes marks that
    /// are already there.
    /// </remarks>
    private static IReadOnlyList<AnnotationTool> ToolOrder { get; } =
        [AnnotationTool.Select, .. AnnotationRasterizer.SupportedTools];

    private static string Label(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Select => "Select",
        AnnotationTool.Arrow => "Arrow",
        AnnotationTool.Rectangle => "Box",
        AnnotationTool.Ellipse => "Ellipse",
        AnnotationTool.Line => "Line",
        AnnotationTool.Pencil => "Pen",
        AnnotationTool.Marker => "Marker",
        AnnotationTool.FilledRectangle => "Redact",
        AnnotationTool.Pixelate => "Pixelate",
        AnnotationTool.Blur => "Blur",
        AnnotationTool.Measure => "Measure",
        AnnotationTool.Loupe => "Magnifier",
        _ => tool.ToString(),
    };

    /// <summary>The icon for a tool: the mark it makes, drawn small.</summary>
    /// <remarks>
    /// <para>
    /// Shapes rather than words. A row of word buttons is wide, slow to scan, and tells
    /// a beginner nothing they did not already know from the word; a tool showing its
    /// own shape needs no legend. The word survives as the tooltip, which is where a
    /// name belongs once the picture carries the meaning.
    /// </para>
    /// <para>
    /// Built from <c>Line</c>, <c>Rectangle</c> and <c>Ellipse</c> rather than icon-font
    /// codepoints. A codepoint written without a Windows to look at renders as an empty
    /// box when it is wrong, which is worse than the word it replaced — and half of
    /// these have no glyph in the icon font anyway.
    /// </para>
    /// </remarks>
    private static FrameworkElement ToolIcon(AnnotationTool tool)
    {
        var canvas = new Canvas { Width = IconExtent, Height = IconExtent };

        switch (tool)
        {
        case AnnotationTool.Line:
            canvas.Children.Add(Stroke(2, 14, 14, 2));
            break;

        case AnnotationTool.Arrow:
            canvas.Children.Add(Stroke(2, 14, 14, 2));
            canvas.Children.Add(Stroke(14, 2, 8.5, 2.5));
            canvas.Children.Add(Stroke(14, 2, 13.5, 7.5));
            break;

        case AnnotationTool.Pencil:
            // A zigzag rather than a straight line: what separates this from the line
            // tool is that the stroke follows the hand.
            canvas.Children.Add(Stroke(2, 12, 6, 4));
            canvas.Children.Add(Stroke(6, 4, 10, 12));
            canvas.Children.Add(Stroke(10, 12, 14, 5));
            break;

        case AnnotationTool.Marker:
            // Wide and translucent, which is the whole difference from the pencil.
            canvas.Children.Add(Stroke(2, 13, 14, 3, thickness: 5, opacity: 0.55));
            break;

        case AnnotationTool.Rectangle:
            canvas.Children.Add(Box(filled: false));
            break;

        case AnnotationTool.FilledRectangle:
            canvas.Children.Add(Box(filled: true));
            break;

        case AnnotationTool.Ellipse:
            canvas.Children.Add(new Ellipse
            {
                Width = 13,
                Height = 10,
                Stroke = IconBrush(1),
                StrokeThickness = 1.6,
                Margin = new Thickness(1.5, 3, 0, 0),
            });
            break;

        case AnnotationTool.Pixelate:
            // Four blocks in a checker, which is what the effect looks like at the size
            // anyone actually notices it.
            canvas.Children.Add(Block(2, 3, 1));
            canvas.Children.Add(Block(8, 3, 0.45));
            canvas.Children.Add(Block(2, 9, 0.45));
            canvas.Children.Add(Block(8, 9, 1));
            break;

        case AnnotationTool.Blur:
            // Nested rings fading outwards: the same shape losing its edge, which is
            // what distinguishes it from the hard blocks above.
            canvas.Children.Add(Ring(1, 14, 0.35));
            canvas.Children.Add(Ring(4, 8, 0.7));
            canvas.Children.Add(Ring(6, 4, 1));
            break;

        case AnnotationTool.Measure:
            // A span with a bar across each end, which is exactly what the tool draws.
            canvas.Children.Add(Stroke(3, 8, 13, 8));
            canvas.Children.Add(Stroke(3, 4, 3, 12));
            canvas.Children.Add(Stroke(13, 4, 13, 12));
            break;

        case AnnotationTool.Select:
            // A pointer, because this is the one tool that changes what is already there
            // instead of adding to it. Drawn as the outline of a cursor arrow.
            canvas.Children.Add(Stroke(3, 2, 3, 13));
            canvas.Children.Add(Stroke(3, 2, 11, 9.5));
            canvas.Children.Add(Stroke(3, 13, 6.5, 9.5));
            canvas.Children.Add(Stroke(6.5, 9.5, 11, 9.5));
            break;

        case AnnotationTool.Loupe:
            // A circle with a handle: the one icon here that is a picture of the
            // instrument rather than of its mark, because the mark is the pixels
            // underneath at twice the size and there is no drawing that.
            canvas.Children.Add(Ring(1, 11, 1));
            canvas.Children.Add(Stroke(11, 11, 14.5, 14.5, thickness: 2));
            break;

        case AnnotationTool.Text:
            // A capital T, drawn rather than typed: a glyph would be the one icon whose
            // size and weight follow the toolbar's font instead of the row.
            canvas.Children.Add(Stroke(3, 3.5, 13, 3.5));
            canvas.Children.Add(Stroke(8, 3.5, 8, 13));
            break;

        case AnnotationTool.Number:
            canvas.Children.Add(Ring(1, 14, 1));
            canvas.Children.Add(Stroke(8, 4.5, 8, 11.5));
            canvas.Children.Add(Stroke(8, 4.5, 6, 6.5));
            break;

        default:
            // A tool the icon set has not caught up with still has to be usable, so it
            // falls back to its name rather than to an empty button. The stamp is the
            // one that stays here on purpose: its mark is whichever emoji is chosen,
            // and the picker beside it already shows that.
            return new TextBlock
            {
                Text = Label(tool),
                Foreground = IconBrush(1),
                FontSize = 12,
            };
        }

        return canvas;
    }

    private static Line Stroke(double x1, double y1, double x2, double y2, double thickness = 1.6, double opacity = 1) =>
        new()
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = IconBrush(opacity),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };

    private static Rectangle Box(bool filled) => new()
    {
        Width = 12,
        Height = 9,
        Stroke = IconBrush(1),
        StrokeThickness = 1.6,
        Fill = filled ? IconBrush(1) : null,
        Margin = new Thickness(2, 3.5, 0, 0),
    };

    private static Rectangle Block(double x, double y, double opacity) => new()
    {
        Width = 5,
        Height = 4,
        Fill = IconBrush(opacity),
        Margin = new Thickness(x, y, 0, 0),
    };

    private static Ellipse Ring(double inset, double extent, double opacity) => new()
    {
        Width = extent,
        Height = extent,
        Stroke = IconBrush(opacity),
        StrokeThickness = 1.4,
        Margin = new Thickness(inset + 1, inset, 0, 0),
    };

    /// <summary>
    /// White, because the toolbar is dark whatever the system theme is. A
    /// theme-adaptive brush would be invisible on it in light mode.
    /// </summary>
    private static SolidColorBrush IconBrush(double opacity) =>
        new(Color.FromArgb((byte)(255 * opacity), 255, 255, 255));
}
