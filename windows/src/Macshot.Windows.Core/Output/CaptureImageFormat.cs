namespace Macshot.Windows.Core.Output;

/// <summary>
/// The encodings macshot can write a capture as.
/// </summary>
/// <remarks>
/// macOS offers PNG, JPEG, HEIC, and WebP. Windows Imaging Component ships no
/// WebP encoder (only a decoder), and HEIC encoding depends on the optional HEVC
/// extension from the Store, so neither can be offered without bundling a
/// third-party codec. That is a distribution decision rather than an imaging one,
/// so this enum stays at what the platform can always do.
/// </remarks>
public enum CaptureImageFormat
{
    Png,
    Jpeg,
}

public static class CaptureImageFormatExtensions
{
    public static string FileExtension(this CaptureImageFormat format) => format switch
    {
        CaptureImageFormat.Png => ".png",
        CaptureImageFormat.Jpeg => ".jpg",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown capture image format."),
    };

    /// <summary>True when <see cref="CaptureSettings.Quality"/> affects the output.</summary>
    public static bool IsLossy(this CaptureImageFormat format) => format == CaptureImageFormat.Jpeg;
}
