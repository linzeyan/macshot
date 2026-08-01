using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Output;

/// <summary>
/// The shape of the history panel: a shallow strip hanging from the top of the screen
/// that captures are flicked through sideways.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>HistoryOverlayController</c>. Every number here is one of its constants,
/// kept in one place because the panel, its tab bar and its cards have to agree — the
/// card row starts where the tab bar ends, and a panel an inch taller would leave a band
/// of empty grey under the cards rather than more of them.
/// </para>
/// <para>
/// Shallow on purpose. A full window of history is a file browser, and choosing between
/// yesterday's captures is a glance, not a browse: one row that scrolls sideways puts the
/// answer on screen without covering what the user was doing.
/// </para>
/// </remarks>
public static class HistoryPanelLayout
{
    /// <summary>Tall enough for the tab bar and one row of cards, and no taller.</summary>
    public const double Height = 240;

    /// <summary>
    /// Past this the row is wider than it is useful — six cards is already more than a
    /// glance, and the rest is empty panel on an ultrawide.
    /// </summary>
    public const double MaxWidth = 1200;

    /// <summary>Clearance at each side, so the panel reads as floating over the screen.</summary>
    public const double ScreenInset = 20;

    public const double CornerRadius = 14;

    /// <summary>The band the filter tabs and the trash button sit in.</summary>
    public const double TabBarHeight = 50;

    public const double TabHeight = 26;
    public const double TabGap = 6;
    public const double TabPaddingHorizontal = 16;

    /// <summary>Clear of the panel edge, for the tabs, the trash and the card row alike.</summary>
    public const double SidePadding = 24;

    /// <summary>The gap between the tab bar and the first card.</summary>
    public const double CardTopGap = 8;

    public const double CardWidth = 200;
    public const double CardHeight = 160;
    public const double CardGap = 14;
    public const double CardCornerRadius = 10;

    /// <summary>How far the preview is held off the card edge.</summary>
    public const double CardInset = 8;

    /// <summary>The strip at the foot of a card that the size and the age are written in.</summary>
    public const double CardLabelHeight = 28;

    public const double TrashSide = 22;

    /// <summary>
    /// Where the panel goes: centred on the work area and hanging from the top of it.
    /// </summary>
    /// <remarks>
    /// The work area rather than the whole screen, because macshot hangs it under the
    /// menu bar and the taskbar is the same kind of obstacle. Returned in pixels, since
    /// that is what a window is positioned in.
    /// </remarks>
    public static (int X, int Y, int Width, int Height) For(CaptureRegion workArea, double scale)
    {
        var width = Math.Min(workArea.Width - (2 * ScreenInset * scale), MaxWidth * scale);
        var height = Height * scale;

        return (
            (int)Math.Round(workArea.X + ((workArea.Width - width) / 2)),
            (int)Math.Round(workArea.Y),
            (int)Math.Round(width),
            (int)Math.Round(height));
    }
}
