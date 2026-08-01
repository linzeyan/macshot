using System.Globalization;

namespace Macshot.Windows.Core.Output;

/// <summary>
/// How long ago a capture was taken, in the words macshot uses.
/// </summary>
/// <remarks>
/// macshot's <c>timeAgoString</c>. It steps up a unit as each one runs out and gives up
/// on "ago" altogether after a day, because "31h ago" is arithmetic the reader has to do
/// and "Jul 31, 14:05" is not.
/// </remarks>
public static class TimeAgo
{
    /// <summary>
    /// The phrase, with <c>%d</c> left where the number goes so the caller can put it
    /// through the same lookup macshot's strings are keyed by.
    /// </summary>
    /// <remarks>
    /// The template and the number come back separately because a translation cannot be
    /// looked up after its number has been substituted in — "5m ago" is not a key, and
    /// "%dm ago" is.
    /// </remarks>
    public static (string Template, int Count) Phrase(DateTimeOffset taken, DateTimeOffset now)
    {
        var seconds = (int)(now - taken).TotalSeconds;

        // A capture from the future is a clock that moved, not a capture. Reading it as
        // "just now" is the only answer that is not nonsense.
        if (seconds < 5)
        {
            return ("just now", 0);
        }

        if (seconds < 60)
        {
            return ("%ds ago", seconds);
        }

        var minutes = seconds / 60;
        if (minutes < 60)
        {
            return ("%dm ago", minutes);
        }

        var hours = minutes / 60;
        return hours < 24 ? ("%dh ago", hours) : (string.Empty, 0);
    }

    /// <summary>
    /// The date macshot falls back to once a capture is more than a day old.
    /// </summary>
    public static string OnDate(DateTimeOffset taken) =>
        taken.ToLocalTime().ToString("MMM d, HH:mm", CultureInfo.CurrentCulture);
}
