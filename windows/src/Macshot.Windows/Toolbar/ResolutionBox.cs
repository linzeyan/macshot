using Macshot.Windows.Core.Capture;
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
/// The numbers are pixels, which is what the delivered image is measured in. Showing
/// layout units would mean the box and the file disagreed on every display that is not at
/// 100%.
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

        _width.Text = _shownWidth.ToString("0", System.Globalization.CultureInfo.CurrentCulture);
        _height.Text = _shownHeight.ToString("0", System.Globalization.CultureInfo.CurrentCulture);
    }

    private static TextBox Field()
    {
        var field = new TextBox
        {
            Width = FieldWidth,
            Height = FieldHeight,
            MinHeight = FieldHeight,
            Padding = new Thickness(4, 0, 4, 0),
            FontSize = FieldFontSize,
            FontWeight = FontWeights.Medium,
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

    private MenuFlyout BuildPresets()
    {
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.Bottom };

        foreach (var preset in ResolutionPresets.Ratios)
        {
            menu.Items.Add(Item(preset));
        }

        // The two lists do different things — one holds a shape, the other sets a size —
        // and a menu that ran them together would read as one list of fourteen sizes.
        menu.Items.Add(new MenuFlyoutSeparator());

        foreach (var preset in ResolutionPresets.Sizes)
        {
            menu.Items.Add(Item(preset));
        }

        return menu;
    }

    private MenuFlyoutItem Item(ResolutionPreset preset)
    {
        var item = new MenuFlyoutItem { Text = preset.Label };
        item.Click += (_, _) =>
        {
            PresetPicked?.Invoke(this, preset);
            EditingEnded?.Invoke(this, EventArgs.Empty);
        };

        return item;
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
        if (!double.TryParse(_width.Text, out var width) || !double.TryParse(_height.Text, out var height))
        {
            Show(_shownWidth, _shownHeight);
            return;
        }

        var edited = ReferenceEquals(source, _width)
            ? SizedDimension.Width
            : SizedDimension.Height;

        if (width == _shownWidth && height == _shownHeight)
        {
            return;
        }

        SizeCommitted?.Invoke(this, new SizeRequest(width, height, edited));
    }
}
