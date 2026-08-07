namespace Macshot.Windows.Core.Output;

/// <summary>
/// What a screen recording is written as.
/// </summary>
/// <remarks>
/// The same pair macOS offers, and for the same reason: MP4 is what a recording
/// should be, and GIF is what some places still only take.
/// </remarks>
public enum RecordingFormat
{
    Mp4,
    Gif,
}

public static class RecordingFormatExtensions
{
    public static string FileExtension(this RecordingFormat format) => format switch
    {
        RecordingFormat.Mp4 => ".mp4",
        RecordingFormat.Gif => ".gif",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown recording format."),
    };

    /// <summary>
    /// What to call the format where a user reads it, which is not what the enum calls
    /// it. The preferences page listed <c>Enum.ToString()</c> and so offered "Mp4" —
    /// this port's own identifiers leaking into the interface, the same mistake
    /// <see cref="CaptureImageFormatExtensions.DisplayName"/> exists to prevent.
    /// </summary>
    public static string DisplayName(this RecordingFormat format) => format switch
    {
        RecordingFormat.Mp4 => "MP4",
        RecordingFormat.Gif => "GIF",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown recording format."),
    };
}
