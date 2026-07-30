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
    public static Color Background { get; } = Color.FromArgb(255, 31, 31, 31);

    /// <summary>The selected button. macshot's purple, and the same one the grips use.</summary>
    public static Color Accent { get; } = Color.FromArgb(255, 140, 77, 217);

    /// <summary>Icons, and the text of anything that has no icon.</summary>
    public static Color Icon { get; } = Color.FromArgb(255, 255, 255, 255);

    /// <summary>A button under the pointer: the icon colour at a twelfth.</summary>
    public static Color Hover { get; } = Color.FromArgb(31, 255, 255, 255);

    /// <summary>A button being pressed: the accent, softened.</summary>
    public static Color Pressed { get; } = Color.FromArgb(153, 140, 77, 217);

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

    /// <summary>The icon colour at a given opacity, for icons drawn from several parts.</summary>
    public static SolidColorBrush IconBrush(double opacity = 1) =>
        new(Color.FromArgb((byte)Math.Clamp(Math.Round(255 * opacity), 0, 255), 255, 255, 255));

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
