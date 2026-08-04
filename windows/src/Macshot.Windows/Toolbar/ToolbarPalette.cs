using Macshot.Windows.Core.Annotations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// The toolbar's colours and measurements, taken from the macOS app.
/// </summary>
/// <remarks>
/// <para>
/// These numbers are not a fresh design. macshot on Windows is the same product as
/// macshot on macOS written in another language, so a user who knows one must not have
/// to learn the other: the dark strip, the purple selected button, the 32-point square
/// buttons two points apart are all what <c>ToolbarLayout</c> and
/// <c>ToolbarButtonView</c> use there.
/// </para>
/// <para>
/// Fixed rather than theme-adaptive on purpose. The toolbar sits over a screenshot,
/// which can be any colour at all, and a bar that followed the system theme would be
/// white-on-white over half the captures anyone takes.
/// </para>
/// </remarks>
internal static class ToolbarPalette
{
    /// <summary>The strip itself: near-black, opaque, so icons read over any capture.</summary>
    public static Color Background { get; private set; } = ToUiColor(ToolbarColors.DefaultBackground);

    /// <summary>The selected button. macshot's purple, and the same one the grips use.</summary>
    public static Color Accent { get; private set; } = ToUiColor(ToolbarColors.DefaultAccent);

    /// <summary>Icons, and the text of anything that has no icon.</summary>
    public static Color Icon { get; private set; } = ToUiColor(ToolbarColors.DefaultIcon);

    /// <summary>A button under the pointer: the icon colour at a twelfth.</summary>
    public static Color Hover { get; private set; } = ToUiColor(ToolbarColors.Default.Hover);

    /// <summary>A button being pressed: the accent, softened.</summary>
    public static Color Pressed { get; private set; } = ToUiColor(ToolbarColors.Default.Pressed);

    public const double ButtonSize = 32;

    public const double ButtonRadius = 6;

    public const double StripPadding = 4;

    public const double StripSpacing = 2;

    public const double StripRadius = 6;

    /// <summary>The drawing area an icon is composed in, centred in a button.</summary>
    public const double IconExtent = 16;

    /// <summary>
    /// Built from a zero alpha rather than taken from the named palette: WinUI 3 keeps
    /// the Color struct in Windows.UI and moved the Colors table to Microsoft.UI, and
    /// inside namespace Macshot.Windows that is one import too easy to get wrong.
    /// </summary>
    public static SolidColorBrush TransparentBrush { get; } = new(Color.FromArgb(0, 0, 0, 0));

    public static SolidColorBrush BackgroundBrush { get; } = new(Background);

    public static SolidColorBrush AccentBrush { get; } = new(Accent);

    public static SolidColorBrush HoverBrush { get; } = new(Hover);

    public static SolidColorBrush PressedBrush { get; } = new(Pressed);

    /// <summary>
    /// The microphone meter's fill: macshot's <c>NSColor.systemGreen</c> at 0.45, which is
    /// what it floods the mic button with while the microphone is hearing something
    /// (<c>ToolbarButtonView.swift:93-100</c>).
    /// </summary>
    /// <remarks>
    /// Not one of the five the user can repaint, and not derived from them: this is a
    /// reading rather than decoration, and a meter tinted to match a strip somebody has
    /// turned green would say nothing at all.
    /// </remarks>
    public static SolidColorBrush LevelBrush { get; } = new(Color.FromArgb(115, 52, 199, 89));

    /// <summary>
    /// A flyout with no chrome of its own, for the popovers that paint their own slab.
    /// </summary>
    /// <remarks>
    /// macshot's popovers are the toolbar's dark background at an exact size. WinUI's
    /// presenter brings 12 of padding, a minimum width and a light card, all three of which
    /// read as a second popover drawn around the first.
    /// </remarks>
    public static Style BareFlyoutStyle
    {
        get
        {
            // A fresh Style each time: a Style may only be applied to one presenter, and
            // two of these popovers can be open at once on two displays.
            var bare = new Style(typeof(FlyoutPresenter));
            bare.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
            bare.Setters.Add(new Setter(FlyoutPresenter.MinWidthProperty, 0d));
            bare.Setters.Add(new Setter(FlyoutPresenter.BackgroundProperty, BackgroundBrush));
            return bare;
        }
    }

    /// <summary>The icon colour at a given opacity, for icons drawn from several parts.</summary>
    public static SolidColorBrush IconBrush(double opacity = 1) => new(Color.FromArgb(
        (byte)Math.Clamp(Math.Round(255 * opacity), 0, 255),
        Icon.R,
        Icon.G,
        Icon.B));

    /// <summary>
    /// Repaints the toolbar in the colours the user chose.
    /// </summary>
    /// <remarks>
    /// The brushes are changed rather than replaced, so everything already drawn from them
    /// follows without being rebuilt — an overlay open on another display, a toolbar mid
    /// hover. The icons drawn from <see cref="IconBrush"/> are the exception: those brushes
    /// were made on the spot, so they belong to whatever asked for them and are refreshed
    /// when that is rebuilt.
    /// </remarks>
    public static void Apply(ToolbarColors colors)
    {
        Background = ToUiColor(colors.Background);
        Accent = ToUiColor(colors.Accent);
        Icon = ToUiColor(colors.Icon);
        Hover = ToUiColor(colors.Hover);
        Pressed = ToUiColor(colors.Pressed);

        BackgroundBrush.Color = Background;
        AccentBrush.Color = Accent;
        HoverBrush.Color = Hover;
        PressedBrush.Color = Pressed;
    }

    private static Color ToUiColor(AnnotationColor color) =>
        Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

    /// <summary>
    /// How long a strip of <paramref name="count"/> buttons is, along the direction it
    /// runs. Worked out rather than measured, because where the strips go is decided
    /// before WinUI has laid anything out — and a strip placed from a stale measurement
    /// is a strip in the wrong place for one frame every time the selection moves.
    /// </summary>
    public static double StripLength(int count) => count <= 0
        ? 0
        : (StripPadding * 2) + (count * ButtonSize) + ((count - 1) * StripSpacing);

    /// <summary>How thick a strip is, across the direction it runs.</summary>
    public static double StripThickness => ButtonSize + (StripPadding * 2);
}
