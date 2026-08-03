using Macshot.Windows.Core.Capture;
using Macshot.Windows.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;

using Windows.System;

namespace Macshot.Windows.Toolbar;

/// <summary>A size the user asked the selection to be, and which number they typed.</summary>
public readonly record struct SizeRequest(double Width, double Height, SizedDimension Edited);

/// <summary>
/// The width × height box that sits against the selection.
/// </summary>
/// <remarks>
/// <para>
/// It reads out and it takes dictation. A capture wanted at an exact size — a 1920×1080
/// slide, a 16 : 9 thumbnail — is otherwise dragged out by eye and then complained about
/// by whatever it is pasted into, and no amount of care with a mouse fixes that.
/// </para>
/// <para>
/// The numbers are pixels by default, which is what the delivered image is measured in —
/// on any display that is not at 100%, layout units and the file disagree. Points are
/// offered in the presets panel because that is what macOS reports and what a design handed
/// over in points is specified in, but the box says which it is showing and everything
/// behind it stays in pixels.
/// </para>
/// </remarks>
internal sealed partial class ResolutionBox : UserControl
{
    private const double FieldWidth = 56;
    private const double FieldHeight = 22;
    private const double Gap = 4;
    private const double Pad = 6;
    private const double TimesWidth = 12;
    private const double PresetsWidth = 30;

    /// <summary>
    /// The numbers, at macshot's size and weight — ResolutionBoxView.swift:67. Tabular
    /// figures with it, which is what monospacedDigitSystemFont means there: a width that
    /// changes as the digits change makes the reading jitter under a drag.
    /// </summary>
    private const double FieldFontSize = 12;

    /// <summary>The multiplication sign, a point larger than the numbers — `:46`.</summary>
    private const double TimesFontSize = 13;

    private readonly TextBox _width = Field();
    private readonly TextBox _height = Field();
    private readonly ResolutionPresetsView _presetsView = new();
    private readonly Button _presets = new()
    {
        Width = PresetsWidth,
        Height = FieldHeight,
        Padding = new Thickness(0),
        Content = "⌄",
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>What the fields last showed, so a commit can say which number changed.</summary>
    private double _shownWidth;
    private double _shownHeight;

    public ResolutionBox()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Gap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        row.Children.Add(_width);
        row.Children.Add(new TextBlock
        {
            Text = "×",
            Width = TimesWidth,
            FontSize = TimesFontSize,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ToolbarPalette.IconBrush(0.55),
        });

        row.Children.Add(_height);
        row.Children.Add(_presets);

        Content = new Border
        {
            Background = ToolbarPalette.BackgroundBrush,
            CornerRadius = new CornerRadius(ToolbarPalette.StripRadius),
            Padding = new Thickness(Pad),

            // Dark whatever the system theme is, like the toolbar it belongs with: this
            // sits over a screenshot, which can be any colour at all.
            RequestedTheme = ElementTheme.Dark,
            Child = row,
        };

        _presets.Flyout = BuildPresets();

        foreach (var field in new[] { _width, _height })
        {
            field.KeyDown += Field_KeyDown;
            field.LostFocus += (_, _) => Commit(field);
        }

        ToolTipService.SetToolTip(_presets, "Shapes and sizes");
    }

    /// <summary>Raised when a typed size is committed, with Enter or by looking away.</summary>
    public event EventHandler<SizeRequest>? SizeCommitted;

    /// <summary>Raised when a shape or a size is picked from the menu.</summary>
    public event EventHandler<ResolutionPreset>? PresetPicked;

    /// <summary>Raised when the keep-ratio switch is moved, with its new state.</summary>
    public event EventHandler<bool>? KeepRatioToggled;

    /// <summary>Raised when the unit is changed. True for points, false for pixels.</summary>
    public event EventHandler<bool>? UnitPicked;

    /// <summary>The shape being held, so the panel can tick the row that holds it.</summary>
    public double? LockedAspect { get; set; }

    /// <summary>Whether a picked shape is to outlive this capture.</summary>
    public bool KeepRatio { get; set; }

    /// <summary>
    /// Whether the reading is in layout points rather than device pixels.
    /// </summary>
    /// <remarks>
    /// The stored image is measured in pixels, so pixels is the honest default and the
    /// only unit the two ever agree on. Points are what macOS reports and what a design
    /// handed over in points is specified in, which is why the choice exists at all.
    /// </remarks>
    public bool ShowPoints { get; set; }

    /// <summary>
    /// How many device pixels a point is on the display this box is over. Only read when
    /// <see cref="ShowPoints"/> is on.
    /// </summary>
    /// <remarks>
    /// Not called Scale: <c>UIElement</c> already has one, and a property that quietly
    /// hides a base member is a property someone will one day set expecting the other.
    /// </remarks>
    public double PixelsPerPoint { get; set; } = 1;

    /// <summary>
    /// Raised when the box is done with the keyboard, so the overlay can take it back —
    /// otherwise Escape and the tool shortcuts would keep going to a text field.
    /// </summary>
    public event EventHandler? EditingEnded;

