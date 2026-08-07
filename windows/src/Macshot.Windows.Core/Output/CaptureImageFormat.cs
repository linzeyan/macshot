namespace Macshot.Windows.Core.Output;

/// <summary>
/// The encodings macshot can write a capture as.
/// </summary>
/// <remarks>
/// <para>
/// macOS offers PNG, JPEG, HEIC, WebP and AVIF; this port offers all but AVIF. Asked
/// what it can write, a stock Windows 11 answers BMP, GIF, JPEG, PNG, TIFF, WMPhoto,
/// DDS, HEIF and JPEG XL — two more than <c>BitmapEncoder</c> has named ids for, which
/// is why the question is worth putting to the machine rather than to the API surface,
/// and neither WebP nor AVIF is on it either way. The Store's "Webp Image Extensions"
/// and "AV1 Video Extension" add <em>decoders</em>; nothing in the box writes either.
/// </para>
/// <para>
/// So WebP is not written by WIC at all. It goes to libwebp — the same library the Mac
/// app encodes with (<c>ImageEncoder.swift:187</c>) — carried beside the app, one
/// native call and about 300 KB per architecture. AVIF has no equivalent at that price:
/// an AV1 encoder is a different order of dependency, and a format that always failed
/// would be worse than one that is plainly absent, so it stays out rather than being
/// added and made to throw.
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
    Webp,
}

public static class CaptureImageFormatExtensions
{
    public static string FileExtension(this CaptureImageFormat format) => format switch
    {
        CaptureImageFormat.Png => ".png",
        CaptureImageFormat.Jpeg => ".jpg",
        CaptureImageFormat.Heic => ".heic",
        CaptureImageFormat.Webp => ".webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown capture image format."),
    };

    /// <summary>
    /// What to call the format where a user reads it, which is not what the enum calls
    /// it: macOS names these PNG, JPEG, HEIC and WebP, and "Png" in a menu is a port that
    /// leaked its own identifiers.
    /// </summary>
    public static string DisplayName(this CaptureImageFormat format) => format switch
    {
        CaptureImageFormat.Png => "PNG",
        CaptureImageFormat.Jpeg => "JPEG",
        CaptureImageFormat.Heic => "HEIC",

        // The format's own spelling, and the Mac app's. Not "WEBP": Google names it WebP
        // and every other tool on the machine writes it that way.
        CaptureImageFormat.Webp => "WebP",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown capture image format."),
    };

    /// <summary>True when <see cref="CaptureSettings.Quality"/> affects the output.</summary>
    /// <remarks>
    /// WebP counts. The format has a lossless mode, but macshot never asks for it — the
    /// Mac app encodes at <c>quality * 100</c> through libwebp's lossy path
    /// (<c>ImageEncoder.swift:202</c>), and a WebP that ignored the quality slider would
    /// be a second lossless format sitting beside PNG rather than the small one someone
    /// picked it to get.
    /// </remarks>
    public static bool IsLossy(this CaptureImageFormat format) =>
        format is CaptureImageFormat.Jpeg or CaptureImageFormat.Heic or CaptureImageFormat.Webp;

    /// <summary>
    /// True when writing this format depends on a codec that may not be there, so it
    /// must be probed for before it is offered.
    /// </summary>
    /// <remarks>
    /// Two different absences. HEIC's codec belongs to Windows and is an optional Store
    /// component. WebP's is libwebp, shipped beside the app — but a native library still
    /// has to load, and one whose own dependencies are missing fails at the first call
    /// rather than at build time. Both are run-time questions, and the answer decides
    /// whether the format is offered at all.
    /// </remarks>
    public static bool RequiresOptionalCodec(this CaptureImageFormat format) =>
        format is CaptureImageFormat.Heic or CaptureImageFormat.Webp;

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
        CaptureImageFormat.Heic or CaptureImageFormat.Webp => CaptureImageFormat.Jpeg,
        _ => format,
    };
}
