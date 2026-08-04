namespace Macshot.Windows.Core.Output;

/// <summary>
/// The encodings macshot can write a capture as.
/// </summary>
/// <remarks>
/// <para>
/// macOS offers PNG, JPEG, HEIC and WebP; Windows matches the first three. WebP is
/// missing for want of an encoder, not for want of an id to name it by: WIC ships a
/// WebP <em>decoder</em> only. Asked what it can write, a stock Windows 11 answers
/// BMP, GIF, JPEG, PNG, TIFF, WMPhoto, DDS, HEIF and JPEG XL — two more than
/// <c>BitmapEncoder</c> has named ids for, which is why the question is worth putting
/// to the machine rather than to the API surface, and no WebP either way. A WebP case
/// here could not be written without bundling a third-party encoder, and a format
/// that always failed would be worse than one that is plainly absent, so it is left
/// out rather than added and made to throw.
/// </para>
/// <para>
/// HEIC is a different shape of problem. The encoder id exists and the codec is
/// registered on a stock Windows 11, but the bytes are HEVC and the extension that
/// writes them is an optional Store component a given machine may not have. That is a
/// run-time fact rather than a compile-time one, which is what
/// <see cref="CaptureImageFormatExtensions.RequiresOptionalCodec"/> and
/// <see cref="CaptureImageFormatExtensions.Fallback"/> are for: the platform half
/// probes once and does not offer what it cannot write, and the encode path still
/// substitutes the fallback — and renames the file after it — for the machine where
/// the probe said yes and the encoder said no.
/// </para>
/// </remarks>
public enum CaptureImageFormat
{
    Png,
    Jpeg,
    Heic,
}

public static class CaptureImageFormatExtensions
{
    public static string FileExtension(this CaptureImageFormat format) => format switch
    {
        CaptureImageFormat.Png => ".png",
        CaptureImageFormat.Jpeg => ".jpg",
        CaptureImageFormat.Heic => ".heic",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown capture image format."),
    };

    /// <summary>
    /// What to call the format where a user reads it, which is not what the enum calls
    /// it: macOS names these PNG, JPEG and HEIC, and "Png" in a menu is a port that
    /// leaked its own identifiers.
    /// </summary>
    public static string DisplayName(this CaptureImageFormat format) => format switch
    {
        CaptureImageFormat.Png => "PNG",
        CaptureImageFormat.Jpeg => "JPEG",
        CaptureImageFormat.Heic => "HEIC",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown capture image format."),
    };

    /// <summary>True when <see cref="CaptureSettings.Quality"/> affects the output.</summary>
    public static bool IsLossy(this CaptureImageFormat format) =>
        format is CaptureImageFormat.Jpeg or CaptureImageFormat.Heic;

    /// <summary>
    /// True when writing this format depends on a codec Windows does not always have,
    /// so it must be probed for before it is offered.
    /// </summary>
    public static bool RequiresOptionalCodec(this CaptureImageFormat format) =>
        format is CaptureImageFormat.Heic;

    /// <summary>
    /// What to write instead when this format's encoder turns out to be missing.
    /// </summary>
    /// <remarks>
    /// Lossy for lossy, lossless for lossless: someone who chose HEIC chose a small
    /// file and accepted the artefacts, so JPEG is the substitute that keeps the
    /// bargain they made. Falling back to PNG would honour the picture and quietly
    /// multiply the file size they were picking a format to avoid. Every format's
    /// fallback needs no optional codec, so this resolves in one step and cannot loop.
    /// </remarks>
    public static CaptureImageFormat Fallback(this CaptureImageFormat format) => format switch
    {
        CaptureImageFormat.Heic => CaptureImageFormat.Jpeg,
        _ => format,
    };
}