    /// <summary>
    /// True while a field is being typed into. The selection must not be re-read into the
    /// fields underneath the user's hands, and the box must not be moved out from under
    /// the caret.
    /// </summary>
    public bool IsEditing => _width.FocusState != FocusState.Unfocused
        || _height.FocusState != FocusState.Unfocused;

    /// <summary>How big the box is once laid out, worked out rather than measured.</summary>
    public CaptureRegion PreferredSize { get; } = new(
        0,
        0,
        (Pad * 2) + (FieldWidth * 2) + TimesWidth + PresetsWidth + (Gap * 3),
        (Pad * 2) + FieldHeight);

    /// <summary>
    /// How far from the left edge the middle of the "W × H" reading is. The presets button
    /// hangs off the right, and centring the whole box would leave the numbers visibly off
    /// to one side of the region they describe.
    /// </summary>
    public double DimensionsCenter { get; } = Pad + FieldWidth + Gap + (TimesWidth / 2);

    /// <summary>
    /// Shows a size, unless the user is in the middle of typing one.
    /// </summary>
    public void Show(double width, double height)
    {
        _shownWidth = Math.Round(width);
        _shownHeight = Math.Round(height);

        if (IsEditing)
        {
            return;
        }

        _width.Text = InTheChosenUnit(_shownWidth);
        _height.Text = InTheChosenUnit(_shownHeight);
    }

    /// <summary>
    /// A pixel count as the fields show it: itself, or divided down into points.
    /// </summary>
    /// <remarks>
    /// Rounded to a whole number in either unit, because a size box that reads 1279.5 is a
    /// size box nobody can type back into it.
    /// </remarks>
    private string InTheChosenUnit(double pixels)
    {
        var shown = ShowPoints && PixelsPerPoint > 0 ? pixels / PixelsPerPoint : pixels;
        return Math.Round(shown).ToString("0", System.Globalization.CultureInfo.CurrentCulture);
    }

    /// <summary>A number typed into a field, back in the pixels everything else works in.</summary>
    private double InPixels(double typed) => ShowPoints && PixelsPerPoint > 0 ? typed * PixelsPerPoint : typed;

    private static TextBox Field()
    {
        var field = new TextBox
        {
            Width = FieldWidth,
            Height = FieldHeight,
            MinHeight = FieldHeight,
            Padding = new Thickness(4, 0, 4, 0),
            FontSize = FieldFontSize,
            FontWeight = AppFonts.Heavier(FontWeights.Medium),
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        // The whole reason macshot sets a monospaced-digit font here: the numbers are
        // rewritten on every pointer move during a drag, and figures of different widths
        // make the reading dance.
        Typography.SetNumeralAlignment(field, FontNumeralAlignment.Tabular);
        return field;
    }

    private Flyout BuildPresets()
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.Bottom,
            Content = _presetsView,

            // The panel brings its own padding and its own dark background; the default
            // chrome would put a light card round a control that sits over a screenshot.
            FlyoutPresenterStyle = ToolbarPalette.BareFlyoutStyle,
        };

        // Filled as it opens rather than once: the tick goes on whatever shape is being
        // held right now, and the two footer controls show what is stored right now.
        flyout.Opening += (_, _) => _presetsView.Show(
            LockedAspect,
            _shownWidth,
            _shownHeight,
            KeepRatio,
            ShowPoints);

        _presetsView.PresetPicked += (_, preset) =>
        {
            flyout.Hide();
            PresetPicked?.Invoke(this, preset);
            EditingEnded?.Invoke(this, EventArgs.Empty);
        };

        // The footer does not dismiss: both are switches rather than choices, and closing
        // the panel on a switch would mean reopening it to see what the switch did.
        _presetsView.KeepRatioToggled += (_, on) => KeepRatioToggled?.Invoke(this, on);
        _presetsView.UnitPicked += (_, points) => UnitPicked?.Invoke(this, points);

        return flyout;
    }

    private void Field_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                // Handled, or it would reach the overlay and finish the capture — which
                // is what Enter means everywhere else on this window.
                e.Handled = true;
                Commit((TextBox)sender);
                EditingEnded?.Invoke(this, EventArgs.Empty);
                return;

            case VirtualKey.Escape:
                // The same reasoning the other way: Escape here gives up the number being
                // typed, not the capture it is a measurement of.
                e.Handled = true;
                Show(_shownWidth, _shownHeight);
                EditingEnded?.Invoke(this, EventArgs.Empty);
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// Reads both fields and asks for the size, naming the one that changed so a held
    /// shape knows which number to work the other out from.
    /// </summary>
    private void Commit(TextBox source)
    {
        if (!double.TryParse(_width.Text, out var typedWidth) || !double.TryParse(_height.Text, out var typedHeight))
        {
            Show(_shownWidth, _shownHeight);
            return;
        }

        var edited = ReferenceEquals(source, _width)
            ? SizedDimension.Width
            : SizedDimension.Height;

        // Compared and raised in pixels, whichever unit was typed: the region, the presets
        // and the delivered image are all measured in pixels, and only these two fields
        // are ever in anything else.
        var width = InPixels(typedWidth);
        var height = InPixels(typedHeight);

        if (width == _shownWidth && height == _shownHeight)
        {
            return;
        }

        SizeCommitted?.Invoke(this, new SizeRequest(width, height, edited));
    }
}
