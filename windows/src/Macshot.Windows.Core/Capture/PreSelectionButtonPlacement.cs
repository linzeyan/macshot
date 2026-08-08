namespace Macshot.Windows.Core.Capture;

/// <summary>
/// Where the button that shapes the next drag sits on the idle overlay.
/// </summary>
/// <remarks>
/// <para>
/// Inside the instruction pill, along its bottom edge, which is where macshot puts it
/// (<c>OverlayView.swift:2236-2254</c>): the pill grows by the button's height plus a gap,
/// and the button takes that reserved strip. It belongs there rather than floating on its
/// own because it is only ever offered while the instruction is — macshot hides the two
/// together (<c>:1648-1663</c>), and a lone button in the middle of an empty screen says
/// nothing about what it would do.
/// </para>
/// <para>
/// Its own arithmetic rather than a third child of the pill, because the pill is not hit
/// testable: it stands over the middle of the display, and a press there has to start a
/// drag. Only the button's own rectangle may take a click.
/// </para>
/// <para>Top-left origin, unlike the AppKit original, so "the bottom" is the larger Y.</para>
/// </remarks>
public static class PreSelectionButtonPlacement
{
    /// <summary>The button's size (<c>OverlayView.swift:2233</c>).</summary>
    public const double Width = 34;

    public const double Height = 28;

    /// <summary>How far the button sits below the last line of the instruction (<c>:2234</c>).</summary>
    public const double Gap = 10;

    /// <summary>The pill's padding round what it holds (<c>:2232</c>).</summary>
    public const double Padding = 14;

    /// <summary>
    /// How much taller the pill has to be to carry a button of <paramref name="height"/>.
    /// </summary>
    public static double Reserved(double height) => height + Gap;

    /// <summary>
    /// How wide the pill has to be for the button to fit inside its padding. macshot takes
    /// the widest of its two lines and the button (<c>:2238</c>); the instruction is the
    /// wider of the three in every language it ships, so this only ever binds on a pill
    /// carrying something unusually short.
    /// </summary>
    public static double LeastWidth(double width, double padding = Padding) =>
        width + (padding * 2);

    /// <summary>
    /// Places a button of <paramref name="size"/> in the strip <see cref="Reserved"/> left
    /// for it at the bottom of <paramref name="pill"/>.
    /// </summary>
    /// <param name="pill">The instruction pill, where it has already been placed.</param>
    /// <param name="size">How big the button is; its position is ignored.</param>
    /// <param name="padding">The pill's own padding, which the button sits inside.</param>
    public static CaptureRegion For(CaptureRegion pill, CaptureRegion size, double padding = Padding) =>
        new(
            pill.X + ((pill.Width - size.Width) / 2),
            pill.Bottom - padding - size.Height,
            size.Width,
            size.Height);
}
