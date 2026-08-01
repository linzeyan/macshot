namespace Macshot.Windows.Core.Capture;

/// <summary>
/// One of the capture commands at the top of the notification-area menu.
/// </summary>
/// <remarks>
/// macshot's <c>CaptureMenuItemID</c>, with macshot's own identifiers as the names: the
/// order is stored as a list of these, and using macshot's spelling means a settings file
/// written by either product describes the same menu.
/// </remarks>
public enum CaptureMenuItem
{
    CaptureArea,

    CaptureScreen,

    CaptureOcr,

    QuickCapture,

    CaptureLastArea,

    ScrollCapture,
}

/// <summary>
/// The order the capture commands are offered in, which the user may rearrange.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>CaptureMenuItemID.orderedItems</c> and <c>saveOrder</c>. Only these six
/// move: they are the ways of starting a capture, and putting the one you use most at the
/// top is worth a setting when the menu is the app's front door. Everything below them —
/// the recordings, the history, the openers, the settings — keeps its place, because that
/// part of the menu is not a list of alternatives.
/// </para>
/// <para>
/// A stored order is repaired rather than trusted: unknown names are dropped, repeats are
/// dropped, and anything missing is added back at the end in the default order. A settings
/// file from a version that had five of these, or that someone edited by hand, therefore
/// still produces a menu with every command in it exactly once.
/// </para>
/// </remarks>
public static class CaptureMenuItems
{
    /// <summary>macshot's <c>defaultOrder</c>, which is the order its menu is written in.</summary>
    public static IReadOnlyList<CaptureMenuItem> DefaultOrder { get; } =
    [
        CaptureMenuItem.CaptureArea,
        CaptureMenuItem.CaptureScreen,
        CaptureMenuItem.CaptureOcr,
        CaptureMenuItem.QuickCapture,
        CaptureMenuItem.CaptureLastArea,
        CaptureMenuItem.ScrollCapture,
    ];

    /// <summary>
    /// What the menu and the settings list call each one. macshot's own menu strings, so
    /// they resolve against its translations.
    /// </summary>
    public static string Label(CaptureMenuItem item) => item switch
    {
        CaptureMenuItem.CaptureArea => "Capture Area",
        CaptureMenuItem.CaptureScreen => "Capture Screen",
        CaptureMenuItem.CaptureOcr => "Capture OCR & QR",
        CaptureMenuItem.QuickCapture => "Quick Capture",
        CaptureMenuItem.CaptureLastArea => "Capture Last Area",
        CaptureMenuItem.ScrollCapture => "Scroll Capture",
        _ => item.ToString(),
    };

    /// <summary>
    /// The stored order, repaired: every item, once each, in the order it was left in.
    /// </summary>
    public static IReadOnlyList<CaptureMenuItem> Resolve(IEnumerable<string>? stored)
    {
        var order = new List<CaptureMenuItem>(DefaultOrder.Count);

        foreach (var name in stored ?? [])
        {
            if (Enum.TryParse<CaptureMenuItem>(name, ignoreCase: true, out var item)
                && !order.Contains(item))
            {
                order.Add(item);
            }
        }

        order.AddRange(DefaultOrder.Where(item => !order.Contains(item)));
        return order;
    }

    /// <summary>
    /// How an order is written down. Completed the same way <see cref="Resolve"/> reads
    /// it, so what is stored is always the whole menu rather than the part that was
    /// moved.
    /// </summary>
    public static IReadOnlyList<string> Store(IEnumerable<CaptureMenuItem> order) =>
        [.. Resolve(order.Select(item => item.ToString())).Select(item => item.ToString())];

    /// <summary>
    /// The order with one item moved by one place, or the same order when it cannot go
    /// any further. Answering the unchanged list is what leaves the topmost item's Up
    /// button doing nothing rather than wrapping it round to the bottom.
    /// </summary>
    public static IReadOnlyList<CaptureMenuItem> Move(
        IReadOnlyList<CaptureMenuItem> order,
        int index,
        int by)
    {
        ArgumentNullException.ThrowIfNull(order);

        var target = index + by;
        if (index < 0 || index >= order.Count || target < 0 || target >= order.Count)
        {
            return order;
        }

        var moved = new List<CaptureMenuItem>(order);
        (moved[index], moved[target]) = (moved[target], moved[index]);
        return moved;
    }
}
