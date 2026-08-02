using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Core.Output;
using Macshot.Windows.Rendering;
using Macshot.Windows.Services;
using static Macshot.Windows.Services.Localization;
using Macshot.Windows.Toolbar;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Color resolves to
// Macshot.Color and does not compile.
using Windows.System;
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

    /// <summary>
    /// The gap between two controls on the row. macshot's 4 — <c>ToolOptionsRowView.swift:305</c>
    /// and everywhere else it advances past a control. WinUI's own comfortable spacing is
    /// two and a half times that, and on a row this dense it is most of why the port's
    /// options bar was wider than the same bar on macOS.
    /// </summary>
    private const double OptionGap = 4;

    /// <summary>The name in front of a slider — 9.5 medium at the icon colour's 0.4, <c>:298–300</c>.</summary>
    private const double OptionLabelSize = 9.5;

    /// <summary>The number after a slider — 10 medium at 0.6, <c>:325–328</c>.</summary>
    private const double OptionValueSize = 10;

    /// <summary>What the width slider spans when it is a stroke rather than a font size.</summary>
    private const double MinStroke = 1;

    private const double MaxStroke = 32;

    private readonly Canvas _surface = new();
    private readonly ToolbarStrip _tools = new(Orientation.Horizontal);
    private readonly ToolbarStrip _actions = new(Orientation.Vertical);
    private readonly Border _optionsRow;
    private readonly StackPanel _optionsContent;

    private readonly ColorPickerView _colorPicker = new();
    private readonly EffectsPickerView _effectsPicker = new();
    private readonly BeautifySwatchGrid _frames = new();
    private readonly TextBlock _sizeLabel = OptionLabel();
    private readonly Slider _size = OptionSlider(100, MinStroke, MaxStroke);
    private readonly TextBlock _sizeValue = OptionValue(28);
    private readonly StyleSegments _lineStyle = new();
    private readonly TextBlock _cornerLabel = OptionLabel(L("Rounded"));
    private readonly Slider _cornerRadius = OptionSlider(84, 0, 64);
    private readonly TextBlock _cornerValue = OptionValue(28);
    private readonly StyleSegments _arrowStyle = new();

    /// <summary>
    /// macshot's Flip, which turns an arrow round without redrawing it — an arrow is
    /// drawn from where the hand starts to where it stops, and what it should point at is
    /// often where the hand started.
    /// </summary>
    private readonly CheckBox _flipArrow = new()
    {
        Content = L("Flip"),
        FontSize = OptionValueSize,
        MinWidth = 0,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly StyleSegments _smoothing = new();
    private readonly StyleSegments _censorMode = new();
    private readonly Button _font = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 10, Padding = new Thickness(8, 2, 8, 2) };
    private readonly FontPickerView _fontChoices = new();
    private readonly StyleSegments _weight = new();
    /// <summary>
    /// macshot's outline controls: a halo under the mark, in a colour of its own. A red
    /// arrow over a red button is invisible, and the answer is a rim rather than a
    /// different arrow.
    /// </summary>
    private readonly ToggleSwatch _outline = new(L("Outline"));

    private readonly ToggleSwatch _textFill = new(L("Fill"));
    private readonly ToggleSwatch _textOutline = new(L("Outline"));
    private readonly Button _stamp = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly GridView _stampChoices = new() { MaxWidth = 240, SelectionMode = ListViewSelectionMode.Single, RequestedTheme = ElementTheme.Dark };

    /// <summary>
    /// The hairlines between groups of controls, each paired with the group it introduces
    /// so it can go when that group does. macshot's separator — 1 wide, 6 clear either
    /// side, at the icon colour's tenth — <c>ToolOptionsRowView.swift:288–293</c>.
    /// </summary>
    private readonly List<(Border Rule, FrameworkElement[] Group)> _optionGroups = [];

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

    private bool _recordingSetup;

    private bool _beautified;

    private bool _inverted;

    public AnnotationToolbarView()
    {
        _optionsContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = OptionGap,
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
    /// Raised with the background the user picked from behind the Frame button, so the
    /// host can arm the frame and say which one it now is.
    /// </summary>
    public event EventHandler<int>? FrameStyleChosen;

    /// <summary>
    /// True in the editor window, which has no region to cancel or move and places its
    /// strips at fixed corners rather than around a selection.
    /// </summary>
    public bool EditorMode { get; set; }

    /// <summary>
    /// Whether the strip is the one shown after a region has been chosen to record:
    /// Start, Cancel and the five switches that decide what ends up in the file.
    /// </summary>
    /// <remarks>
    /// macshot replaces the whole action strip here and empties the tool strip, because
    /// there is nothing to draw on yet — and because every one of those switches has to
    /// be decided before the recording starts rather than after.
    /// </remarks>
    public bool RecordingSetup
    {
        get => _recordingSetup;
        set
        {
            if (_recordingSetup != value)
            {
                _recordingSetup = value;
                RefreshStrips();
            }
        }
    }

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

        // macshot's rememberLastTool. Here rather than in LoadStyle, which runs again
        // whenever the style is reloaded: putting a tool in the user's hand is right
        // once, at the start of a capture, and an imposition at any later moment.
        // A tool that has since been hidden from the strip is not restored — the strip
        // would show nothing selected while the next drag drew with it.
        if (settings.Current.RememberLastTool
            && settings.Current.EnabledTools().Contains(settings.Current.LastTool))
        {
            editor.Tool = settings.Current.LastTool;
        }

        _colorPicker.LoadCustomColors(settings.Current.CustomColors);
        RefreshStrips();

        _colorPicker.CustomColorsChanged += (_, saved) => Remember(
            current => current with { CustomColors = saved });

        _colorPicker.ColorChanged += (_, _) => ApplyStyle();
        _effectsPicker.Changed += (_, options) =>
        {
            // The strip first, so the button lights on the same frame the picture
            // changes rather than one after it.
            RefreshStrips();
            EffectsChanged?.Invoke(this, options);
        };
        _size.ValueChanged += (_, _) =>
        {
            ShowSliderValue(_size, _sizeValue);
            ApplyStyle();
        };

        _cornerRadius.ValueChanged += (_, _) =>
        {
            ShowSliderValue(_cornerRadius, _cornerValue);
            ApplyStyle();
        };

        _censorMode.SelectionChanged += (_, _) => ApplyStyle();
        _lineStyle.SelectionChanged += (_, _) => ApplyStyle();
        _arrowStyle.SelectionChanged += (_, _) => ApplyStyle();
        _flipArrow.Checked += (_, _) => ApplyStyle();
        _flipArrow.Unchecked += (_, _) => ApplyStyle();
        _stampChoices.SelectionChanged += StampChoice_Changed;
        _smoothing.SelectionChanged += (_, index) =>
        {
            if (!_isLoadingStyle && index >= 0 && _editor is { } bound)
            {
                bound.Smoothing = (PencilSmoothing)index;
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
            // Zero rather than the strip's own size while it is hidden: an empty strip
            // still reports a slab's worth of padding, and the action strip would be
            // placed clear of something that is not on screen.
            _tools.Visibility == Visibility.Visible ? _tools.Size : default,
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

            var occupied = new List<CaptureRegion>(3) { layout.Actions };

            if (_tools.Visibility == Visibility.Visible)
            {
                occupied.Add(layout.Tools);
            }

            if (_optionsRow.Visibility == Visibility.Visible)
            {
                occupied.Add(layout.OptionsRow);
            }

            return occupied;
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
    public void ApplyPickedColor(AnnotationColor sampled)
    {
        _colorPicker.Color = Color.FromArgb(
            _colorPicker.Color.A,
            sampled.Red,
            sampled.Green,
            sampled.Blue);

        // Setting the picker's colour is deliberately silent — that is what lets
        // LoadStyle fill it without writing a half-built style back — so the one place
        // that sets it and does mean a change says so itself.
        ApplyStyle();
    }

    /// <summary>
    /// Remembers what the options row and the strip were left set to, for next time. A
    /// failure is swallowed on purpose: this runs while the host is being torn down,
    /// there is no window left to report into, and the cost of losing it is that the
    /// next capture starts from the previous colour.
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

        if (current.RememberLastTool && IsRemembered(editor.Tool) && editor.Tool != current.LastTool)
        {
            updated = updated with { LastTool = editor.Tool };
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

        // The five recording switches are settings, and settings are this control's to
        // write: the host would only be handing them straight back. Everything else on
        // that strip — start, cancel, the preferences, the region handle — is the
        // host's, and goes out with the rest.
        case ToolbarCommand.MouseHighlight:
            Toggle(current => current with { ShowClickHighlight = !current.ShowClickHighlight });
            return;

        case ToolbarCommand.ShowKeystrokes:
            Toggle(current => current with { ShowKeystrokes = !current.ShowKeystrokes });
            return;

        case ToolbarCommand.SystemAudio:
            Toggle(current => current with { RecordSystemAudio = !current.RecordSystemAudio });
            return;

        case ToolbarCommand.MicAudio:
            Toggle(current => current with { RecordMicAudio = !current.RecordMicAudio });
            return;

        case ToolbarCommand.Webcam:
            Toggle(current => current with { RecordWebcam = !current.RecordWebcam });
            CommandInvoked?.Invoke(this, item.Command);
            return;

        default:
            CommandInvoked?.Invoke(this, item.Command);
            return;
        }
    }

    /// <summary>
    /// Writes a recording switch and relights its button.
    /// </summary>
    /// <remarks>
    /// The strip is rebuilt from the settings rather than the button toggling itself, so
    /// the light is the setting rather than a second copy of it — the preferences window
    /// can be open at the same time, showing the same switch.
    /// </remarks>
    private void Toggle(Func<CaptureSettings, CaptureSettings> change)
    {
        Remember(change);
        RefreshStrips();
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

        // Chromeless: the picker paints its own dark slab at macshot's exact size, and
        // the presenter's own 12 of padding and light background would sit around it as
        // a second popover.
        var bare = ToolbarPalette.BareFlyoutStyle;

        // Detached from any previous anchor first: a Flyout can only be shown from one
        // place at a time, and the strip rebuilds its buttons.
        new Flyout { Content = _colorPicker, FlyoutPresenterStyle = bare }.ShowAt(anchor);
    }

    /// <summary>
    /// Writes a change back to the settings file. A failure is swallowed for the reason
    /// every other write from the toolbar is: the user is mid-capture, there is no window
    /// to report into, and the cost is that the change comes back next time.
    /// </summary>
    private void Remember(Func<CaptureSettings, CaptureSettings> change)
    {
        if (_settings is not { } settings)
        {
            return;
        }

        try
        {
            settings.Save(change(settings.Current).Normalized());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The menu behind an action button. Only Save has one, and it holds the way of
    /// saving that is not the default — macshot's own arrangement: one press for the
    /// usual answer, the menu for the other one.
    /// </summary>
    private void Action_Alternate(object? sender, ToolbarItem item)
    {
        if (_actions.ButtonFor(item.Command) is not { } anchor)
        {
            return;
        }

        switch (item.Command)
        {
        case ToolbarCommand.Save:
            var menu = new MenuFlyout();
            var saveAs = new MenuFlyoutItem { Text = L("Save As...") };
            saveAs.Click += (_, _) => CommandInvoked?.Invoke(this, ToolbarCommand.SaveAs);
            menu.Items.Add(saveAs);
            menu.ShowAt(anchor);
            break;

        case ToolbarCommand.Beautify:
            ShowFramePicker(anchor);
            break;

        case ToolbarCommand.Upload:
            // Where macshot keeps it (OverlayView.swift:7575) rather than in Preferences:
            // it is a question about the capture in front of you, and it belongs on the
            // button that would send it. Off unless asked for, in both products — pressing
            // Upload is already a deliberate act, and a confirmation on every one of them
            // is a thing people click through without reading.
            var confirm = new ToggleMenuFlyoutItem
            {
                Text = L("Confirm before upload"),
                IsChecked = _settings?.Current.UploadConfirm == true,
            };
            confirm.Click += (_, _) => Remember(current => current with { UploadConfirm = confirm.IsChecked });

            var uploadMenu = new MenuFlyout();
            uploadMenu.Items.Add(confirm);
            uploadMenu.ShowAt(anchor);
            break;

        default:
            break;
        }
    }

    /// <summary>
    /// The backgrounds, offered where the button that uses them is.
    /// </summary>
    /// <remarks>
    /// macshot puts the picker on the options row that appears while the frame is armed.
    /// The port has no options row for an action button, so it goes behind the right-click
    /// — the same place Save keeps "Save as...", and the one gesture on this strip that is
    /// already spoken for by nothing else. Picking a background arms the frame as well as
    /// choosing it: nobody opens this to change a setting they cannot see the effect of.
    /// </remarks>
    private void ShowFramePicker(FrameworkElement anchor)
    {
        if (_settings is not { } settings)
        {
            return;
        }

        // Painting 48 gradients is not free, so the grid is filled as it opens rather
        // than held ready; the only thing that changes between openings is the ring.
        _frames.Show(settings.Current.ToBeautifyOptions().StyleIndex);

        var bare = ToolbarPalette.BareFlyoutStyle;

        var flyout = new Flyout { Content = _frames, FlyoutPresenterStyle = bare };

        void Chosen(object? sender, int index)
        {
            _frames.Picked -= Chosen;
            flyout.Hide();
            Remember(current => current with { BeautifyStyleIndex = index });
            FrameStyleChosen?.Invoke(this, index);
        }

        _frames.Picked += Chosen;
        flyout.ShowAt(anchor);
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

    /// <summary>
    /// Does whatever <paramref name="key"/> is bound to, and says whether it was bound to
    /// anything — so the caller knows whether to let the key travel on.
    /// </summary>
    /// <remarks>
    /// A tool cannot be picked while a recording is being set up: the tools are not on
    /// screen then, and a key that swapped the tool underneath a hidden strip would leave
    /// the user drawing with something they never chose once the recording started.
    /// </remarks>
    public bool TryShortcut(VirtualKey key)
    {
        var chosen = _settings?.Current.ToolShortcuts;
        if (ToolShortcuts.Find(ShortcutKey.Of(key), chosen) is not { } shortcut)
        {
            return false;
        }

        if (shortcut.Tool is { } tool)
        {
            if (_recordingSetup)
            {
                return false;
            }

            SelectTool(tool);
            return true;
        }

        CommandInvoked?.Invoke(this, shortcut.Command);
        return true;
    }

    /// <summary>
    /// The same buttons, each carrying the key that also does its job.
    /// </summary>
    /// <remarks>
    /// Left alone when the user has turned the hints off, so that nothing has to be
    /// stripped back out again downstream.
    /// </remarks>
    private IReadOnlyList<ToolbarItem> WithKeys(IReadOnlyList<ToolbarItem> items)
    {
        var settings = _settings?.Current ?? CaptureSettings.Default;
        if (!settings.ShowShortcutsInTooltips)
        {
            return items;
        }

        var labelled = new List<ToolbarItem>(items.Count);
        foreach (var item in items)
        {
            var key = KeyFor(item, settings);
            labelled.Add(key.Length == 0 ? item : item with { Shortcut = ToolShortcuts.Describe(key) });
        }

        return labelled;
    }

    /// <summary>
    /// The key this button also answers to, or empty when nothing is on it.
    /// </summary>
    /// <remarks>
    /// A tool button matches on its tool and every other button on its command, because
    /// each tool button carries the same <see cref="ToolbarCommand.PickTool"/> and
    /// matching on that alone would put the pencil's key on all of them.
    /// </remarks>
    private static string KeyFor(ToolbarItem item, CaptureSettings settings)
    {
        foreach (var shortcut in ToolShortcuts.All)
        {
            var matches = item.Tool is { } tool
                ? shortcut.Tool == tool
                : shortcut.Tool is null && shortcut.Command == item.Command;

            if (matches)
            {
                return ToolShortcuts.KeyFor(shortcut, settings.ToolShortcuts);
            }
        }

        return ToolShortcuts.Unbound;
    }

    /// <summary>
    /// Whether a tool is one worth starting the next capture in.
    /// </summary>
    /// <remarks>
    /// The four that are not are things done to a capture rather than marks made on one.
    /// Restored, each would start the next capture in a mode the user has to leave
    /// before they can draw — and the sampler would start it holding a pipette over a
    /// screenshot nobody asked to sample.
    /// </remarks>
    private static bool IsRemembered(AnnotationTool tool) => tool
        is not (AnnotationTool.Select or AnnotationTool.Loupe
            or AnnotationTool.ColorSampler or AnnotationTool.Crop);

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
        if (_recordingSetup)
        {
            var current = _settings?.Current ?? CaptureSettings.Default;

            _tools.Visibility = Visibility.Collapsed;
            _optionsRow.Visibility = Visibility.Collapsed;
            _actions.SetItems(WithKeys(ToolbarActions.Recording(
                current.ShowClickHighlight,
                current.ShowKeystrokes,
                current.RecordSystemAudio,
                current.RecordMicAudio,
                current.RecordWebcam)));

            if (_placedAround is { } placed)
            {
                Reposition(placed.Selection, placed.Screen, placed.Avoid);
            }

            return;
        }

        if (_editor is not { } editor)
        {
            return;
        }

        var hidden = _settings?.Current.HiddenActions;

        _tools.Visibility = Visibility.Visible;
        _tools.SetItems(WithKeys(ToolbarActions.Tools(
            editor.Tool,
            _settings?.Current.EnabledTools(),
            _beautified,
            _inverted,
            !_effectsPicker.Options.IsIdentity,
            hidden)));
        // The offline build has neither a translator nor an uploader compiled into it, so
        // it is not offered a button for either.
        _actions.SetItems(WithKeys(ToolbarActions.Actions(
            EditorMode,
            translation: !BuildVariant.IsOffline,
            hiddenActions: hidden,
            upload: !BuildVariant.IsOffline)));
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
        var sizes = Show(AnnotationToolOptions.UsesSize(tool));
        _sizeLabel.Visibility = sizes;
        _size.Visibility = sizes;
        _sizeValue.Visibility = sizes;
        _sizeLabel.Text = SizeLabelFor(tool);

        // A stroke is measured in pixels; an extent is a number of its own, which is what
        // macshot shows for the loupe. The wider box is that number's — it can reach three
        // digits where a stroke cannot.
        var extent = AnnotationToolOptions.SizeMeaning(tool) == AnnotationSizeMeaning.Extent;
        _sizeValue.Width = extent ? 32 : 28;
        _sizeValue.Tag = extent ? string.Empty : "px";
        SyncSizeSlider(tool);

        _lineStyle.Visibility = Show(AnnotationToolOptions.UsesLineStyle(tool));
        _arrowStyle.Visibility = Show(AnnotationToolOptions.UsesArrowStyle(tool));
        _flipArrow.Visibility = _arrowStyle.Visibility;

        // Every mark with an edge to rim. Censor marks and the loupe carry their own
        // chrome, and a halo round a pixelated block would outline the redaction.
        _outline.Visibility = Show(AnnotationToolOptions.UsesLineStyle(tool) || tool == AnnotationTool.Arrow);

        var rounds = Show(AnnotationToolOptions.UsesCornerRadius(tool));
        _cornerLabel.Visibility = rounds;
        _cornerRadius.Visibility = rounds;
        _cornerValue.Visibility = rounds;
        _stamp.Visibility = Show(AnnotationToolOptions.UsesStamp(tool));
        _smoothing.Visibility = Show(AnnotationEditor.IsFreeform(tool));
        _censorMode.Visibility = Show(AnnotationToolOptions.UsesCensorMode(tool));

        var typesetting = Show(tool == AnnotationTool.Text);
        _font.Visibility = typesetting;
        _weight.Visibility = typesetting;
        _textFill.Visibility = typesetting;
        _textOutline.Visibility = typesetting;

        // A group's hairline follows the group, and the first one showing loses its rule
        // so the row never opens with a line hanging off its left edge.
        var seen = false;
        foreach (var (rule, group) in _optionGroups)
        {
            var showing = group.Any(control => control.Visibility == Visibility.Visible);
            rule.Visibility = Show(showing && seen);
            seen |= showing;
        }

        // The row itself goes when it would be empty, rather than sitting under the
        // tools as a bar of nothing.
        _optionsRow.Visibility = Show(seen);
    }

    /// <summary>
    /// Writes a slider's number into the box after it, in whatever unit that box carries.
    /// </summary>
    /// <remarks>
    /// The readout is the difference between a width that can be set and a width that can
    /// be *restored*: without it a stroke is dragged until it looks right and there is no
    /// way back to the one used on the last capture.
    /// </remarks>
    private static void ShowSliderValue(Slider slider, TextBlock readout) =>
        readout.Text = $"{(int)Math.Round(slider.Value)}{readout.Tag as string}";

    /// <summary>
    /// Points the one size slider at whichever number the tool in hand is sized by.
    /// </summary>
    /// <remarks>
    /// The text tool is sized by <see cref="AnnotationStyle.FontSize"/> and everything
    /// else by its stroke width, which is the whole reason the two are separate: a label
    /// set to 42 must not leave the next arrow 42 pixels thick. Reloading rather than
    /// rescaling, so switching to the text tool and back returns both to what they were.
    /// </remarks>
    private void SyncSizeSlider(AnnotationTool tool)
    {
        if (_editor is not { } editor)
        {
            return;
        }

        var wasLoading = _isLoadingStyle;
        _isLoadingStyle = true;
        try
        {
            if (tool == AnnotationTool.Text)
            {
                _size.Minimum = AnnotationStyle.MinFontSize;
                _size.Maximum = AnnotationStyle.MaxFontSize;
                _size.Value = Math.Clamp(
                    editor.Style.FontSize,
                    AnnotationStyle.MinFontSize,
                    AnnotationStyle.MaxFontSize);
            }
            else
            {
                _size.Minimum = MinStroke;
                _size.Maximum = MaxStroke;
                _size.Value = Math.Clamp(editor.Style.StrokeWidth, MinStroke, MaxStroke);
            }
        }
        finally
        {
            _isLoadingStyle = wasLoading;
        }

        ShowSliderValue(_size, _sizeValue);
    }

    /// <summary>
    /// Repaints what was drawn from a brush of its own rather than from the palette's
    /// shared ones. Those follow a colour change on their own; these were made on the spot
    /// and have to be asked.
    /// </summary>
    private void RepaintChrome()
    {
        _sizeLabel.Foreground = ToolbarPalette.IconBrush(0.4);
        _cornerLabel.Foreground = ToolbarPalette.IconBrush(0.4);
        _sizeValue.Foreground = ToolbarPalette.IconBrush(0.6);
        _cornerValue.Foreground = ToolbarPalette.IconBrush(0.6);
    }

    /// <summary>The name in front of a slider, at macshot's size, weight and opacity.</summary>
    private static TextBlock OptionLabel(string text = "") => new()
    {
        Text = text,
        FontSize = OptionLabelSize,
        FontWeight = FontWeights.Medium,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// The number after a slider: right-aligned in a fixed box with tabular figures, so
    /// the controls after it do not shuffle sideways as the slider is dragged.
    /// </summary>
    private static TextBlock OptionValue(double width, string unit = "px")
    {
        var value = new TextBlock
        {
            Width = width,
            Tag = unit,
            FontSize = OptionValueSize,
            FontWeight = FontWeights.Medium,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
        return value;
    }

    private static Slider OptionSlider(double width, double minimum, double maximum) => new()
    {
        Width = width,
        Height = 20,
        Minimum = minimum,
        Maximum = maximum,
        StepFrequency = 1,
        VerticalAlignment = VerticalAlignment.Center,

        // The number lives in its own box after the slider, at macshot's size and weight.
        // WinUI's own tooltip appears only while the thumb is held, which is exactly when
        // the value is least needed.
        IsThumbToolTipEnabled = false,
    };

    private void BuildOptionsRow()
    {
        RepaintChrome();

        ToolTipService.SetToolTip(_arrowStyle, "Arrow ends");
        ToolTipService.SetToolTip(_stamp, "Stamp");

        // Drawn rather than named: macshot's segments carry a picture of the mark you are
        // about to make, which is both quicker to read than "Dashed" and one click rather
        // than a combo's two.
        _lineStyle.SetSegments([.. Enum.GetValues<LineStyle>().Select(style =>
            new StyleSegment(StylePreviews.Line(style), null, StylePreviews.LineSegmentWidth))]);

        _arrowStyle.SetSegments([.. Enum.GetValues<ArrowStyle>().Select(style =>
            new StyleSegment(StylePreviews.Arrow(style), null, StylePreviews.ArrowSegmentWidth))]);

        // Here and nowhere else, because it is a choice made while drawing — which is
        // where macshot puts it, next to the pencil. Worded, as macshot's is: the
        // difference between two smoothings is invisible at 22 points.
        //
        // The words are the enum's names on purpose. None, Smooth and Refined are strings
        // macshot ships, so naming the segments after the enum is what gets them
        // translated; a name invented here would read English in every language.
        ToolTipService.SetToolTip(_smoothing, "Freehand smoothing");
        _smoothing.SetSegments([.. Enum.GetValues<PencilSmoothing>().Select(mode =>
            new StyleSegment(null, L(mode.ToString()), 0))]);

        // The censor tool's only option. There is deliberately no strength beside it:
        // how much of a redaction survives is not a thing to leave to a slider.
        ToolTipService.SetToolTip(_censorMode, "How the region is covered");
        _censorMode.SetSegments([.. Enum.GetValues<CensorMode>().Select(mode =>
            new StyleSegment(null, L(mode.ToString()), 0))]);

        // Populated from StampGlyph.Choices so the picker and the renderer cannot offer
        // different sets.
        _stampChoices.ItemsSource = StampGlyph.Choices;
        _stamp.Content = StampEmoji;
        _stamp.Flyout = new Flyout { Content = _stampChoices };

        // The label's own four controls. macshot puts them on this row and nowhere else,
        // which is right: a face and a fill are chosen while looking at the label, not in
        // a settings window opened afterwards.
        ToolTipService.SetToolTip(_font, "Typeface");
        _font.Flyout = new Flyout { Content = _fontChoices };
        _fontChoices.SelectionChanged += FontChoice_Changed;
        _weight.SetSegments(
        [
            new StyleSegment(null, L("Regular"), 0),
            new StyleSegment(null, L("Bold"), 0),
        ]);
        _weight.SelectionChanged += (_, _) => ApplyStyle();
        _outline.Toggled += (_, _) => ApplyStyle();
        _outline.SwatchPressed += (_, _) => PickSwatchColor(_outline);
        _textFill.Toggled += (_, _) => ApplyStyle();
        _textOutline.Toggled += (_, _) => ApplyStyle();
        _textFill.SwatchPressed += (_, _) => PickSwatchColor(_textFill);
        _textOutline.SwatchPressed += (_, _) => PickSwatchColor(_textOutline);

        AddGroup(_sizeLabel, _size, _sizeValue);
        AddGroup(_lineStyle);
        AddGroup(_arrowStyle, _flipArrow);
        AddGroup(_outline);
        AddGroup(_cornerLabel, _cornerRadius, _cornerValue);
        AddGroup(_smoothing);
        AddGroup(_censorMode);
        AddGroup(_font, _weight);
        AddGroup(_textFill, _textOutline);
        AddGroup(_stamp);
    }

    private void FontChoice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_fontChoices.SelectedItem is null)
        {
            return;
        }

        _font.Content = FontPickerView.FamilyOf(_fontChoices.SelectedItem) is { Length: > 0 } family
            ? family
            : FontPickerView.SystemFace;

        _font.Flyout?.Hide();
        ApplyStyle();
    }

    /// <summary>
    /// Opens the colour picker over a fill or outline swatch, and hands what is chosen
    /// back to that swatch rather than to the mark's own colour.
    /// </summary>
    /// <remarks>
    /// The same picker the toolbar's colour button opens, borrowed for the length of the
    /// popover. A second instance would be a second set of custom slots, and the slots
    /// are the point of it.
    /// </remarks>
    private void PickSwatchColor(ToggleSwatch swatch)
    {
        var picker = new ColorPickerView { Color = swatch.Color };
        picker.LoadCustomColors(_settings?.Current.CustomColors ?? []);
        picker.ColorChanged += (_, chosen) => swatch.Pick(chosen);

        var bare = ToolbarPalette.BareFlyoutStyle;

        new Flyout { Content = picker, FlyoutPresenterStyle = bare }.ShowAt(swatch.Anchor);
    }

    /// <summary>
    /// Adds one group of controls behind a hairline. The hairline belongs to the group
    /// rather than sitting between two of them, so hiding a group takes its rule with it
    /// and the row cannot end up starting with a line or showing two in a row.
    /// </summary>
    private void AddGroup(params FrameworkElement[] group)
    {
        var rule = new Border
        {
            Width = 1,
            Height = OptionsRowHeight - 16,
            VerticalAlignment = VerticalAlignment.Center,

            // 6 either side of the 1: with the row's own 4 that is macshot's 13 from one
            // group's last control to the next group's first.
            Margin = new Thickness(2, 0, 2, 0),
            Background = ToolbarPalette.IconBrush(0.1),
        };

        _optionsContent.Children.Add(rule);
        foreach (var control in group)
        {
            _optionsContent.Children.Add(control);
        }

        _optionGroups.Add((rule, group));
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

            _lineStyle.SelectedIndex = (int)_loadedStyle.LineStyle;
            _arrowStyle.SelectedIndex = (int)_loadedStyle.ArrowStyle;
            _flipArrow.IsChecked = _loadedStyle.ArrowReversed;
            _outline.Show(_loadedStyle.Outline is not null, ToUiColor(
                _loadedStyle.Outline ?? new AnnotationColor(255, 255, 255)));
            _size.Value = _loadedStyle.StrokeWidth;
            _cornerRadius.Value = _loadedStyle.CornerRadius;
            ShowSliderValue(_size, _sizeValue);
            ShowSliderValue(_cornerRadius, _cornerValue);
            _colorPicker.Color = ToUiColor(_loadedStyle.Color);

            // Read here rather than by the editor itself, so Core stays free of the
            // settings file and this stays the one place the toolbar's state comes from.
            editor.Smoothing = settings.Current.PencilSmoothing;
            _smoothing.SelectedIndex = (int)editor.Smoothing;
            editor.SnapGuides = settings.Current.SnapGuides;
            _censorMode.SelectedIndex = (int)_loadedStyle.CensorMode;

            _fontChoices.Show(_loadedStyle.FontFamily);
            _font.Content = string.IsNullOrWhiteSpace(_loadedStyle.FontFamily)
                ? FontPickerView.SystemFace
                : _loadedStyle.FontFamily;
            _weight.SelectedIndex = _loadedStyle.Bold ? 1 : 0;

            // A fill or an outline that is switched off still has a colour, so turning it
            // back on gives back the one that was there rather than an arbitrary black.
            _textFill.Show(_loadedStyle.TextBackground is not null, ToUiColor(
                _loadedStyle.TextBackground ?? new AnnotationColor(0, 0, 0, 160)));
            _textOutline.Show(_loadedStyle.TextOutline is not null, ToUiColor(
                _loadedStyle.TextOutline ?? new AnnotationColor(255, 255, 255)));
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
        var previous = editor.Style;

        // The one slider is the label's size while the text tool is in hand and a stroke
        // width otherwise, so the other number is carried across untouched.
        var typesetting = editor.Tool == AnnotationTool.Text;

        editor.Style = new AnnotationStyle(
            new AnnotationColor(color.R, color.G, color.B, color.A),
            typesetting ? previous.StrokeWidth : Math.Max(MinStroke, _size.Value),
            _lineStyle.SelectedIndex >= 0 ? (LineStyle)_lineStyle.SelectedIndex : LineStyle.Solid,
            ArrowStyle: _arrowStyle.SelectedIndex >= 0
                ? (ArrowStyle)_arrowStyle.SelectedIndex
                : ArrowStyle.Filled,
            CornerRadius: Math.Max(0, _cornerRadius.Value),
            CensorMode: _censorMode.SelectedIndex >= 0
                ? (CensorMode)_censorMode.SelectedIndex
                : CensorMode.Pixelate)
        {
            FontSize = typesetting
                ? Math.Clamp(_size.Value, AnnotationStyle.MinFontSize, AnnotationStyle.MaxFontSize)
                : previous.FontSize,

            // Kept rather than cleared when the picker has no row for it: a family this
            // machine does not have still names the face the file asked for, and
            // dropping it would silently rewrite the setting on the first capture.
            FontFamily = _fontChoices.SelectedItem is null
                ? previous.FontFamily
                : FontPickerView.FamilyOf(_fontChoices.SelectedItem),
            Bold = _weight.SelectedIndex == 1,
            ArrowReversed = _flipArrow.IsChecked == true,
            Outline = _outline.IsOn ? ToAnnotationColor(_outline.Color) : null,
            TextBackground = _textFill.IsOn ? ToAnnotationColor(_textFill.Color) : null,
            TextOutline = _textOutline.IsOn ? ToAnnotationColor(_textOutline.Color) : null,
        };

        _tools.ShowSwatch(ToUiColor(editor.Style.Color));
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// What the size slider is called for this tool. One slider, two jobs — a stroke
    /// width and the size of a glyph — and a label that says "Width" over the size of a
    /// stamp is worse than no label.
    /// </summary>
    private static string SizeLabelFor(AnnotationTool tool) => AnnotationToolOptions.SizeMeaning(tool) switch
    {
        AnnotationSizeMeaning.Extent => L("Size"),
        _ => L("Stroke"),
    };

    private static Color ToUiColor(AnnotationColor color) =>
        new() { A = color.Alpha, R = color.Red, G = color.Green, B = color.Blue };

    private static AnnotationColor ToAnnotationColor(Color color) =>
        new(color.R, color.G, color.B, color.A);
}
