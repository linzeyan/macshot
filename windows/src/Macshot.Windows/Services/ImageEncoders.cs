using Macshot.Windows.Core.Output;
using Windows.Graphics.Imaging;

namespace Macshot.Windows.Services;

/// <summary>
/// Which of macshot's save formats this particular machine can write, and the encoder
/// behind each one.
/// </summary>
/// <remarks>
/// <para>
/// macOS's <c>ImageEncoder.availableFormats</c>, which filters the list the same way
/// and for the same reason: a format offered on a machine that cannot write it is a
/// setting whose only effect is a file in some other format.
/// </para>
/// <para>
/// The probe asks WIC what encoders are registered rather than attempting a real
/// encode. A trial encode would be the stronger answer — HEIF is registered on a
/// stock Windows 11 whether or not the HEVC extension behind it is installed — but it
/// is asynchronous, and blocking the UI thread on a WinRT operation to answer a
/// question about a combo box is a deadlock waiting for the wrong machine. The
/// weaker answer plus the fallback in <see cref="ImageDelivery.EncodeAsync"/> covers
/// the same ground: the option disappears where the codec was never registered, and
/// where it is registered but broken the encode substitutes and says so in the log.
/// </para>
/// </remarks>
internal static class ImageEncoders
{
    private static readonly Lazy<HashSet<CaptureImageFormat>> SupportedFormats = new(Probe);

    /// <summary>The formats to offer, in the order the enum declares them.</summary>
    public static IEnumerable<CaptureImageFormat> Available =>
        Enum.GetValues<CaptureImageFormat>().Where(Supports);

    public static bool Supports(CaptureImageFormat format) => SupportedFormats.Value.Contains(format);

    /// <summary>
    /// The format as stored, or its substitute on a machine that cannot write it — so a
    /// settings file carried from a machine with the HEVC extension to one without does
    /// not leave the preferences showing a format nothing else in the app will produce.
    /// </summary>
    public static CaptureImageFormat Resolve(CaptureImageFormat format) =>
        Supports(format) ? format : format.Fallback();

    /// <summary>
    /// The WIC encoder behind a format. Not every format has one — WebP is written by
    /// <see cref="WebpEncoder"/> and AVIF by <see cref="AvifEncoder"/>, and neither
    /// reaches here.
    /// </summary>
    public static Guid EncoderIdOf(CaptureImageFormat format) => format switch
    {
        CaptureImageFormat.Png => BitmapEncoder.PngEncoderId,
        CaptureImageFormat.Jpeg => BitmapEncoder.JpegEncoderId,
        CaptureImageFormat.Heic => BitmapEncoder.HeifEncoderId,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown capture image format."),
    };

    /// <param name="registered">
    /// What WIC answered, or null where the question could not be put to it.
    /// </param>
    private static bool CanWrite(
        CaptureImageFormat format,
        IReadOnlyList<BitmapCodecInformation>? registered) => format switch
    {
        // Not WIC's to answer for. Both encoders are carried beside the app — libwebp and
        // macshot_avif — so the question is whether that library loaded rather than what
        // Windows registered, and it stays answerable even when the enumeration above
        // failed.
        CaptureImageFormat.Webp => WebpEncoder.IsAvailable,
        CaptureImageFormat.Avif => AvifEncoder.IsAvailable,
        _ => !format.RequiresOptionalCodec()
            || (registered is not null && registered.Any(codec => codec.CodecId == EncoderIdOf(format))),
    };

    private static HashSet<CaptureImageFormat> Probe()
    {
        IReadOnlyList<BitmapCodecInformation>? registered;
        try
        {
            registered = BitmapEncoder.GetEncoderInformationEnumerator();
        }
        catch (Exception exception)
        {
            // An empty answer would take PNG away with it. The formats that need no
            // optional codec are part of WIC itself and cannot be absent on a machine
            // running this app at all.
            DiagnosticLog.Write($"Could not enumerate the image encoders: {exception.Message}");
            return [.. Enum.GetValues<CaptureImageFormat>().Where(format => CanWrite(format, null))];
        }

        // The one place the real encoder list is visible. It is written once per run and
        // only to the log, because the answer to "why is HEIC not offered here" is
        // otherwise a question that needs a debugger on the user's machine.
        DiagnosticLog.Write(
            "Image encoders registered: "
            + string.Join(", ", registered.Select(codec => $"{codec.FriendlyName} {codec.CodecId:B}")));

        return [.. Enum.GetValues<CaptureImageFormat>().Where(format => CanWrite(format, registered))];
    }
}
