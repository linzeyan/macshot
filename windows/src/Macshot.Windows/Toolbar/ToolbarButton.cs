using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using static Macshot.Windows.Services.Localization;

using Windows.Foundation;
using Windows.UI;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// One square button on a toolbar strip.
/// </summary>
/// <remarks>
/// <para>
/// Not a <see cref="Button"/>. A themed Windows button brings its own background,
/// border, corner radius, focus ring and press animation, and the whole point here is
/// the look macshot has on macOS: a flat 32-point square that is transparent until the
/// pointer is over it, the accent colour while it is the tool in hand, and nothing else.
/// Restyling a Button down to that is more markup than drawing it.
/// </para>
/// <para>
/// The states are the four <c>ToolbarButtonView</c> draws: pressed, selected, hovered,
/// and plain, in that order of precedence.
/// </para>
/// </remarks>
internal sealed partial class ToolbarButton : UserControl
{
    /// <summary>
    /// The side of the corner triangle that marks a button with a menu behind it, and how
    /// far in from the corner it sits. macOS's 4 and 3.
    /// </summary>
    private const double MarkSize = 4;

    private const double MarkInset = 3;

    /// <summary>
    /// Where the face sits among the surface's children: over the microphone meter, under
    /// the menu mark. Named because <see cref="SetFace"/> replaces that child by position,
    /// and the meter going in below it moved the position by one.
    /// </summary>
    private const int FaceIndex = 1;

    private readonly Border _surface;

    /// <summary>Holds the face, the meter under it and the menu mark over its corner.</summary>
    private readonly Grid _content;

    private readonly Border _level;
    private readonly Polygon _menuMark;
    private bool _isHovered;
    private bool _isPressed;

    public ToolbarButton(ToolbarItem item)
    {
        Item = item;

        _level = LevelMeter();
        _menuMark = MenuMark();
        _content = new Grid();
        _content.Children.Add(_level);
        _content.Children.Add(FaceOf(item));
        _content.Children.Add(_menuMark);

        _surface = new Border
        {
            Width = ToolbarPalette.ButtonSize,
            Height = ToolbarPalette.ButtonSize,
            CornerRadius = new CornerRadius(ToolbarPalette.ButtonRadius),

            // Transparent rather than unset: a Border with no background is not hit
            // testable, so the gaps between the icon's strokes would swallow the click.
            Background = ToolbarPalette.TransparentBrush,
            Child = _content,
        };

        Width = ToolbarPalette.ButtonSize;
        Height = ToolbarPalette.ButtonSize;
        base.Content = _surface;

        ToolTipService.SetToolTip(this, AppFonts.Tip(Hint(item)));

        PointerEntered += (_, _) => { _isHovered = true; Repaint(); };
        PointerExited += (_, _) => { _isHovered = false; _isPressed = false; Repaint(); };
        PointerPressed += Surface_PointerPressed;
        PointerReleased += Surface_PointerReleased;
        PointerCaptureLost += (_, _) => { _isPressed = false; Repaint(); };
        RightTapped += (_, _) => Alternate?.Invoke(this, Item);

        Repaint();
    }

    /// <summary>Raised on a click.</summary>
    public event EventHandler<ToolbarItem>? Invoked;

    /// <summary>Raised on a right-click, for the buttons that offer a second choice.</summary>
    public event EventHandler<ToolbarItem>? Alternate;

    public ToolbarItem Item { get; private set; }

    /// <summary>
    /// Updates the button in place. Rebuilding the strip on every tool change would
    /// throw away the button under the pointer, and the hover would be lost with it.
    /// </summary>
    public void Update(ToolbarItem item)
    {
        var iconChanged = item.Command != Item.Command || item.Tool != Item.Tool;
        var hintChanged = iconChanged || !string.Equals(item.Shortcut, Item.Shortcut, StringComparison.Ordinal);
        Item = item;

        // A reused slot that has stopped being the microphone must not keep its meter:
        // the strips reuse buttons rather than rebuilding them, so a bar left standing
        // would end up under whatever icon took the position. macshot clears it in
        // configure(with:) for the same reason.
        if (item.Command != ToolbarCommand.MicAudio)
        {
            SetLevel(0);
        }

        if (iconChanged)
        {
            SetFace(FaceOf(item));
        }

        if (hintChanged)
        {
            ToolTipService.SetToolTip(this, AppFonts.Tip(Hint(item)));
        }

        Repaint();
    }

    /// <summary>The colour the swatch button shows, for the one button that is a colour.</summary>
    public void ShowSwatch(Color color)
    {
        if (Item.Command != ToolbarCommand.PickColor)
        {
            return;
        }

        SetFace(Swatch(color));
    }

    /// <summary>
    /// Shows how loud the microphone is, on the one button that is the microphone.
    /// </summary>
    /// <remarks>
    /// Guarded here rather than at the caller for the same reason <see cref="ShowSwatch"/>
    /// is: the strip hands the reading to every button it has and lets the one it is about
    /// take it, so nothing outside has to know where on the strip the microphone ended up.
    /// </remarks>
    public void ShowLevel(double level)
    {
        if (Item.Command == ToolbarCommand.MicAudio)
        {
            SetLevel(level);
        }
    }

    /// <summary>
    /// Puts a new face in, leaving the meter under it and the menu mark over it. Replaced
    /// rather than the whole child, so neither has to be rebuilt when an icon changes.
    /// </summary>
    private void SetFace(UIElement face)
    {
        _content.Children.RemoveAt(FaceIndex);
        _content.Children.Insert(FaceIndex, face);
    }

