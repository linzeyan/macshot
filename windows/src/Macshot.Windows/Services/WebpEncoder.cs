using System.Runtime.InteropServices;

using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Services;

/// <summary>
/// WebP, written by libwebp rather than by WIC.
/// </summary>
/// <remarks>
/// <para>
/// The one save format Windows cannot write for itself: WIC enumerates no WebP encoder,
/// and the Store extension that makes .webp files open adds a decoder only. macOS is in
/// the same position — <c>ImageEncoder.encodeWebP</c> reaches for libwebp too — so the
/// port matching the spec here means carrying the same library, not finding a platform
/// answer that does not exist.
/// </para>
/// <para>
/// libwebp's simple API, not its configurable one. <c>WebPEncodeBGRA</c> is a single call
/// against the buffer already in hand; the alternative is <c>WebPConfig</c> and
/// <c>WebPPicture</c>, two structs whose layout this would have to restate by hand and
/// keep true across versions, to gain the encoder presets. The Mac app asks for the
/// <c>.picture</c> preset, which differs from the default in its noise shaping and filter
/// strength — a difference in the artefacts at a given quality, not in the quality itself.
/// That is the one place this deliberately does not match, and it is not worth two
/// hand-written struct layouts.
/// </para>
/// <para>
/// Nothing here is behind <c>OFFLINE</c>: an image encoder touches no network, and the
/// offline build saves WebP exactly as the normal one does.
/// </para>
/// </remarks>
internal static class WebpEncoder
{
    /// <summary>
    /// The name NuGet lands beside the app, per architecture. Extensionless, so the
    /// runtime resolves it the way it resolves any native library.
    /// </summary>
    private const string Library = "libwebp";

    private static readonly Lazy<bool> Loaded = new(Probe);

    /// <summary>
    /// Whether libwebp answered, decided once. False takes WebP out of the format list
    /// entirely rather than leaving an option that fails at every save.
    /// </summary>
    public static bool IsAvailable => Loaded.Value;

    /// <summary>
    /// The capture as WebP bytes at the given quality.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// When libwebp declines the picture. The one predictable cause is size: WebP cannot
    /// hold a side longer than 16383 pixels, which a stitched scroll capture reaches. The
    /// caller substitutes the fallback format and logs it, so the capture still lands.
    /// </exception>
    public static byte[] Encode(CapturedFrame frame, int quality)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var factor = Math.Clamp(quality, CaptureSettings.MinQuality, CaptureSettings.MaxQuality);

        // Alpha only where it means something. Every ordinary capture is BGRX with an
        // undefined fourth byte, and handing that to WebPEncodeBGRA would punch holes in
        // screenshots at random; dropping it also spares the file an alpha plane it has
        // no use for. The cut-out Remove Background produces is the exception, and its
        // alpha is straight rather than premultiplied, which is what libwebp expects.
        IntPtr encoded;
        var size = frame.HasAlpha
            ? WebPEncodeBGRA(frame.BgraPixels, frame.Width, frame.Height, frame.Width * 4, factor, out encoded)
            : WebPEncodeBGR(WithoutAlpha(frame), frame.Width, frame.Height, frame.Width * 3, factor, out encoded);

        if (encoded == IntPtr.Zero || size == UIntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"libwebp could not encode a {frame.Width}×{frame.Height} capture.");
        }

        try
        {
            var bytes = new byte[(int)size];
            Marshal.Copy(encoded, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            // The buffer came from libwebp's allocator, so it goes back to libwebp's
            // deallocator. Marshal.FreeHGlobal would be the wrong heap.
            WebPFree(encoded);
        }
    }

    /// <summary>The same pixels packed three bytes each, which is what BGR wants.</summary>
    private static byte[] WithoutAlpha(CapturedFrame frame)
    {
        var source = frame.BgraPixels;
        var packed = new byte[frame.Width * frame.Height * 3];

        for (int read = 0, write = 0; write < packed.Length; read += 4, write += 3)
        {
            packed[write] = source[read];
            packed[write + 1] = source[read + 1];
            packed[write + 2] = source[read + 2];
        }

        return packed;
    }

    private static bool Probe()
    {
        try
        {
            // Cheapest export there is, and it answers the only question worth asking:
            // did the library load. A missing file arrives as DllNotFoundException, a
            // library built for the other architecture as BadImageFormatException, and a
            // library whose own dependencies are absent as either.
            var version = WebPGetEncoderVersion();
            DiagnosticLog.Write(
                $"libwebp {version >> 16}.{(version >> 8) & 0xff}.{version & 0xff} is loaded; WebP is available.");
            return true;
        }
        catch (Exception exception)
        {
            // Written down because "why is WebP not in the format list on this machine"
            // is otherwise a question that needs a debugger on the user's machine.
            DiagnosticLog.Write($"libwebp did not load, so WebP is not offered: {exception.Message}");
            return false;
        }
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int WebPGetEncoderVersion();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern UIntPtr WebPEncodeBGR(
        byte[] bgr, int width, int height, int stride, float qualityFactor, out IntPtr output);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern UIntPtr WebPEncodeBGRA(
        byte[] bgra, int width, int height, int stride, float qualityFactor, out IntPtr output);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void WebPFree(IntPtr pointer);
}
