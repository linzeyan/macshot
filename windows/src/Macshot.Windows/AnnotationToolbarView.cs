using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Rendering;
using Macshot.Windows.Services;
using Macshot.Windows.Toolbar;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Color resolves to
// Macshot.Color and does not compile.
using Windows.UI;

namespace Macshot.Windows;

/// <summary>
/// macshot's toolbar: the tools under the region being captured, what to do with it
/// down the side, and the options for the tool in hand beneath the tools.
/// </summary>
/// <remarks>
/// <para>
/// Three surfaces rather than one bar, placed around the selection by
/// <see cref="ToolbarPlacement"/> — the arrangement the macOS app has. A single bar
/// pinned to the bottom of the display makes the eye leave the thing being annotated,
/// and it was the largest visible difference between the two versions of the product.
/// </para>
/// <para>
/// A control rather than markup inside one window, because two places need exactly this
/// — the capture overlay and the editor window — and a copy of it would drift the moment
/// either grew a tool. It owns the editor's <see cref="AnnotationEditor.Tool"/> and
/// <see cref="AnnotationEditor.Style"/> and nothing else; the host owns the pixels, the
/// pointer, and what each action means.
/// </para>
/// </remarks>
public sealed partial class AnnotationToolbarView : UserControl
{
    /// <summary>
    /// How tall the options row is. macshot's 34 — ToolOptionsRowView.swift:13 — which
    /// with <see cref="ToolbarPlacement.RowGap"/> makes the 38 it reserves for the pair.
    /// Fixed rather than measured, because the strips are placed before WinUI has laid
    /// anything out and a row that reported its height one frame late would leave the
    /// tools jumping.
    /// </summary>
    private const double OptionsRowHeight = 34;

    private readonly Canvas _surface = new();
    private readonly ToolbarStrip _tools = new(Orientation.Horizontal);
    private readonly ToolbarStrip _actions = new(Orientation.Vertical);
    private readonly Border _optionsRow;
    private readonly StackPanel _optionsContent;

