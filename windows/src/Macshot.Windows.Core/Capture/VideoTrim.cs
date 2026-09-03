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
    /// A moment as the timeline writes it: minutes, seconds and tenths, with hours only
    /// when there are any.
    /// </summary>
    /// <remarks>
    /// <para>
    /// macshot's <c>formatTime</c>, tenths included: a trim handle moves in tenths and its
    /// floor is a tenth, so a reading that stops at whole seconds cannot say what the
    /// handle is doing for nine of every ten positions it can hold.
    /// </para>
    /// <para>
    /// Hours are left out below an hour rather than shown as 00, because almost every
    /// recording is under a minute and a leading 00: is two characters of noise on every
    /// one of them. macshot has no hour at all and counts on past 60 minutes, which reads
    /// worse the one time it happens.
    /// </para>
    /// </remarks>
    public static string Format(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        // Every field off one rounded total, which is macshot's own fix
        // (VideoEditorWindowController.swift:1291). Deriving the tenths on their own as
        // (seconds - floor(seconds)) * 10 truncates 2.3 — held as 2.2999… — to ".2", and
        // carries nowhere, so 1.999 read 0:01.9 rather than 0:02.0.
        var tenths = (long)Math.Round(seconds * 10, MidpointRounding.AwayFromZero);
        var hours = tenths / 36_000;
        var minutes = tenths / 600 % 60;
        var rest = tenths / 10 % 60;
        var fraction = tenths % 10;

        return hours > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}.{3}", hours, minutes, rest, fraction)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}.{2}", minutes, rest, fraction);
    }
}
