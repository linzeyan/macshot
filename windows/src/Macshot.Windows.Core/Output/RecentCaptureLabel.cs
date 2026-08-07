using System.Globalization;

namespace Macshot.Windows.Core.Output;

/// <summary>
/// What the notification area's Recent Captures submenu calls one past capture.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>HistoryEntry.timeAgoString</c> and the title built from it
/// (<c>AppDelegate.swift:3180</c>): the size, then how long ago. Both halves are
/// load-bearing. A submenu of five clock times says nothing about which capture is
/// which — this port listed only the time, and picking the right one out of it meant
/// remembering when each was taken.
/// </para>
/// <para>
/// Localized through a delegate rather than by calling into the string table, because
/// that table lives in the WinUI half and this arithmetic is the part worth testing.
/// </para>
/// </remarks>
public static class RecentCaptureLabel
{
    /// <summary>
    /// Below this, the capture is "just now" rather than a count. macshot's 5: a capture
    /// taken while the menu was being opened should not read as "1s ago".
    /// </summary>
    private const int JustNowSeconds = 5;

    /// <summary>
    /// What a capture older than a day says instead of a count, which is macshot's own
    /// <c>MMM d, HH:mm</c>. Days are not offered as a unit: "3d ago" is where a date
    /// starts being the more useful answer.
    /// </summary>
    private const string OlderFormat = "MMM d, HH:mm";

    /// <summary>The size and the age, the way macshot writes them.</summary>
    public static string Of(
        int width,
        int height,
        DateTimeOffset takenAt,
        DateTimeOffset now,
        Func<string, string> localize) =>
        // The multiplication sign rather than a letter x, and two spaces either side of
        // the em dash, which is how macshot spaces this title.
        $"{width} × {height}  —  {Age(takenAt, now, localize)}";

    /// <summary>How long ago <paramref name="takenAt"/> was, as macshot phrases it.</summary>
    public static string Age(DateTimeOffset takenAt, DateTimeOffset now, Func<string, string> localize)
    {
        ArgumentNullException.ThrowIfNull(localize);

        // Truncated rather than rounded, so a capture 59.9 seconds old is still counted
        // in seconds — and a clock that moved backwards lands in "just now" rather than
        // reporting a negative count.
        var seconds = (int)(now - takenAt).TotalSeconds;

        if (seconds < JustNowSeconds)
        {
            return localize("just now");
        }

        if (seconds < 60)
        {
            return Count(localize("%ds ago"), seconds);
        }

        var minutes = seconds / 60;
        if (minutes < 60)
        {
            return Count(localize("%dm ago"), minutes);
        }

        var hours = minutes / 60;
        if (hours < 24)
        {
            return Count(localize("%dh ago"), hours);
        }

        return takenAt.ToLocalTime().ToString(OlderFormat, CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// The translated string carries macshot's printf placeholder, which no .NET formatter
    /// reads — so the number goes in by hand rather than through <c>string.Format</c>,
    /// which would leave "%ds ago" on screen.
    /// </summary>
    private static string Count(string template, int value) =>
        template.Replace("%d", value.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal);
}
