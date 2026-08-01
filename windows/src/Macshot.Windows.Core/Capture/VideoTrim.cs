using System.Globalization;

namespace Macshot.Windows.Core.Capture;

/// <summary>
/// Which part of a recording is being kept, and how the timeline reads it back.
/// </summary>
/// <param name="Start">Seconds from the beginning of the source.</param>
/// <param name="End">Seconds from the beginning of the source.</param>
public readonly record struct VideoTrim(double Start, double End)
{
    /// <summary>
    /// The shortest piece a handle may leave. macshot's own floor: a trim dragged to
    /// nothing exports a file with no frames in it, which every player refuses to open
    /// with an error about the file being corrupt.
    /// </summary>
    public const double MinimumSeconds = 0.1;

    public double Duration => Math.Max(0, End - Start);

    /// <summary>The whole of a recording <paramref name="seconds"/> long.</summary>
    public static VideoTrim Whole(double seconds) => new(0, Math.Max(0, seconds));

    /// <summary>Whether anything has actually been trimmed away.</summary>
    /// <remarks>
    /// A hundredth of a second of slack, because the handles are dragged in pixels and a
    /// timeline eight hundred pixels wide cannot express an exact zero on a long
    /// recording. Without it an untouched export would re-encode for no reason.
    /// </remarks>
    public bool IsWhole(double totalSeconds) =>
        Start <= 0.01 && End >= totalSeconds - 0.01;

    /// <summary>
    /// Moves the left handle, keeping it in front of the right one by at least
    /// <see cref="MinimumSeconds"/>.
    /// </summary>
    public VideoTrim WithStart(double start, double totalSeconds)
    {
        var limit = Math.Min(End, totalSeconds) - MinimumSeconds;
        return this with { Start = Math.Clamp(start, 0, Math.Max(0, limit)) };
    }

    /// <summary>Moves the right handle, keeping it behind the left one.</summary>
    public VideoTrim WithEnd(double end, double totalSeconds)
    {
        var floor = Start + MinimumSeconds;
        return this with { End = Math.Clamp(end, Math.Min(floor, totalSeconds), Math.Max(0, totalSeconds)) };
    }

    /// <summary>Where a moment sits inside the kept piece, clamped to it.</summary>
    public double Clamp(double seconds) => Math.Clamp(seconds, Start, Math.Max(Start, End));

    /// <summary>
    /// A moment as the timeline writes it: minutes and seconds, and hours only when
    /// there are any.
    /// </summary>
    /// <remarks>
    /// macshot's <c>formatTime</c>. Hours are left out below an hour rather than shown as
    /// 00, because almost every recording is under a minute and a leading 00: is two
    /// characters of noise on every one of them.
    /// </remarks>
    public static string Format(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        var whole = (int)seconds;
        var hours = whole / 3600;
        var minutes = whole % 3600 / 60;
        var rest = whole % 60;

        return hours > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, rest)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", minutes, rest);
    }
}