    private readonly ColorPicker _colorPicker = new() { IsAlphaEnabled = true, RequestedTheme = ElementTheme.Dark };
    private readonly EffectsPickerView _effectsPicker = new();
    private readonly TextBlock _sizeLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider _size = new() { Width = 120, Minimum = 1, Maximum = 32, StepFrequency = 1 };
    private readonly ComboBox _lineStyle = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _cornerLabel = new() { Text = "Corners", VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider _cornerRadius = new() { Width = 90, Minimum = 0, Maximum = 64, StepFrequency = 1 };
    private readonly ComboBox _arrowStyle = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _smoothing = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _stamp = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly GridView _stampChoices = new() { MaxWidth = 240, SelectionMode = ListViewSelectionMode.Single, RequestedTheme = ElementTheme.Dark };

    private AnnotationEditor? _editor;
    private SettingsStore? _settings;

    /// <summary>
    /// The last arrangement asked for, so a strip that changes width can put itself back
    /// where it belongs without the host being told. Switching tools shows and hides the
    /// options row, and a row that grew after being placed would hang off one side.
    /// </summary>
    private (CaptureRegion Selection, CaptureRegion Screen, CaptureRegion Avoid)? _placedAround;

    /// <summary>Where the strips ended up, for anything else that has to keep clear.</summary>
    private ToolbarLayout? _placedLayout;

    /// <summary>The style the toolbar started from, so only a real change is written back.</summary>
    private AnnotationStyle _loadedStyle = AnnotationStyle.Default;

    private bool _isLoadingStyle;

    /// <summary>
    /// What was in hand before the colour sampler took over, so a pick hands the tool
    /// back instead of leaving the user to reselect what they were using.
    /// </summary>
    private AnnotationTool _toolBeforeSampling = AnnotationTool.Arrow;

    private bool _beautified;

    private bool _inverted;

    public AnnotationToolbarView()
    {
        _optionsContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _optionsRow = new Border
        {
            Background = ToolbarPalette.BackgroundBrush,
            CornerRadius = new CornerRadius(ToolbarPalette.StripRadius),
            Padding = new Thickness(8, 0, 8, 0),
            Height = OptionsRowHeight,

            // Dark whatever the system theme is, the way the macOS options row forces
            // darkAqua. Without this the Slider and ComboBox render light-on-light
            // against a near-black slab on a light-themed Windows.
            RequestedTheme = ElementTheme.Dark,
            Child = _optionsContent,
        };

        BuildOptionsRow();

        _surface.Children.Add(_optionsRow);
        _surface.Children.Add(_tools);
        _surface.Children.Add(_actions);
        Content = _surface;

        _tools.ItemInvoked += Strip_ItemInvoked;
        _actions.ItemInvoked += Strip_ItemInvoked;
        _tools.ItemAlternate += Tool_Alternate;
        _actions.ItemAlternate += Action_Alternate;
    }

    /// <summary>Raised when what is on the canvas no longer matches the document.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised when the colour sampler is armed or disarmed. The host answers it because
    /// the pixels a sample comes from are the host's: the whole screenshot under an
    /// overlay, the image being edited in an editor.
    /// </summary>
    public event EventHandler<bool>? ColorSamplingToggled;

    /// <summary>
    /// Raised by every button that is not a tool or the colour. One event rather than
    /// one per action: the host has a single place where it decides what copying, saving
    /// or cancelling means where it is being used.
    /// </summary>
    public event EventHandler<ToolbarCommand>? CommandInvoked;

    /// <summary>Raised on every move of the Adjust popover, with what it now asks for.</summary>
    public event EventHandler<ImageEffectsOptions>? EffectsChanged;

    /// <summary>
    /// True in the editor window, which has no region to cancel or move and places its
    /// strips at fixed corners rather than around a selection.
    /// </summary>
    public bool EditorMode { get; set; }

    /// <summary>
    /// Whether the capture is set to be framed, which lights the Beautify button.
    /// </summary>
    /// <remarks>
    /// The toolbar shows it rather than decides it: what beautifying means differs
    /// between a live capture, where it is a thing the delivered image will have done to
    /// it, and the editor, where it is done to the pixels the moment it is asked for.
    /// </remarks>
    public bool Beautified
    {
        get => _beautified;
        set
        {
            if (_beautified == value)
            {
                return;
            }

            _beautified = value;
            RefreshStrips();
        }
    }

    /// <summary>
    /// Whether the capture's colours are turned, which lights the Invert button.
    /// </summary>
    public bool Inverted
    {
        get => _inverted;
        set
        {
            if (_inverted == value)
            {
                return;
            }

            _inverted = value;
            RefreshStrips();
        }
    }

    /// <summary>The emoji the stamp tool places.</summary>
    public string StampEmoji { get; private set; } = StampGlyph.Default;

    /// <summary>
    /// Whether the sampler is armed, which makes the next click on the host a pick
    /// rather than a mark.
    /// </summary>
    public bool IsSamplingColor => _editor?.Tool == AnnotationTool.ColorSampler;

    /// <summary>
    /// Attaches the toolbar to an editor and the settings its style is remembered in.
    /// </summary>
    public void Bind(AnnotationEditor editor, SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(settings);

        _editor = editor;
        _settings = settings;

        // Before anything is drawn from the palette: the toolbar sits over a screenshot
        // rather than in a window, so its colours are the user's to choose and everything
        // below reads them.
        ToolbarPalette.Apply(settings.Current.ToToolbarColors());
        RepaintChrome();

        LoadStyle();
        RefreshStrips();

        _colorPicker.ColorChanged += (_, _) => ApplyStyle();
        _effectsPicker.Changed += (_, options) =>
        {
            // The strip first, so the button lights on the same frame the picture
            // changes rather than one after it.
            RefreshStrips();
            EffectsChanged?.Invoke(this, options);
        };
        _size.ValueChanged += (_, _) => ApplyStyle();
        _lineStyle.SelectionChanged += (_, _) => ApplyStyle();
        _arrowStyle.SelectionChanged += (_, _) => ApplyStyle();
        _cornerRadius.ValueChanged += (_, _) => ApplyStyle();
        _stampChoices.SelectionChanged += StampChoice_Changed;
        _smoothing.SelectionChanged += (_, _) =>
        {
            if (!_isLoadingStyle && _smoothing.SelectedIndex >= 0 && _editor is { } bound)
            {
                bound.Smoothing = (PencilSmoothing)_smoothing.SelectedIndex;
            }
        };
    }

    /// <summary>
    /// Puts the three surfaces around <paramref name="selection"/>, all in layout units.
    /// </summary>
    /// <param name="selection">The region being captured, or the image in the editor.</param>
    /// <param name="screen">What the toolbar must stay inside.</param>
    /// <param name="avoid">Something the action strip must not cover, if anything.</param>
    public void Reposition(CaptureRegion selection, CaptureRegion screen, CaptureRegion avoid = default)
    {
        _placedAround = (selection, screen, avoid);

        var hasOptions = _optionsRow.Visibility == Visibility.Visible;
        var sizes = new ToolbarSizes(
            _tools.Size,
            _actions.Size,
            hasOptions ? new CaptureRegion(0, 0, 0, OptionsRowHeight) : default);

        var layout = EditorMode
            ? FixedCorners(screen, sizes)
            : ToolbarPlacement.For(selection, screen, sizes, avoid);

        _placedLayout = layout;
        Place(_tools, layout.Tools);
        Place(_actions, layout.Actions);

        if (hasOptions)
        {
            _optionsRow.Width = layout.OptionsRow.Width;
            Place(_optionsRow, layout.OptionsRow);
        }
    }

    /// <summary>
    /// Where the strips are, for whatever else has to share the screen with them. Empty
    /// while the toolbar is hidden or before it has been placed.
    /// </summary>
    public IReadOnlyList<CaptureRegion> Occupies
    {
        get
        {
            if (Visibility != Visibility.Visible || _placedLayout is not { } layout)
            {
                return [];
            }

            return _optionsRow.Visibility == Visibility.Visible
                ? [layout.Tools, layout.Actions, layout.OptionsRow]
                : [layout.Tools, layout.Actions];
        }
    }

    /// <summary>Shows or hides the whole toolbar.</summary>
    public void ShowToolbar(bool visible) =>
        Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Arms or disarms the colour sampler. Called by the host as well as from the strip,
    /// because Escape and a completed pick both give it up.
    /// </summary>
    public void SetColorSampling(bool armed)
    {
        if (_editor is not { } editor || IsSamplingColor == armed)
        {
            return;
        }

        editor.Tool = armed ? AnnotationTool.ColorSampler : _toolBeforeSampling;
        RefreshStrips();
        ColorSamplingToggled?.Invoke(this, armed);
    }

    /// <summary>
    /// Puts a sampled colour on the toolbar, keeping the opacity already chosen.
    /// </summary>
    /// <remarks>
    /// Applied through the colour picker rather than straight onto the editor's style,
    /// so the swatch, the picker, and what the next mark is drawn in cannot disagree.
    /// Opacity is a property of the mark rather than of the pixel, and the pixel has no
    /// opinion about it: a screenshot is opaque everywhere.
    /// </remarks>
    public void ApplyPickedColor(AnnotationColor sampled) => _colorPicker.Color = Color.FromArgb(
        _colorPicker.Color.A,
        sampled.Red,
        sampled.Green,
        sampled.Blue);

    /// <summary>
    /// Remembers what the options row was left set to, for next time. A failure is
    /// swallowed on purpose: this runs while the host is being torn down, there is no
    /// window left to report into, and the cost of losing it is that the next capture
    /// starts from the previous colour.
    /// </summary>
    public void PersistStyle()
    {
        if (_editor is not { } editor || _settings is not { } settings)
        {
            return;
        }

        var current = settings.Current;
        var updated = editor.Style == _loadedStyle ? current : current.WithAnnotationStyle(editor.Style);
        if (editor.Smoothing != current.PencilSmoothing)
        {
            updated = updated with { PencilSmoothing = editor.Smoothing };
        }

        // Reference equality on purpose: the settings are a record holding a list, so
        // value equality would compare that list by reference anyway and this says what
        // is meant — nothing was changed, so nothing is written.
        if (ReferenceEquals(updated, current))
        {
            return;
        }

        try
        {
            settings.Save(updated);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The editor window's corners: tools bottom-centre, actions top-right. The same
    /// fixed places macshot uses there, since an editor has no selection to hang them
    /// off.
    /// </summary>
    private static ToolbarLayout FixedCorners(CaptureRegion screen, ToolbarSizes sizes)
    {
        var toolsY = screen.Bottom - 20 - sizes.Tools.Height
            - (sizes.OptionsRow.Height > 0 ? sizes.OptionsRow.Height + ToolbarPlacement.RowGap : 0);

        var tools = new CaptureRegion(
            screen.X + ((screen.Width - sizes.Tools.Width) / 2),
            toolsY,
            sizes.Tools.Width,
            sizes.Tools.Height);

        var actions = new CaptureRegion(
            screen.Right - sizes.Actions.Width - 20,
            screen.Y + 20,
            sizes.Actions.Width,
            sizes.Actions.Height);

        var optionsRow = sizes.OptionsRow.Height > 0
            ? new CaptureRegion(
                tools.X,
                tools.Bottom + ToolbarPlacement.RowGap,
                tools.Width,
                sizes.OptionsRow.Height)
            : default;

        return new ToolbarLayout(tools, actions, optionsRow);
    }

    private static void Place(FrameworkElement element, CaptureRegion where)
    {
        Canvas.SetLeft(element, where.X);
        Canvas.SetTop(element, where.Y);
    }

    private void Strip_ItemInvoked(object? sender, ToolbarItem item)
    {
        switch (item.Command)
        {
        case ToolbarCommand.PickTool when item.Tool is { } tool:
            SelectTool(tool);
            return;

        case ToolbarCommand.PickColor:
            ShowColorPicker();
            return;

        case ToolbarCommand.Adjust:
            ShowEffectsPicker();
            return;

        default:
            CommandInvoked?.Invoke(this, item.Command);
            return;
        }
    }

    /// <summary>
    /// The menu behind a tool button: what to do with a tool other than use it.
    /// </summary>
    /// <remarks>
    /// Sixteen tools is a long strip, and most people use four of them. Taking one off is
    /// offered where the tool is rather than only in the preferences, because the moment
    /// someone knows they never want the loupe is the moment they are looking at it.
    /// </remarks>
    private void Tool_Alternate(object? sender, ToolbarItem item)
    {
        if (item.Tool is not { } tool || sender is not FrameworkElement anchor || _settings is not { } settings)
        {
            return;
        }

        var menu = new MenuFlyout();

        var hide = new MenuFlyoutItem { Text = $"Hide {item.Tooltip}" };
        hide.Click += (_, _) => HideTool(tool);

        // Not offered when it would empty the strip: a toolbar with no tools on it is not
        // a preference, it is a broken window.
        hide.IsEnabled = settings.Current.EnabledTools().Count > 1;
        menu.Items.Add(hide);

        if (settings.Current.HiddenTools.Count > 0)
        {
            var restore = new MenuFlyoutItem { Text = "Show every tool" };
            restore.Click += (_, _) => SetHiddenTools([]);
            menu.Items.Add(restore);
        }

        menu.ShowAt(anchor);
    }

    private void HideTool(AnnotationTool tool)
    {
        if (_settings is not { } settings)
        {
            return;
        }

        SetHiddenTools([.. settings.Current.HiddenTools, tool.ToString()]);

        // A hidden tool cannot stay in hand, or the strip would show nothing selected
        // while the next drag drew with the tool that is no longer there.
        if (_editor is { } editor && editor.Tool == tool)
        {
            SelectTool(settings.Current.EnabledTools().First());
        }
    }

    /// <summary>
    /// Writes the strip's contents back to the settings file and rebuilds it.
    /// </summary>
    /// <remarks>
    /// A failure is swallowed for the same reason the style's is: the user is in the
    /// middle of a capture, there is no window to report into, and the cost is that the
    /// tool comes back next time.
    /// </remarks>
    private void SetHiddenTools(IReadOnlyList<string> hidden)
    {
        if (_settings is not { } settings)
        {
            return;
        }

        try
        {
            settings.Save((settings.Current with { HiddenTools = hidden }).Normalized());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        RefreshStrips();
    }

    private void ShowColorPicker()
    {
        if (_tools.ButtonFor(ToolbarCommand.PickColor) is not { } anchor)
        {
            return;
        }

        var flyout = new Flyout { Content = _colorPicker };

        // Detached from any previous anchor first: a Flyout can only be shown from one
        // place at a time, and the strip rebuilds its buttons.
        flyout.ShowAt(anchor);
    }

    /// <summary>
    /// The menu behind an action button. Only Save has one, and it holds the way of
    /// saving that is not the default — macshot's own arrangement: one press for the
    /// usual answer, the menu for the other one.
    /// </summary>
    private void Action_Alternate(object? sender, ToolbarItem item)
    {
        if (item.Command != ToolbarCommand.Save || _actions.ButtonFor(ToolbarCommand.Save) is not { } anchor)
        {
            return;
        }

        var menu = new MenuFlyout();
        var saveAs = new MenuFlyoutItem { Text = "Save as..." };
        saveAs.Click += (_, _) => CommandInvoked?.Invoke(this, ToolbarCommand.SaveAs);
        menu.Items.Add(saveAs);
        menu.ShowAt(anchor);
    }

    private void ShowEffectsPicker()
    {
        if (_tools.ButtonFor(ToolbarCommand.Adjust) is not { } anchor)
        {
            return;
        }

        // Detached from any previous anchor first, for the same reason the colour picker
        // is: a Flyout can be shown from one place at a time, and the strip rebuilds its
        // buttons whenever anything on it lights up.
        new Flyout { Content = _effectsPicker }.ShowAt(anchor);
    }

    /// <summary>Makes <paramref name="tool"/> the active one, as clicking its button would.</summary>
    private void SelectTool(AnnotationTool tool)
    {
        if (_editor is not { } editor)
        {
            return;
        }

        if (tool == AnnotationTool.ColorSampler)
        {
            _toolBeforeSampling = editor.Tool == AnnotationTool.ColorSampler
                ? _toolBeforeSampling
                : editor.Tool;

            editor.Tool = tool;
            RefreshStrips();
            ColorSamplingToggled?.Invoke(this, true);
            return;
        }

        // Reaching for a tool is as clear a way of abandoning a pick as Escape is.
        if (IsSamplingColor)
        {
            ColorSamplingToggled?.Invoke(this, false);
        }

        editor.Tool = tool;
        RefreshStrips();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Rebuilds both strips and the options row from the editor's state.
    /// </summary>
    private void RefreshStrips()
    {
        if (_editor is not { } editor)
        {
            return;
        }

        _tools.SetItems(ToolbarActions.Tools(
            editor.Tool,
            _settings?.Current.EnabledTools(),
            _beautified,
            _inverted,
            !_effectsPicker.Options.IsIdentity));
        // The offline build has no translator compiled into it, so it is not offered a
        // button for one.
        _actions.SetItems(ToolbarActions.Actions(EditorMode, translation: !BuildVariant.IsOffline));
        _tools.ShowSwatch(ToUiColor(editor.Style.Color));
        ShowOptionsFor(editor.Tool);

        if (_placedAround is { } around)
        {
            Reposition(around.Selection, around.Screen, around.Avoid);
        }
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
        _sizeLabel.Visibility = Show(AnnotationToolOptions.UsesSize(tool));
        _size.Visibility = _sizeLabel.Visibility;
        _sizeLabel.Text = SizeLabelFor(tool);

        _lineStyle.Visibility = Show(AnnotationToolOptions.UsesLineStyle(tool));
        _arrowStyle.Visibility = Show(AnnotationToolOptions.UsesArrowStyle(tool));

        var rounds = Show(AnnotationToolOptions.UsesCornerRadius(tool));
        _cornerLabel.Visibility = rounds;
        _cornerRadius.Visibility = rounds;
        _stamp.Visibility = Show(AnnotationToolOptions.UsesStamp(tool));
        _smoothing.Visibility = Show(AnnotationEditor.IsFreeform(tool));

        // The row itself goes when it would be empty, rather than sitting under the
        // tools as a bar of nothing.
        var anyOption = _optionsContent.Children
            .OfType<FrameworkElement>()
            .Any(child => child.Visibility == Visibility.Visible);

        _optionsRow.Visibility = Show(anyOption);
    }

    /// <summary>
    /// Repaints what was drawn from a brush of its own rather than from the palette's
    /// shared ones. Those follow a colour change on their own; these were made on the spot
    /// and have to be asked.
    /// </summary>
    private void RepaintChrome()
    {
        _sizeLabel.Foreground = ToolbarPalette.IconBrush();
        _cornerLabel.Foreground = ToolbarPalette.IconBrush();
    }

    private void BuildOptionsRow()
    {
        RepaintChrome();
        _size.VerticalAlignment = VerticalAlignment.Center;
        _cornerRadius.VerticalAlignment = VerticalAlignment.Center;

        ToolTipService.SetToolTip(_arrowStyle, "Arrow ends");
        ToolTipService.SetToolTip(_stamp, "Stamp");

        // On the toolbar as well as in Preferences, because it is a choice made while
        // drawing — macshot puts it here, next to the pencil, and nowhere else.
        ToolTipService.SetToolTip(_smoothing, "Freehand smoothing");
        _smoothing.ItemsSource = Enum.GetValues<PencilSmoothing>().Select(mode => mode.ToString()).ToList();

        // Populated from StampGlyph.Choices so the picker and the renderer cannot offer
        // different sets.
        _stampChoices.ItemsSource = StampGlyph.Choices;
        _stamp.Content = StampEmoji;
        _stamp.Flyout = new Flyout { Content = _stampChoices };

        _optionsContent.Children.Add(_sizeLabel);
        _optionsContent.Children.Add(_size);
        _optionsContent.Children.Add(_lineStyle);
        _optionsContent.Children.Add(_cornerLabel);
        _optionsContent.Children.Add(_cornerRadius);
        _optionsContent.Children.Add(_arrowStyle);
        _optionsContent.Children.Add(_smoothing);
        _optionsContent.Children.Add(_stamp);
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

            _lineStyle.ItemsSource = Enum.GetValues<LineStyle>().Select(style => style.ToString()).ToList();
            _lineStyle.SelectedIndex = (int)_loadedStyle.LineStyle;
            _arrowStyle.ItemsSource = Enum.GetValues<ArrowStyle>().Select(style => style.ToString()).ToList();
            _arrowStyle.SelectedIndex = (int)_loadedStyle.ArrowStyle;
            _size.Value = _loadedStyle.StrokeWidth;
            _cornerRadius.Value = _loadedStyle.CornerRadius;
            _colorPicker.Color = ToUiColor(_loadedStyle.Color);

            // Read here rather than by the editor itself, so Core stays free of the
            // settings file and this stays the one place the toolbar's state comes from.
            editor.Smoothing = settings.Current.PencilSmoothing;
            _smoothing.SelectedIndex = (int)editor.Smoothing;
        }
        finally
        {
            _isLoadingStyle = false;
        }
    }

    private void StampChoice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_stampChoices.SelectedItem is not string emoji)
        {
            return;
        }

        StampEmoji = emoji;
        _stamp.Content = emoji;
        _stamp.Flyout?.Hide();

        // Picking a stamp is asking to stamp: leaving the previous tool active would
        // make the choice look like it did nothing.
        SelectTool(AnnotationTool.Stamp);
    }

    /// <summary>
    /// The style applies to marks drawn from now on. Restyling what is already on the
    /// canvas would need a selection, which is a separate feature.
    /// </summary>
    private void ApplyStyle()
    {
        if (_isLoadingStyle || _editor is not { } editor)
        {
            return;
        }

        var color = _colorPicker.Color;
        editor.Style = new AnnotationStyle(
            new AnnotationColor(color.R, color.G, color.B, color.A),
            Math.Max(1, _size.Value),
            _lineStyle.SelectedIndex >= 0 ? (LineStyle)_lineStyle.SelectedIndex : LineStyle.Solid,
            ArrowStyle: _arrowStyle.SelectedIndex >= 0
                ? (ArrowStyle)_arrowStyle.SelectedIndex
                : ArrowStyle.Filled,
            CornerRadius: Math.Max(0, _cornerRadius.Value));

        _tools.ShowSwatch(ToUiColor(editor.Style.Color));
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

    private static Color ToUiColor(AnnotationColor color) =>
        new() { A = color.Alpha, R = color.Red, G = color.Green, B = color.Blue };
}
