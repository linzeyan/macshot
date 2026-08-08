using Macshot.Windows.Core.Capture;
using Macshot.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using static Macshot.Windows.Services.Localization;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// The one control on the idle overlay: it picks the shape, or the exact size, the next
/// drag will come out as.
/// </summary>
/// <remarks>
/// <para>
/// It opens the same panel the size box's presets button does, because it is the same
/// catalogue asked a moment earlier — the difference is only that there is no region yet
/// for the answer to be applied to. macshot shares it the same way, minus the unit choice,
/// which has no size to be about (<c>OverlayView.swift:2803-2849</c>).
/// </para>
/// <para>
/// Drawn rather than restyled, like <see cref="ToolbarButton"/>: a themed Windows button
/// brings a background, a border, a focus ring and a press animation, and what is wanted
/// here is macshot's flat rounded square that stands quietly under the instruction until it
/// is holding something.
/// </para>
/// </remarks>
internal sealed partial class PreSelectionPresetButton : UserControl
{
    private readonly Border _surface;
    private readonly FontIcon _icon;
    private readonly ResolutionPresetsView _presetsView = new() { ShowsUnits = false };
    private readonly Flyout _flyout;
    private PreSelectionPreset _preset;
    private bool _isHovered;
    private bool _isPressed;

    public PreSelectionPresetButton()
    {
        _icon = new FontIcon
        {
            // The size box's presets button wears the same glyph for the same menu: two
            // controls opening one panel must not look like two different features.
            Glyph = "\uE799",
            FontSize = 14,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
        };

        _surface = new Border
        {
            Width = PreSelectionButtonPlacement.Width,
            Height = PreSelectionButtonPlacement.Height,
            CornerRadius = new CornerRadius(ToolbarPalette.ButtonRadius),
            BorderThickness = new Thickness(1),
            Child = _icon,
        };

        Width = PreSelectionButtonPlacement.Width;
        Height = PreSelectionButtonPlacement.Height;
        base.Content = _surface;

        _flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.Bottom,
            Content = _presetsView,

            // The panel paints its own dark slab; the default presenter would put a light
            // card round a control that stands over a screenshot.
            FlyoutPresenterStyle = ToolbarPalette.BareFlyoutStyle,
        };

        // Filled as it opens rather than once, so the tick lands on whatever is being held
        // at that moment. The size columns are ticked against the preset's own numbers,
        // which are nought unless it names a size — there is no region here whose size the
        // rows could be compared with.
        _flyout.Opening += (_, _) => _presetsView.Show(
            _preset.Ratio, _preset.Width, _preset.Height, KeepRatio, points: false);

        _presetsView.PresetPicked += (_, preset) =>
        {
            _flyout.Hide();
            PresetPicked?.Invoke(this, preset);
        };

        // Not dismissed on the switch: it is a switch rather than a choice, and closing the
        // panel would mean reopening it to see what it did.
        _presetsView.KeepRatioToggled += (_, on) => KeepRatioToggled?.Invoke(this, on);

        PointerEntered += (_, _) => { _isHovered = true; Repaint(); };
        PointerExited += (_, _) => { _isHovered = false; _isPressed = false; Repaint(); };
        PointerPressed += Surface_PointerPressed;
        PointerReleased += Surface_PointerReleased;
        PointerCaptureLost += (_, _) => { _isPressed = false; Repaint(); };

        Update(PreSelectionPreset.Freeform);
    }

    /// <summary>Raised when a shape or a size is chosen for the next drag.</summary>
    public event EventHandler<ResolutionPreset>? PresetPicked;

    /// <summary>Raised when the keep-ratio switch is moved, with its new state.</summary>
    public event EventHandler<bool>? KeepRatioToggled;

    /// <summary>Whether a picked shape is to outlive the capture it was picked on.</summary>
    public bool KeepRatio { get; set; }

    /// <summary>
    /// Shows what the next drag is being held to, and says so in the tooltip.
    /// </summary>
    /// <remarks>
    /// The tooltip is where the answer is: the button is one glyph, and nothing about an
    /// icon can say "16 : 9" — which is exactly the thing a user who left a preset on last
    /// week needs to be told before they drag.
    /// </remarks>
    public void Update(PreSelectionPreset preset)
    {
        _preset = preset;

        var title = L("Aspect ratio & resolution presets");
        ToolTipService.SetToolTip(
            this, AppFonts.Tip(preset.Label is { } label ? $"{title}: {label}" : title));

        Repaint();
    }

    private void Surface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isPressed = true;
        Repaint();

        // Captured so a press that wanders off the button still ends here, rather than
        // leaving it stuck looking pressed under an overlay that has taken the pointer.
        CapturePointer(e.Pointer);
    }

    private void Surface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var wasPressed = _isPressed;
        _isPressed = false;
        ReleasePointerCapture(e.Pointer);
        Repaint();

        if (wasPressed && _isHovered)
        {
            _flyout.ShowAt(this);
        }
    }

    private void Repaint()
    {
        var holding = _preset.Label is not null;

        _surface.Background = _isPressed
            ? ToolbarPalette.PressedBrush
            : _isHovered
                ? ToolbarPalette.HoverBrush

                // Faintly filled at rest rather than transparent: it stands on a black pill
                // with nothing beside it, and a bare glyph there reads as a label.
                : ToolbarPalette.IconBrush(0.07);

        _surface.BorderBrush = holding
            ? ToolbarPalette.AccentBrush
            : ToolbarPalette.IconBrush(0.18);

        _icon.Foreground = holding ? ToolbarPalette.AccentBrush : ToolbarPalette.IconBrush(0.88);
    }
}
