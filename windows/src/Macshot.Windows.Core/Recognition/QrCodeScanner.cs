using ZXing;
using ZXing.Common;

namespace Macshot.Windows.Core.Recognition;

/// <summary>
/// One QR code's payload.
/// </summary>
/// <param name="Value">The decoded text, trimmed.</param>
public sealed record QrCode(string Value)
{
    /// <summary>
    /// The payload as a web address, or null when it is not one.
    /// </summary>
    /// <remarks>
    /// http and https only, which is macshot's rule (<c>VisionOCR.swift:6–14</c>) and
    /// worth keeping for a reason it does not state: a QR code is something a stranger
    /// printed, and an Open button that would hand <c>file:</c> or a custom scheme to
    /// the shell is a way to make a screenshot run something.
    /// </remarks>
    public Uri? Url =>
        Uri.TryCreate(Value, UriKind.Absolute, out var url)
            && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
                ? url
                : null;
}

/// <summary>
/// Finds QR codes in a capture, the counterpart of the <c>VNDetectBarcodesRequest</c>
/// half of macshot's <c>VisionOCR</c>.
/// </summary>
/// <remarks>
/// <para>
/// QR codes only, because that is the whole of what macshot detects:
/// <c>request.symbologies = [.qr, .microQR]</c> and the results are filtered to those
/// two again on the way out (<c>VisionOCR.swift:66–83</c>). Vision can read a dozen
/// other symbologies and macshot asks for none of them, so a port that read bar codes
/// off a parcel label would be inventing a feature rather than matching one.
/// </para>
/// <para>
/// Windows has no decoder in the box — <c>Windows.Media.Ocr</c> reads text and nothing
/// else — so this is the port's one third-party dependency beyond the App SDK. It lives
/// in Core rather than beside the OCR call because decoding is arithmetic on a byte
/// array: no Windows type appears in it, and it can therefore be tested on the machine
/// the code is written on.
/// </para>
/// <para>
/// ⚠️ Micro QR is not covered. macshot asks Vision for it; ZXing.Net 0.16.11 has no
/// such format. Micro QR is rare enough to be worth recording rather than working
/// around.
/// </para>
/// </remarks>
public static class QrCodeScanner
{
    private static readonly BarcodeReaderGeneric Reader = new()
    {
        Options = new DecodingOptions
        {
            PossibleFormats = [BarcodeFormat.QR_CODE],

            // A QR code in a screenshot is not the clean, centred, well-lit picture the
            // fast path assumes — it is a fragment of a web page, possibly scaled and
            // possibly at an angle. The extra passes are worth it here, where the input
            // is one small image and the user has already waited for OCR.
            TryHarder = true,
            TryInverted = true,
        },

        // The scan runs on whatever thread the caller is on and returns a value; no
        // bitmap type is constructed, so nothing here needs a platform.
        AutoRotate = true,
    };

    /// <summary>
    /// Reads every QR code in a BGRA image.
    /// </summary>
    /// <param name="bgra">Pixels, four bytes each, in BGRA order.</param>
    /// <param name="width">Pixel width.</param>
    /// <param name="height">Pixel height.</param>
    /// <remarks>
    /// <para>
    /// Never throws. This runs alongside text recognition on a capture the user already
    /// has; a decoder that threw on a strange image would lose the recognized text with
    /// it, which is the one outcome worth avoiding.
    /// </para>
    /// <para>
    /// Payloads are trimmed, empty ones dropped, and duplicates removed while keeping
    /// the order they were found in — macshot's <c>seen.insert(value).inserted</c>. A
    /// page showing the same code twice should offer one row, not two identical ones.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<QrCode> Scan(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < (long)width * height * 4)
        {
            return [];
        }

        Result[]? results;
        try
        {
            var source = new RGBLuminanceSource(
                bgra.ToArray(),
                width,
                height,
                RGBLuminanceSource.BitmapFormat.BGRA32);

            results = Reader.DecodeMultiple(source);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return [];
        }

        if (results is null)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var found = new List<QrCode>();
        foreach (var result in results)
        {
            var value = result?.Text?.Trim();
            if (!string.IsNullOrEmpty(value) && seen.Add(value))
            {
                found.Add(new QrCode(value));
            }
        }

        return found;
    }
}
