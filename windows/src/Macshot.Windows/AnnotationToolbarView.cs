using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Core.Output;
using Macshot.Windows.Core.Recognition;
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
    /// Outline, wash, or solid — macshot's rectFillStyle picker. Its segments are rebuilt
    /// when the tool changes rather than set once, because macshot draws the preview as
    /// the shape in hand: an oval for the ellipse tool, a rounded box for the rectangle.
    /// </summary>
    private readonly StyleSegments _shapeFill = new();

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

    /// <summary>
    /// macshot's Pressure. Not folded into the smoothing row beside it: smoothing decides
    /// what the stroke's path is and this decides how wide it is along that path, and a
    /// four-choice row mixing the two would be answering two questions at once.
    /// </summary>
    private readonly CheckBox _pencilPressure = new()
    {
        Content = L("Pressure"),
        FontSize = OptionValueSize,
        MinWidth = 0,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// macshot's Smart marker: the stroke lands on the line of text it was drawn across
    /// rather than where the hand actually went. Highlighting a line of text by hand means
    /// holding a straight line at a constant height, which is the one thing a mouse is
    /// worst at.
    /// </summary>
    private readonly CheckBox _smartMarker = new()
    {
        Content = L("Smart"),
        FontSize = OptionValueSize,
        MinWidth = 0,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly StyleSegments _censorMode = new();
    private readonly TextBlock _drawLabel = OptionLabel(L("Draw:"));

    /// <summary>
    /// Whether a censor drag covers the whole region or only the text found inside it.
    /// macshot's <c>censorTextOnly</c> — the difference between blacking out a panel and
    /// blacking out the words on it, which is what most redactions actually want.
    /// </summary>
    private readonly StyleSegments _censorScope = new();

    private readonly TextBlock _autoLabel = OptionLabel(L("Auto:"));

    /// <summary>
    /// The kinds of secret the PII button looks for, and their menu items. Kept so the
    /// ticks can be filled from the settings once the toolbar is bound, which is after the
    /// menu itself is built.
    /// </summary>
    private readonly List<(PiiKind Kind, ToggleMenuFlyoutItem Item)> _piiKinds = [];

    /// <summary>
    /// Covers every line of text found in the region. macshot's "All Text": the answer for
    /// a whole panel of somebody else's data, where naming what is sensitive would be work
    /// the user should not have to do and a pattern that missed one would be a leak.
    /// </summary>
    private readonly Button _redactAllText = RedactButton(L("All Text"));

    /// <summary>
    /// Covers what looks like a secret, with the kinds it looks for behind the arrow.
    /// macshot's PII button and its dropdown, in one control for the same reason macshot
    /// pairs them: the list is what you reach for when the button covered the wrong thing.
    /// </summary>
    private readonly SplitButton _redactPii = new()
    {
        Content = L("PII"),
        FontSize = OptionValueSize,
        MinWidth = 0,
        Padding = new Thickness(8, 2, 8, 2),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// Covers every face in the region. macshot's Faces
    /// (<c>ToolOptionsRowView.swift:1272-1273</c>), and the one automatic redaction that
    /// the two text passes beside it cannot do at all: no amount of pattern-matching over
    /// a transcript finds a face in a screenshot of a call.
    /// </summary>
    private readonly Button _redactFaces = RedactButton(L("Faces"));

    /// <summary>
    /// Covers every person, not only their face — macshot's People (<c>:1275-1276</c>).
    /// </summary>
    /// <remarks>
    /// The wider of the two on purpose, and not redundant beside it: someone is
    /// identifiable from a uniform, a lanyard or a tattoo with their face already covered.
    /// </remarks>
    private readonly Button _redactPeople = RedactButton(L("People"));

    private readonly StyleSegments _numberFormat = new();
    private readonly TextBlock _startLabel = OptionLabel(L("Start:"));

    /// <summary>
    /// What the first badge of the capture counts from. A <see cref="NumberBox"/> rather
    /// than macshot's bare stepper, because Windows' stepper is one: typing 17 beats
    /// clicking an arrow sixteen times, and a screenshot that carries on from figure 16 is
    /// the case this exists for.
    /// </summary>
    /// <remarks>
    /// Sized to the three digits it can hold and no wider. macshot spends 50 on the whole
    /// control — a 19-wide stepper and the figure beside it
    /// (<c>ToolOptionsRowView.swift:881-897</c>) — and the 84 this used to ask for was a
    /// box with half its width empty, which on a row where every other control is drawn to
    /// its content read as a text field somebody had left in.
    /// </remarks>
    private readonly NumberBox _numberStart = new()
    {
        Minimum = 1,
        Maximum = CaptureSettings.MaxNumberStartAt,
        SmallChange = 1,
        LargeChange = 10,
        Width = 58,
        Height = 24,
        MinWidth = 0,
        Padding = new Thickness(6, 0, 0, 0),
        FontSize = OptionValueSize,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly StyleSegments _measureUnit = new();

    /// <summary>
    /// macshot's "Limit to selection": the rule cannot be dragged off the region being
    /// annotated. On by default there and here — a span that runs past the edge is
    /// measuring pixels that will be cropped out of the file.
    /// </summary>
    private readonly CheckBox _clampRuler = new()
    {
        Content = L("Limit to selection"),
        FontSize = OptionValueSize,
        MinWidth = 0,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// What the two number keys do while the ruler is in hand, written on the row.
    /// </summary>
    /// <remarks>
    /// macshot's own hint (<c>ToolOptionsRowView.swift:1139</c>), at the third of the icon
    /// colour it uses for one — quieter than a label, which is right for something that is
    /// telling rather than asking. It is the only place either product says these keys
    /// exist, so a ruler that answered them without saying so would be a feature nobody
    /// found.
    /// </remarks>
    private readonly TextBlock _measureHint = new()
    {
        Text = L("Hold 1 auto-vertical  ·  Hold 2 auto-horizontal"),
        FontSize = OptionLabelSize,
        FontWeight = FontWeights.Medium,
        Foreground = ToolbarPalette.IconBrush(0.3),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _zoomLabel = OptionLabel(L("Zoom"));

    private readonly Slider _loupeZoom = OptionSlider(
        84,
        AnnotationStyle.MinLoupeMagnification,
        AnnotationStyle.MaxLoupeMagnification);

    /// <summary>
    /// The magnification, written by <see cref="ShowZoomValue"/> rather than by the shared
    /// slider readout — hence no unit on the box, which is where that readout takes one
    /// from.
    /// </summary>
    private readonly TextBlock _zoomValue = OptionValue(38, string.Empty);

    private readonly TextBlock _dimLabel = OptionLabel(L("Dim"));

    private readonly Slider _spotlightDim = OptionSlider(
        84,
        AnnotationStyle.MinDimOpacity,
        AnnotationStyle.MaxDimOpacity);

    /// <summary>
    /// The dim as a percentage, written by <see cref="ShowDimValue"/> rather than by the
    /// shared readout: the slider runs in hundredths and the box says 55%, which is the
    /// number macshot shows and the only one anybody would say out loud.
    /// </summary>
    private readonly TextBlock _dimValue = OptionValue(38, string.Empty);

    /// <summary>
    /// Solid or dashed for the spotlight's ring, which is the whole of what macshot lets
    /// the user choose about it. Its own control rather than <see cref="_lineStyle"/>, so
    /// that picking a dash for the spotlight does not pick one for the pencil.
    /// </summary>
    private readonly StyleSegments _spotlightBorder = new();
    private readonly Button _font = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 10, Padding = new Thickness(8, 2, 8, 2) };
    private readonly FontPickerView _fontChoices = new();

    /// <summary>
    /// Bold, italic, underline and strikethrough, each turning on by itself.
    /// </summary>
    /// <remarks>
    /// Four switches rather than the two-way weight picker this row used to carry. They
    /// are not alternatives — a heading typed onto a screenshot is often bold and
    /// underlined at once — and a picker that made them exclusive would take away
    /// combinations anyone can see ought to be possible. macshot has the four
    /// (<c>ToolOptionsRowView.swift:919–942</c>).
    /// </remarks>
    private readonly ToggleButton _bold = TextStyleToggle("B", "Bold");

    private readonly ToggleButton _italic = TextStyleToggle("I", "Italic");

    private readonly ToggleButton _underline = TextStyleToggle("U", "Underline");

    private readonly ToggleButton _strikethrough = TextStyleToggle("S", "Strikethrough");

    /// <summary>
    /// Which edge a label's lines are hung from. Drawn rather than worded, the way every
    /// other picker on this row is: four ragged rules say "centred" in every language.
    /// </summary>
    private readonly StyleSegments _textAlignment = new();

    /// <summary>
    /// The label's size, walked a point at a time rather than dragged.
    /// </summary>
    /// <remarks>
    /// macshot's − 20 + (<c>ToolOptionsRowView.swift:968–994</c>), and not the shared width
    /// slider the port used to point at the font size. A point size is a number people
    /// know — 12, 18, 72 — and reaching an exact one on a slider 100 wide spanning 8 to 200
    /// means landing on a pixel worth two points. Held down, either button repeats.
    /// </remarks>
    private readonly RepeatButton _fontSmaller = FontSizeButton("−");

    private readonly TextBlock _fontSizeValue = new()
    {
        Width = 26,
        FontSize = OptionValueSize,
        FontWeight = FontWeights.Medium,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly RepeatButton _fontLarger = FontSizeButton("+");

    /// <summary>
    /// macshot's outline controls: a halo under the mark, in a colour of its own. A red
    /// arrow over a red button is invisible, and the answer is a rim rather than a
    /// different arrow.
    /// </summary>
    private readonly ToggleSwatch _outline = new(L("Outline"));

    private readonly ToggleSwatch _textFill = new(L("Fill"));
    private readonly ToggleSwatch _textOutline = new(L("Outline"));

    /// <summary>
    /// macshot's quick stamps, laid straight on the row. A stamp is a one-click mark, and
    /// the seventeen it offers there cover nearly every use of the tool — reaching them
    /// through a picker would make the commonest ones the slowest.
    /// </summary>
    private readonly List<Button> _quickStamps = [];

    /// <summary>
    /// The line drawn round each glyph, which is a different thing from the line round the
    /// pill: this one follows the letters. It is what makes a label readable over a
    /// screenshot that is pale in one half and dark in the other, where no fill colour
    /// works and a pill would cover the thing being pointed at.
    /// </summary>
    private readonly ToggleSwatch _textGlyphStroke = new(L("Stroke"));
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

    /// <summary>
    /// What the − and + are walking. Held here rather than read back off the style on each
    /// press, so a size the style rounded or clamped cannot make the buttons stick.
    /// </summary>
    private double _fontSize = AnnotationStyle.DefaultFontSize;

    /// <summary>Which shape the fill segments are currently drawn as, and whether yet.</summary>
    private bool _shapeFillIsOval;

    private bool _shapeFillDrawn;

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

        // The whole toolbar in one call: WinUI passes both of these down the tree, and the
        // strips and the options row are all inside this control. The popovers are not —
        // they hang off the popup root — and take theirs from the application's dictionary.
        AppFonts.Adopt(this);

        BuildOptionsRow();

        _surface.Children.Add(_optionsRow);
        _surface.Children.Add(_tools);
        _surface.Children.Add(_actions);
        Content = _surface;

        _tools.ItemInvoked += Strip_ItemInvoked;
        _actions.ItemInvoked += Strip_ItemInvoked;
        _tools.ItemAlternate += Item_Alternate;
        _actions.ItemAlternate += Item_Alternate;
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
    /// What the first badge of this capture counts from. Read by the canvas when it places
    /// one; the toolbar owns it because it is the row's control, and the canvas has no
    /// settings file of its own.
    /// </summary>
    public int NumberStartAt => double.IsNaN(_numberStart.Value) ? 1 : (int)_numberStart.Value;

    /// <summary>Whether a highlighter stroke should land on the text it was drawn across.</summary>
    public bool SmartMarker => _smartMarker.IsChecked == true;

    /// <summary>Whether a censor drag should cover only the text found inside the region.</summary>
    public bool CensorTextOnly => _censorScope.SelectedIndex == 1;

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

        _loupeZoom.ValueChanged += (_, _) =>
        {
            ShowZoomValue();
            ApplyStyle();
        };

        _spotlightDim.ValueChanged += (_, _) =>
        {
            ShowDimValue();
            ApplyStyle();
            RestyleSpotlights();
        };

        _spotlightBorder.SelectionChanged += (_, index) =>
        {
            if (_editor is { } editor)
            {
                editor.SpotlightBorder = index == 1 ? LineStyle.Dashed : LineStyle.Solid;
            }

            Remember(current => current with { SpotlightBorderDashed = index == 1 });
            RestyleSpotlights();
        };

        _numberFormat.SelectionChanged += (_, _) => ApplyStyle();

        _numberStart.ValueChanged += (_, _) =>
        {
            // Cleared or half-typed, the box reports NaN. Left alone rather than snapped
            // back to 1, which would fight a user in the middle of typing "12".
            if (double.IsNaN(_numberStart.Value))
            {
                return;
            }

            Remember(current => current with { NumberStartAt = (int)_numberStart.Value });
        };

        // Out to the host, because the pixels these read are the host's: the whole
        // screenshot under an overlay, the image being edited in an editor. Both are
        // offered here as well as on the action strip — the moment somebody reaches for the
        // redaction tool is the moment they would take an offer to do the whole job.
        _redactAllText.Click += (_, _) => CommandInvoked?.Invoke(this, ToolbarCommand.RedactAllText);
        _redactPii.Click += (_, _) => CommandInvoked?.Invoke(this, ToolbarCommand.Redact);
        _redactFaces.Click += (_, _) => CommandInvoked?.Invoke(this, ToolbarCommand.RedactFaces);
        _redactPeople.Click += (_, _) => CommandInvoked?.Invoke(this, ToolbarCommand.RedactPeople);

        _measureUnit.SelectionChanged += (_, _) => ApplyStyle();

        // Straight onto the editor beside the settings write, the way the smoothing and the
        // pressure switch go: it decides where a drag may reach rather than how the mark is
        // drawn, so there is nothing about it for the style to carry.
        _clampRuler.Checked += (_, _) => ShowRulerClamp(true);
        _clampRuler.Unchecked += (_, _) => ShowRulerClamp(false);
        _censorMode.SelectionChanged += (_, _) => ApplyStyle();
        _censorScope.SelectionChanged += (_, index) => Remember(
            current => current with { CensorTextOnly = index == 1 });

        _smartMarker.Checked += (_, _) => Remember(current => current with { SmartMarker = true });
        _smartMarker.Unchecked += (_, _) => Remember(current => current with { SmartMarker = false });

        // Straight onto the editor, the way Smoothing is: it changes how the next gesture
        // is recorded rather than how any mark is styled, so it does not belong in the
        // style write ApplyStyle performs.
        _pencilPressure.Checked += (_, _) => ShowPressure(true);
        _pencilPressure.Unchecked += (_, _) => ShowPressure(false);

        _lineStyle.SelectionChanged += (_, _) => ApplyStyle();
        _arrowStyle.SelectionChanged += (_, _) => ApplyStyle();
        _shapeFill.SelectionChanged += (_, _) => ApplyStyle();
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
    /// What a right-click on a button opens.
    /// </summary>
    /// <remarks>
    /// One handler for both strips, told apart by whether the button holds a tool rather
    /// than by which strip raised it. They were two handlers split by strip, and Beautify
    /// is the case that proves the strip cannot be what decides: macshot keeps it on the
    /// bottom row among the tools — <c>ToolbarDefinitions.swift:87</c> — while the branch
    /// that opens its backgrounds was written into the action strip's handler, behind a
    /// lookup in that strip which could never find it. The one gesture that reaches the
    /// backgrounds during a capture did nothing at all.
    /// </remarks>
    private void Item_Alternate(object? sender, ToolbarItem item)
    {
        // The button itself, which is what a menu has to hang off. It arrives as the
        // sender rather than being looked up, because a lookup has to be told where.
        if (sender is not FrameworkElement anchor)
        {
            return;
        }

        if (item.Tool is { } tool)
        {
            ShowToolMenu(anchor, tool, item);
        }
        else
        {
            ShowActionMenu(anchor, item);
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
    private void ShowToolMenu(FrameworkElement anchor, AnnotationTool tool, ToolbarItem item)
    {
        if (_settings is not { } settings)
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
    /// The menu behind a button that does something to the capture rather than draw on
    /// it. Three have one: Save holds the way of saving that is not the default, Beautify
    /// its backgrounds, Upload its confirmation. macshot's own arrangement — one press
    /// for the usual answer, the menu for the other one.
    /// </summary>
    private void ShowActionMenu(FrameworkElement anchor, ToolbarItem item)
    {
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
        new Flyout
        {
            Content = _effectsPicker,
            FlyoutPresenterStyle = ToolbarPalette.BareFlyoutStyle,
        }.ShowAt(anchor);
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

        // The loupe alone writes a bare number, which is macshot's own reading of it
        // (ToolOptionsRowView.swift:324): the width of a magnifier is a measurement of the
        // circle rather than of anything in the capture, and the box has to be wider
        // because it reaches three digits where a stroke cannot. Everything else says px,
        // including the stamp, whose size is a count of capture pixels like any other.
        var bare = tool == AnnotationTool.Loupe;
        _sizeValue.Width = bare ? 32 : 28;
        _sizeValue.Tag = bare ? string.Empty : "px";
        SyncSizeSlider(tool);

        _lineStyle.Visibility = Show(AnnotationToolOptions.UsesLineStyle(tool));
        _arrowStyle.Visibility = Show(AnnotationToolOptions.UsesArrowStyle(tool));
        _flipArrow.Visibility = _arrowStyle.Visibility;

        var fillable = AnnotationToolOptions.UsesShapeFill(tool);
        _shapeFill.Visibility = Show(fillable);
        if (fillable)
        {
            ShowShapeFillSegments(tool == AnnotationTool.Ellipse);
        }

        _outline.Visibility = Show(AnnotationToolOptions.UsesOutline(tool));

        var rounds = Show(AnnotationToolOptions.UsesCornerRadius(tool));
        _cornerLabel.Visibility = rounds;
        _cornerRadius.Visibility = rounds;
        _cornerValue.Visibility = rounds;
        var stamping = Show(AnnotationToolOptions.UsesStamp(tool));
        _stamp.Visibility = stamping;
        foreach (var pick in _quickStamps)
        {
            pick.Visibility = stamping;
        }

        _smoothing.Visibility = Show(AnnotationEditor.IsFreeform(tool));
        _pencilPressure.Visibility = Show(AnnotationToolOptions.UsesPressure(tool));
        _smartMarker.Visibility = Show(AnnotationToolOptions.UsesSmartSnap(tool));
        _censorMode.Visibility = Show(AnnotationToolOptions.UsesCensorMode(tool));

        var scoped = Show(AnnotationToolOptions.UsesCensorScope(tool));
        _drawLabel.Visibility = scoped;
        _censorScope.Visibility = scoped;

        var automatic = Show(AnnotationToolOptions.UsesAutoRedact(tool));
        _autoLabel.Visibility = automatic;
        _redactAllText.Visibility = automatic;
        _redactPii.Visibility = automatic;
        _redactFaces.Visibility = automatic;
        _redactPeople.Visibility = automatic;

        var counted = Show(AnnotationToolOptions.UsesNumberFormat(tool));
        _numberFormat.Visibility = counted;
        _startLabel.Visibility = counted;
        _numberStart.Visibility = counted;

        _measureUnit.Visibility = Show(AnnotationToolOptions.UsesMeasureUnit(tool));
        _clampRuler.Visibility = Show(AnnotationToolOptions.UsesMeasureClamp(tool));
        _measureHint.Visibility = _clampRuler.Visibility;

        var magnified = Show(AnnotationToolOptions.UsesLoupeMagnification(tool));
        _zoomLabel.Visibility = magnified;
        _loupeZoom.Visibility = magnified;
        _zoomValue.Visibility = magnified;

        var dimmed = Show(AnnotationToolOptions.UsesDimStrength(tool));
        _dimLabel.Visibility = dimmed;
        _spotlightDim.Visibility = dimmed;
        _dimValue.Visibility = dimmed;

        _spotlightBorder.Visibility = Show(AnnotationToolOptions.UsesSpotlightBorder(tool));

        var typesetting = Show(AnnotationToolOptions.UsesTypesetting(tool));
        _font.Visibility = typesetting;
        foreach (var toggle in TextStyleToggles)
        {
            toggle.Visibility = typesetting;
        }

        _textAlignment.Visibility = typesetting;
        _fontSmaller.Visibility = typesetting;
        _fontSizeValue.Visibility = typesetting;
        _fontLarger.Visibility = typesetting;
        _textFill.Visibility = typesetting;
        _textOutline.Visibility = typesetting;
        _textGlyphStroke.Visibility = typesetting;

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
    /// Writes the magnification into the box after its slider, to one decimal.
    /// </summary>
    /// <remarks>
    /// Its own formatter rather than <see cref="ShowSliderValue"/>, because this is the
    /// one slider on the row whose useful range is smaller than the gap between two whole
    /// numbers: rounded to an integer, half its positions would read the same.
    /// </remarks>
    private void ShowZoomValue() => _zoomValue.Text = string.Create(
        System.Globalization.CultureInfo.CurrentCulture,
        $"{_loupeZoom.Value:0.0}x");

    /// <summary>Writes the dim into the box after its slider, as macshot's percentage.</summary>
    private void ShowDimValue() => _dimValue.Text = string.Create(
        System.Globalization.CultureInfo.CurrentCulture,
        $"{Math.Round(_spotlightDim.Value * 100)}%");

    /// <summary>
    /// Brings every spotlight already on the canvas to what the row now says — both its
    /// strength and its border.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one control on this row that reaches back to marks already placed, and the
    /// reason is that the dim is not a property of a spotlight the way a stroke width is a
    /// property of a line. It is one layer over the whole capture, rendered from the
    /// strongest spotlight on it — so a slider that only styled the next one would leave
    /// the user dragging it with nothing happening, and would then jump the whole picture
    /// the moment a second spotlight was drawn. macshot applies it to all of them for the
    /// same reason (<c>ToolOptionsRowView.swift:1535-1546</c>).
    /// </para>
    /// <para>
    /// Amended rather than replaced, so this does not become a step to take back: the user
    /// is adjusting one thing they can see, and Ctrl+Z should undo the spotlight, not walk
    /// back through every strength it was dragged past on the way.
    /// </para>
    /// </remarks>
    private void RestyleSpotlights()
    {
        if (_isLoadingStyle || _editor is not { } editor)
        {
            return;
        }

        var strength = editor.Style.DimOpacity;
        var border = editor.SpotlightBorder;
        var stale = editor.Document.Annotations
            .Where(mark => mark.Tool == AnnotationTool.Highlight
                && (mark.Style.DimOpacity != strength || mark.Style.LineStyle != border))
            .ToList();

        foreach (var spotlight in stale)
        {
            editor.Document.Amend(spotlight with
            {
                Style = spotlight.Style with { DimOpacity = strength, LineStyle = border },
            });
        }

        // Only when something moved. The document's own Changed event is not what the
        // canvas listens to — the hosts redraw on this one — and raising it for a drag
        // that amended nothing would relayout the chrome on every tick of the slider.
        if (stale.Count > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Turns pen pressure on or off for the next stroke, and remembers it.</summary>
    private void ShowPressure(bool wanted)
    {
        if (_editor is { } editor)
        {
            editor.PenPressure = wanted;
        }

        Remember(current => current with { PencilPressure = wanted });
    }

    /// <summary>
    /// Writes down which kinds of secret are no longer wanted.
    /// </summary>
    /// <remarks>
    /// Stored as what is switched off rather than what is on, so a version that learns to
    /// spot a new kind covers it for everyone. The other way round, a list written before
    /// the pattern existed could not name it, and every existing user would silently keep
    /// publishing that one — which on this feature is a leak rather than a missing button.
    /// </remarks>
    private void RememberPiiKinds() => Remember(current => current with
    {
        HiddenPiiKinds =
        [
            .. _piiKinds.Where(entry => !entry.Item.IsChecked).Select(entry => entry.Kind.ToString()),
        ],
    });

    /// <summary>Holds the ruler inside the region, or lets it out, and remembers which.</summary>
    private void ShowRulerClamp(bool wanted)
    {
        if (_editor is { } editor)
        {
            editor.ClampRulerToRegion = wanted;
        }

        Remember(current => current with { MeasureClampToSelection = wanted });
    }

    /// <summary>
    /// Points the one size slider at whichever number the tool in hand is sized by.
    /// </summary>
    /// <remarks>
    /// The loupe is sized by <see cref="AnnotationStyle.LoupeSize"/>, the stamp by
    /// <see cref="AnnotationStyle.StampSize"/>, and everything else by its stroke width —
    /// which is the whole reason they are separate numbers: a loupe 120 across must not
    /// leave the next arrow 120 pixels thick. Reloading rather than rescaling, so switching
    /// tools and back returns each to what it was. The label is not here at all: macshot
    /// does not offer it this slider, and its size is set by the row's own − and +.
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
            if (tool == AnnotationTool.Loupe)
            {
                _size.Minimum = AnnotationStyle.MinLoupeSize;
                _size.Maximum = AnnotationStyle.MaxLoupeSize;
                _size.Value = Math.Clamp(
                    editor.Style.LoupeSize,
                    AnnotationStyle.MinLoupeSize,
                    AnnotationStyle.MaxLoupeSize);
            }
            else if (tool == AnnotationTool.Stamp)
            {
                _size.Minimum = AnnotationStyle.MinStampSize;
                _size.Maximum = AnnotationStyle.MaxStampSize;
                _size.Value = Math.Clamp(
                    editor.Style.StampSize,
                    AnnotationStyle.MinStampSize,
                    AnnotationStyle.MaxStampSize);
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
    /// Moves the label's size one point and shows where it landed.
    /// </summary>
    /// <remarks>
    /// The bounds come from <see cref="AnnotationStyle"/> rather than from the buttons, so
    /// holding − at 8 sits still instead of walking the size negative and clamping it back
    /// on the way out through the style.
    /// </remarks>
    private void StepFontSize(int steps)
    {
        _fontSize = AnnotationStyle.StepFontSize(_fontSize, steps);
        ShowFontSize();
        ApplyStyle();
    }

    private void ShowFontSize() => _fontSizeValue.Text = $"{(int)Math.Round(_fontSize)}";

    /// <summary>
    /// Repaints what was drawn from a brush of its own rather than from the palette's
    /// shared ones. Those follow a colour change on their own; these were made on the spot
    /// and have to be asked.
    /// </summary>
    private void RepaintChrome()
    {
        _sizeLabel.Foreground = ToolbarPalette.IconBrush(0.4);
        _cornerLabel.Foreground = ToolbarPalette.IconBrush(0.4);
        _zoomLabel.Foreground = ToolbarPalette.IconBrush(0.4);
        _startLabel.Foreground = ToolbarPalette.IconBrush(0.4);
        _drawLabel.Foreground = ToolbarPalette.IconBrush(0.4);
        _autoLabel.Foreground = ToolbarPalette.IconBrush(0.4);
        _sizeValue.Foreground = ToolbarPalette.IconBrush(0.6);
        _cornerValue.Foreground = ToolbarPalette.IconBrush(0.6);
        _zoomValue.Foreground = ToolbarPalette.IconBrush(0.6);

        // A shade brighter than the readouts beside sliders, as macshot has it: this one
        // is not a reading of where a thumb sits, it is the size itself.
        _fontSizeValue.Foreground = ToolbarPalette.IconBrush(0.7);
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

    /// <summary>
    /// One of the label's four style switches, at macshot's 26 by 22 with its letter set
    /// semibold — <c>ToolOptionsRowView.swift:934–939</c>.
    /// </summary>
    /// <remarks>
    /// A plain <see cref="ToggleButton"/> rather than a control written for this, because
    /// the row already has three of them: the Fill, Outline and Stroke labels beside it are
    /// each one. WinUI's own checked state is the accent fill macshot paints by hand, so
    /// writing it again would be writing the same button twice.
    /// </remarks>
    private static ToggleButton TextStyleToggle(string letter, string name)
    {
        var toggle = new ToggleButton
        {
            Content = letter,
            Width = 26,
            Height = 22,
            MinWidth = 0,
            Padding = new Thickness(0),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // The letter is the whole label, and B says nothing to anyone who has not met a
        // word processor. macshot leans on the same convention and can afford to: it is
        // the one control on the row with no room for a word.
        ToolTipService.SetToolTip(toggle, name);
        return toggle;
    }

    /// <summary>
    /// The − or the + beside the label's size. A <see cref="RepeatButton"/> at macshot's
    /// own delay and interval, so holding one walks the size the way holding it does there
    /// rather than moving it a single point per press.
    /// </summary>
    /// <summary>
    /// One of the automatic redactions on the censor row, at the size macshot gives all
    /// four (<c>ToolOptionsRowView.swift:1260-1262</c>).
    /// </summary>
    /// <remarks>
    /// A factory rather than four declarations, because the point of macshot's own
    /// <c>addRedactButton</c> is that they are the same size: they are alternatives to one
    /// another, and four buttons of four widths would read as four unrelated commands.
    /// </remarks>
    private static Button RedactButton(string label) => new()
    {
        Content = label,
        FontSize = OptionValueSize,
        MinWidth = 0,
        Padding = new Thickness(8, 2, 8, 2),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static RepeatButton FontSizeButton(string sign) => new()
    {
        Content = sign,
        Width = 20,
        Height = 22,
        MinWidth = 0,
        Padding = new Thickness(0),
        FontSize = 14,
        VerticalAlignment = VerticalAlignment.Center,

        // macshot's 0.3 then 0.05 — ToolOptionsRowView.swift:973.
        Delay = 300,
        Interval = 50,
    };

    /// <summary>
    /// One slider on the options row, at macshot's width and centred on it.
    /// </summary>
    /// <remarks>
    /// No height is set, and that is the point. WinUI lays a Slider out as three stacked
    /// rows — a spacer, the track, a spacer — sized from <c>SliderPreContentMargin</c> and
    /// its post twin. Given less height than those three need, the Grid pays the leading
    /// row in full and starves the trailing one, which puts the track below the middle of
    /// the control and hangs the thumb off the bottom. macshot's own 20 was copied here as
    /// a Height and produced exactly that: every slider on the row sitting low, clipped
    /// underneath. Left to size itself the track is centred in its own box, and the box is
    /// centred on the row, which is what the 20 was for.
    /// </remarks>
    private static Slider OptionSlider(double width, double minimum, double maximum) => new()
    {
        Width = width,
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

        ToolTipService.SetToolTip(_shapeFill, "How the shape is filled");
        ShowShapeFillSegments(oval: false);
        ToolTipService.SetToolTip(_arrowStyle, "Arrow ends");
        ToolTipService.SetToolTip(_stamp, "Stamp");

        // Drawn rather than named: macshot's segments carry a picture of the mark you are
        // about to make, which is both quicker to read than "Dashed" and one click rather
        // than a combo's two.
        _lineStyle.SetSegments([.. Enum.GetValues<LineStyle>().Select(style =>
            new StyleSegment(StylePreviews.Line(style), null, StylePreviews.LineSegmentWidth))]);

        _arrowStyle.SetSegments([.. Enum.GetValues<ArrowStyle>().Select(style =>
            new StyleSegment(StylePreviews.Arrow(style), null, StylePreviews.ArrowSegmentWidth))]);

        // The same two previews the dash picker draws, so a solid ring and a solid line are
        // shown by the same picture — this is a narrower choice, not a different one.
        _spotlightBorder.SetSegments(
        [
            new StyleSegment(StylePreviews.Line(LineStyle.Solid), null, StylePreviews.LineSegmentWidth),
            new StyleSegment(StylePreviews.Line(LineStyle.Dashed), null, StylePreviews.LineSegmentWidth),
        ]);
        ToolTipService.SetToolTip(_spotlightBorder, "How the edge of the spotlight is drawn");

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

        // The censor tool's two options. There is deliberately no strength beside them:
        // how much of a redaction survives is not a thing to leave to a slider.
        //
        // The four names are left in English, which is macshot's own choice for this one
        // control: it builds these segments from CensorMode.label, a plain string, where
        // every other label on its row goes through the strings file. They name the effect
        // rather than describe it — a reader who knows what a pixelated screenshot looks
        // like knows which segment says Pixelate — and translating them here would have
        // the two products showing different words for the same four buttons.
        ToolTipService.SetToolTip(_censorMode, "How the region is covered");
        _censorMode.SetSegments([.. Enum.GetValues<CensorMode>().Select(mode =>
            new StyleSegment(null, mode.ToString(), 0))]);

        ToolTipService.SetToolTip(_censorScope, "What inside the region is covered");
        _censorScope.SetSegments(
        [
            new StyleSegment(null, L("All"), 0),
            new StyleSegment(null, L("Text Only"), 0),
        ]);

        // The kinds the PII button looks for, behind its arrow — macshot's own dropdown
        // (ToolOptionsRowView.swift:1267-1270). Built once and ticked from the settings
        // when the toolbar binds: nothing else in the app writes them, so the menu cannot
        // fall out of step with what it last wrote.
        var kinds = new MenuFlyout();
        foreach (var kind in PiiKinds.Order)
        {
            var item = new ToggleMenuFlyoutItem { Text = PiiKinds.Label(kind), IsChecked = true };
            item.Click += (_, _) => RememberPiiKinds();
            kinds.Items.Add(item);
            _piiKinds.Add((kind, item));
        }

        _redactPii.Flyout = kinds;

        // The glyphs themselves rather than the names of the formats: "1 I A a" is read
        // at a glance and needs no translating, where "Roman" and "Lowercase letters"
        // would need both a wider row and a round trip through the strings file.
        ToolTipService.SetToolTip(_numberFormat, "What the badges count in");
        _numberFormat.SetSegments(
        [
            new StyleSegment(null, "1", 0),
            new StyleSegment(null, "I", 0),
            new StyleSegment(null, "A", 0),
            new StyleSegment(null, "a", 0),
        ]);

        // Both readings of the same span. Which one a number means has to be said, so the
        // ruler says it — and the choice is here rather than in the settings window
        // because it is made while looking at what is being measured.
        ToolTipService.SetToolTip(_measureUnit, "What the ruler reports");
        _measureUnit.SetSegments(
        [
            new StyleSegment(null, "px", 0),
            new StyleSegment(null, "pt", 0),
        ]);

        // Tenths, where every other slider on this row steps in whole units: the useful
        // range is 1.1 to 6, and whole steps would offer five of them.
        _loupeZoom.StepFrequency = 0.1;
        ToolTipService.SetToolTip(_loupeZoom, "How much the loupe enlarges");

        // Hundredths, because the box after it reads in whole percent and a coarser step
        // would skip numbers the user can see written there.
        _spotlightDim.StepFrequency = 0.01;
        ToolTipService.SetToolTip(_spotlightDim, "How far down the spotlight takes the rest");

        // Populated from StampGlyph.Choices so the picker and the renderer cannot offer
        // different sets.
        _stampChoices.ItemsSource = StampGlyph.Choices;
        _stamp.Content = StampEmoji;
        _stamp.Flyout = new Flyout { Content = _stampChoices };

        foreach (var emoji in StampGlyph.Quick)
        {
            var pick = QuickStamp(emoji);

            // The emoji is captured rather than read back off the button, so a change to
            // how these are drawn cannot quietly change what they stamp.
            pick.Click += (_, _) => ChooseStamp(emoji);
            _quickStamps.Add(pick);
        }

        // The label's own four controls. macshot puts them on this row and nowhere else,
        // which is right: a face and a fill are chosen while looking at the label, not in
        // a settings window opened afterwards.
        ToolTipService.SetToolTip(_font, "Typeface");
        _font.Flyout = new Flyout { Content = _fontChoices };
        _fontChoices.SelectionChanged += FontChoice_Changed;

        foreach (var toggle in TextStyleToggles)
        {
            toggle.Checked += (_, _) => ApplyStyle();
            toggle.Unchecked += (_, _) => ApplyStyle();
        }

        // The picture of the alignment rather than its name, for the reason the dash picker
        // draws a dash: four ragged rules are read at a glance and need no translating.
        ToolTipService.SetToolTip(_textAlignment, "Which edge the label's lines hang from");
        _textAlignment.SetSegments([.. Enum.GetValues<LabelAlignment>().Select(alignment =>
            new StyleSegment(StylePreviews.Align(alignment), null, StylePreviews.AlignSegmentWidth))]);
        _textAlignment.SelectionChanged += (_, _) => ApplyStyle();

        // Tabular figures, so 8 and 88 occupy the same width and the + does not shuffle
        // sideways as the size is walked past ten and a hundred.
        Typography.SetNumeralAlignment(_fontSizeValue, FontNumeralAlignment.Tabular);
        ToolTipService.SetToolTip(_fontSmaller, "Smaller");
        ToolTipService.SetToolTip(_fontLarger, "Larger");
        _fontSmaller.Click += (_, _) => StepFontSize(-1);
        _fontLarger.Click += (_, _) => StepFontSize(1);

        _outline.Toggled += (_, _) => ApplyStyle();
        _outline.SwatchPressed += (_, _) => PickSwatchColor(_outline);
        _textFill.Toggled += (_, _) => ApplyStyle();
        _textOutline.Toggled += (_, _) => ApplyStyle();
        _textGlyphStroke.Toggled += (_, _) => ApplyStyle();
        _textFill.SwatchPressed += (_, _) => PickSwatchColor(_textFill);
        _textOutline.SwatchPressed += (_, _) => PickSwatchColor(_textOutline);
        _textGlyphStroke.SwatchPressed += (_, _) => PickSwatchColor(_textGlyphStroke);

        // macshot's order, group for group: stroke, line style, arrow ends, the shape's
        // corner, the outline, and Flip last — ToolOptionsRowView.swift:124–180, 265–270.
        // Flip used to sit beside the arrow ends, which put it where macshot puts the
        // outline: the two rows read as different toolbars at a glance, which is the one
        // thing the order is for.
        AddGroup(_sizeLabel, _size, _sizeValue);
        AddGroup(_zoomLabel, _loupeZoom, _zoomValue);

        // The spotlight's two, in macshot's order: the dim, then the border, and for this
        // tool the row holds nothing else (ToolOptionsRowView.swift:133-141).
        AddGroup(_dimLabel, _spotlightDim, _dimValue);
        AddGroup(_spotlightBorder);
        AddGroup(_lineStyle);
        AddGroup(_arrowStyle);
        AddGroup(_shapeFill);
        AddGroup(_cornerLabel, _cornerRadius, _cornerValue);

        // The badge's two before the halo rather than after it, because macshot puts the
        // halo last on that tool as well as on the shapes (:238-241 then :266-270) — and
        // this is one sequence for every tool, so the badge's groups have to be somewhere
        // the shapes do not mind. They are: nothing but the badge ever shows them.
        AddGroup(_numberFormat);
        AddGroup(_startLabel, _numberStart);
        AddGroup(_outline);
        AddGroup(_flipArrow);
        AddGroup(_smoothing);
        AddGroup(_pencilPressure);
        AddGroup(_smartMarker);
        AddGroup(_censorMode);
        AddGroup(_drawLabel, _censorScope);
        AddGroup(_autoLabel, _redactAllText, _redactPii, _redactFaces, _redactPeople);

        // One group, so no hairline comes between them: macshot runs the unit straight into
        // the switch with nothing between (ToolOptionsRowView.swift:1125-1136), because the
        // two are the whole of what it asks about a ruler.
        AddGroup(_measureUnit, _clampRuler, _measureHint);

        // The label's own row, group for group as macshot builds it: the face beside the
        // four style switches, then the alignment, then the size, then the three colours —
        // behind the label, around that, and on the glyphs (ToolOptionsRowView.swift:902–1087).
        AddGroup(_font, _bold, _italic, _underline, _strikethrough);
        AddGroup(_textAlignment);
        AddGroup(_fontSmaller, _fontSizeValue, _fontLarger);
        AddGroup(_textFill, _textOutline, _textGlyphStroke);

        // The quick row first and the picker behind its own rule, which is macshot's order
        // (ToolOptionsRowView.swift:1183-1208): the seventeen you can reach, then the way
        // to the rest.
        AddGroup([.. _quickStamps]);
        AddGroup(_stamp);
    }

    /// <summary>
    /// Draws the fill segments as the shape they apply to.
    /// </summary>
    /// <remarks>
    /// Rebuilt only when the shape changes. The chosen segment survives it — SetSegments
    /// keeps the index and repaints — so switching between the rectangle and the ellipse
    /// does not quietly reset a user who had asked for a filled one.
    /// </remarks>
    private void ShowShapeFillSegments(bool oval)
    {
        if (_shapeFillDrawn && _shapeFillIsOval == oval)
        {
            return;
        }

        _shapeFillIsOval = oval;
        _shapeFillDrawn = true;

        _shapeFill.SetSegments([.. Enum.GetValues<ShapeFill>().Select(style =>
            new StyleSegment(
                StylePreviews.ShapeFillPreview(style, oval),
                null,
                StylePreviews.ShapeFillSegmentWidth))]);
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
            _shapeFill.SelectedIndex = (int)_loadedStyle.ShapeFill;
            _flipArrow.IsChecked = _loadedStyle.ArrowReversed;
            _outline.Show(_loadedStyle.Outline is not null, ToUiColor(
                _loadedStyle.Outline ?? new AnnotationColor(255, 255, 255)));
            // Every slider filled in first, and every readout written after — in that
            // order, and not interleaved. A readout written before its slider was loaded
            // reports the slider's Minimum, which is how the spotlight's dim came up
            // reading 10% on a capture where it was set to 55: the box was written while
            // the slider still sat at the bottom of its range, and the assignment that
            // moved it raised nothing to correct the box with, because a value change
            // during a load is suppressed on purpose.
            _size.Value = _loadedStyle.StrokeWidth;
            _cornerRadius.Value = _loadedStyle.CornerRadius;
            _loupeZoom.Value = _loadedStyle.LoupeMagnification;
            _spotlightDim.Value = _loadedStyle.DimOpacity;

            ShowSliderValue(_size, _sizeValue);
            ShowSliderValue(_cornerRadius, _cornerValue);
            ShowZoomValue();
            ShowDimValue();

            _colorPicker.Color = ToUiColor(_loadedStyle.Color);

            // Read here rather than by the editor itself, so Core stays free of the
            // settings file and this stays the one place the toolbar's state comes from.
            editor.Smoothing = settings.Current.PencilSmoothing;
            _smoothing.SelectedIndex = (int)editor.Smoothing;
            editor.SnapGuides = settings.Current.SnapGuides;
            editor.SpotlightBorder = settings.Current.SpotlightBorderDashed
                ? LineStyle.Dashed
                : LineStyle.Solid;
            _spotlightBorder.SelectedIndex = settings.Current.SpotlightBorderDashed ? 1 : 0;
            editor.PenPressure = settings.Current.PencilPressure;
            _pencilPressure.IsChecked = editor.PenPressure;
            editor.ClampRulerToRegion = settings.Current.MeasureClampToSelection;
            _clampRuler.IsChecked = editor.ClampRulerToRegion;
            _smartMarker.IsChecked = settings.Current.SmartMarker;
            _censorMode.SelectedIndex = (int)_loadedStyle.CensorMode;
            _censorScope.SelectedIndex = settings.Current.CensorTextOnly ? 1 : 0;

            var wanted = settings.Current.RedactedPiiKinds();
            foreach (var (kind, item) in _piiKinds)
            {
                item.IsChecked = wanted.Contains(kind);
            }

            _numberFormat.SelectedIndex = (int)_loadedStyle.NumberFormat;
            _numberStart.Value = settings.Current.NumberStartAt;

            _measureUnit.SelectedIndex = _loadedStyle.MeasureInPoints ? 1 : 0;

            _fontChoices.Show(_loadedStyle.FontFamily);
            _font.Content = string.IsNullOrWhiteSpace(_loadedStyle.FontFamily)
                ? FontPickerView.SystemFace
                : _loadedStyle.FontFamily;
            _bold.IsChecked = _loadedStyle.Bold;
            _italic.IsChecked = _loadedStyle.Italic;
            _underline.IsChecked = _loadedStyle.Underline;
            _strikethrough.IsChecked = _loadedStyle.Strikethrough;
            _textAlignment.SelectedIndex = (int)_loadedStyle.TextAlignment;

            _fontSize = _loadedStyle.FontSize;
            ShowFontSize();

            // A fill or an outline that is switched off still has a colour, so turning it
            // back on gives back the one that was there rather than an arbitrary black.
            _textFill.Show(_loadedStyle.TextBackground is not null, ToUiColor(
                _loadedStyle.TextBackground ?? new AnnotationColor(0, 0, 0, 160)));
            _textOutline.Show(_loadedStyle.TextOutline is not null, ToUiColor(
                _loadedStyle.TextOutline ?? new AnnotationColor(255, 255, 255)));
            _textGlyphStroke.Show(_loadedStyle.TextGlyphStroke is not null, ToUiColor(
                _loadedStyle.TextGlyphStroke ?? new AnnotationColor(255, 255, 255)));
        }
        finally
        {
            _isLoadingStyle = false;
        }
    }

    private void StampChoice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_stampChoices.SelectedItem is string emoji)
        {
            ChooseStamp(emoji);
        }
    }

    /// <summary>
    /// Takes <paramref name="emoji"/> as the mark the stamp tool places, from the row or
    /// from the picker behind it.
    /// </summary>
    private void ChooseStamp(string emoji)
    {
        StampEmoji = emoji;
        _stamp.Content = emoji;
        _stamp.Flyout?.Hide();

        // Picking a stamp is asking to stamp: leaving the previous tool active would
        // make the choice look like it did nothing.
        SelectTool(AnnotationTool.Stamp);
    }

    /// <summary>
    /// One emoji on the row: the glyph and nothing round it.
    /// </summary>
    /// <remarks>
    /// Stripped of the button's slab and its padding because seventeen of them sit side by
    /// side, and seventeen chrome boxes would be a second toolbar inside the row. macshot's
    /// size — 26 square, the glyph a little under it (<c>ToolOptionsRowView.swift:1185-1191</c>).
    /// The minimums go with the padding: WinUI's own floor is 32, which would silently
    /// ignore the height asked for here.
    /// </remarks>
    private static Button QuickStamp(string emoji) => new()
    {
        Content = emoji,
        FontFamily = new FontFamily("Segoe UI Emoji"),
        FontSize = 16,
        Width = 26,
        Height = 26,
        MinWidth = 0,
        MinHeight = 0,
        Padding = new Thickness(0),
        BorderThickness = new Thickness(0),
        Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
        VerticalAlignment = VerticalAlignment.Center,
    };

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

        // The one slider is the loupe's width, the stamp's size, or a stroke width,
        // depending on what is in hand — so the two numbers it is not writing are carried
        // across untouched rather than being overwritten with a reading of the other one.
        // The label is on the list as well even though it has a size control of its own:
        // its slider is hidden rather than absent, and a hidden control must not be able
        // to write anything.
        var typesetting = editor.Tool == AnnotationTool.Text;
        var magnifying = editor.Tool == AnnotationTool.Loupe;
        var stamping = editor.Tool == AnnotationTool.Stamp;

        editor.Style = new AnnotationStyle(
            new AnnotationColor(color.R, color.G, color.B, color.A),
            typesetting || magnifying || stamping
                ? previous.StrokeWidth
                : Math.Max(MinStroke, _size.Value),
            _lineStyle.SelectedIndex >= 0 ? (LineStyle)_lineStyle.SelectedIndex : LineStyle.Solid,
            ArrowStyle: _arrowStyle.SelectedIndex >= 0
                ? (ArrowStyle)_arrowStyle.SelectedIndex
                : ArrowStyle.Filled,
            CornerRadius: Math.Max(0, _cornerRadius.Value),
            CensorMode: _censorMode.SelectedIndex >= 0
                ? (CensorMode)_censorMode.SelectedIndex
                : CensorMode.Pixelate,
            ShapeFill: _shapeFill.SelectedIndex >= 0
                ? (ShapeFill)_shapeFill.SelectedIndex
                : ShapeFill.Stroke)
        {
            // Its own number rather than the width slider's, which is what the label's size
            // used to be read off: sizing a label must not resize the next arrow.
            FontSize = Math.Clamp(_fontSize, AnnotationStyle.MinFontSize, AnnotationStyle.MaxFontSize),

            // Kept rather than cleared when the picker has no row for it: a family this
            // machine does not have still names the face the file asked for, and
            // dropping it would silently rewrite the setting on the first capture.
            FontFamily = _fontChoices.SelectedItem is null
                ? previous.FontFamily
                : FontPickerView.FamilyOf(_fontChoices.SelectedItem),
            Bold = _bold.IsChecked == true,
            Italic = _italic.IsChecked == true,
            Underline = _underline.IsChecked == true,
            Strikethrough = _strikethrough.IsChecked == true,
            TextAlignment = _textAlignment.SelectedIndex >= 0
                ? (LabelAlignment)_textAlignment.SelectedIndex
                : LabelAlignment.Left,
            ArrowReversed = _flipArrow.IsChecked == true,
            NumberFormat = _numberFormat.SelectedIndex >= 0
                ? (NumberFormat)_numberFormat.SelectedIndex
                : NumberFormat.Decimal,
            MeasureInPoints = _measureUnit.SelectedIndex == 1,
            LoupeMagnification = Math.Clamp(
                _loupeZoom.Value,
                AnnotationStyle.MinLoupeMagnification,
                AnnotationStyle.MaxLoupeMagnification),
            LoupeSize = magnifying
                ? Math.Clamp(_size.Value, AnnotationStyle.MinLoupeSize, AnnotationStyle.MaxLoupeSize)
                : previous.LoupeSize,
            StampSize = stamping
                ? Math.Clamp(_size.Value, AnnotationStyle.MinStampSize, AnnotationStyle.MaxStampSize)
                : previous.StampSize,
            DimOpacity = Math.Clamp(
                _spotlightDim.Value,
                AnnotationStyle.MinDimOpacity,
                AnnotationStyle.MaxDimOpacity),
            Outline = _outline.IsOn ? ToAnnotationColor(_outline.Color) : null,
            TextBackground = _textFill.IsOn ? ToAnnotationColor(_textFill.Color) : null,
            TextOutline = _textOutline.IsOn ? ToAnnotationColor(_textOutline.Color) : null,
            TextGlyphStroke = _textGlyphStroke.IsOn ? ToAnnotationColor(_textGlyphStroke.Color) : null,
        };

        _tools.ShowSwatch(ToUiColor(editor.Style.Color));
    }

    /// <summary>
    /// The label's four style switches, for the places that treat them alike — showing
    /// them, hiding them, listening to them. They are named fields rather than an array
    /// because everywhere else asks for one of them by name, and <c>[2]</c> is not a name.
    /// </summary>
    private ToggleButton[] TextStyleToggles => [_bold, _italic, _underline, _strikethrough];

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