    /// <summary>
    /// Sizes the meter, and takes it away entirely below the level nothing should be drawn
    /// at — a hairline of green over a silent room says the microphone hears something.
    /// </summary>
    private void SetLevel(double level)
    {
        var height = Math.Clamp(level, 0, 1) * ToolbarPalette.ButtonSize;

        _level.Height = height;
        _level.Visibility = level < MicrophoneLevel.Silent ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Surface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // The right button opens the menu and must not also work the button: RightTapped
        // is a gesture built on top of these events rather than instead of them, so
        // without this a right-click on the microphone opened its device menu and turned
        // the microphone on underneath it — and a right-click on Save saved.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isPressed = true;
        Repaint();

        // Captured so a press that wanders off the button still ends here rather than
        // leaving it stuck looking pressed.
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
            Invoked?.Invoke(this, Item);
        }
    }

    private void Repaint()
    {
        // The tools have a menu behind them — right-clicking one offers to take it off the
        // strip — and so do the three below. Right-clicking anything else does nothing, and
        // without the mark there is nothing at all to say which is which; macOS draws the
        // same triangle for the same reason, on the same buttons.
        _menuMark.Visibility = Item.Tool is null && !HasMenu(Item.Command)
            ? Visibility.Collapsed
            : Visibility.Visible;

        _surface.Background = _isPressed
            ? ToolbarPalette.PressedBrush
            : Item.IsSelected
                ? ToolbarPalette.AccentBrush
                : _isHovered
                    ? ToolbarPalette.HoverBrush
                    : ToolbarPalette.TransparentBrush;
    }

    /// <summary>
    /// Which buttons that are not tools carry a menu: Save offers the way of saving that
    /// is not the default, and the microphone and the camera offer which device they open.
    /// macshot marks the same three (<c>ToolbarDefinitions.swift:445</c>, <c>:458</c>,
    /// <c>:506</c>).
    /// </summary>
    private static bool HasMenu(ToolbarCommand command) =>
        command is ToolbarCommand.Save or ToolbarCommand.MicAudio or ToolbarCommand.Webcam;

    /// <summary>
    /// What the tooltip says: the button's name, and the key that does the same thing when
    /// there is one to name.
    /// </summary>
    /// <remarks>
    /// The key is appended after translation rather than folded into the tooltip text,
    /// because macshot keys its translations on the exact English it ships — "Pencil" has
    /// a translation and "Pencil (P)" never would.
    /// </remarks>
    private static string Hint(ToolbarItem item) =>
        item.Shortcut.Length == 0
            ? L(item.Tooltip)
            : $"{L(item.Tooltip)} ({item.Shortcut})";

    /// <summary>
    /// What the button shows: its icon, or the colour itself for the one button that is a
    /// colour. Not called Content — that is the property this control inherits, and a
    /// method of the same name hides it.
    /// </summary>
    private static UIElement FaceOf(ToolbarItem item) =>
        ToolbarIcons.For(item) ?? Swatch(ToolbarPalette.Icon);

    /// <summary>
    /// The microphone meter: a bar of green rising from the bottom of the button, behind
    /// the icon.
    /// </summary>
    /// <remarks>
    /// The bottom corners are rounded on the meter itself rather than left to the button
    /// it sits in, because a WinUI <see cref="Border"/> does not clip what it holds to its
    /// own corner radius — an unrounded bar would square off the two corners of the button
    /// the moment the microphone heard anything. The top is left square: at full scale it
    /// is the one edge that reaches a rounded corner, and a quarter of a pixel there is not
    /// worth a second geometry.
    /// </remarks>
    private static Border LevelMeter() => new()
    {
        CornerRadius = new CornerRadius(0, 0, ToolbarPalette.ButtonRadius, ToolbarPalette.ButtonRadius),
        Background = ToolbarPalette.LevelBrush,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Bottom,
        Height = 0,
        Visibility = Visibility.Collapsed,

        // A reading, not a target: the click it would swallow is the one that turns the
        // microphone off, and it covers the bottom of the button it belongs to.
        IsHitTestVisible = false,
    };

    /// <summary>
    /// The triangle in the bottom-right corner of a button that has a menu behind it: a
    /// right angle at the corner, the hypotenuse running up and to the right.
    /// </summary>
    private static Polygon MenuMark()
    {
        var mark = new Polygon
        {
            Width = MarkSize,
            Height = MarkSize,
            Fill = ToolbarPalette.IconBrush(0.4),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, MarkInset, MarkInset),

            // The mark is a hint, not a target: the right-click it stands for works
            // anywhere on the button, and a shape that took the pointer would put a hole
            // in the middle of the one it is drawn on.
            IsHitTestVisible = false,
        };

        mark.Points.Add(new Point(0, MarkSize));
        mark.Points.Add(new Point(MarkSize, MarkSize));
        mark.Points.Add(new Point(MarkSize, 0));
        return mark;
    }

    /// <summary>
    /// The colour button: the colour itself, inset, with a hairline border so a colour
    /// close to the strip's own is still a square rather than a hole in it.
    /// </summary>
    private static Border Swatch(Color color) => new()
    {
        Width = ToolbarPalette.ButtonSize - 12,
        Height = ToolbarPalette.ButtonSize - 12,
        CornerRadius = new CornerRadius(4),
        Background = new SolidColorBrush(color),
        BorderThickness = new Thickness(0.5),
        BorderBrush = ToolbarPalette.IconBrush(0.4),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
