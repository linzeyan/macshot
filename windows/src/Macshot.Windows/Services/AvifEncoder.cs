using System.Runtime.InteropServices;

using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Services;

/// <summary>
/// AVIF, written by rav1e rather than by WIC.
/// </summary>
/// <remarks>
/// <para>
/// The second save format Windows cannot write for itself. WIC enumerates no AVIF
/// encoder, and the Store's "AV1 Video Extension" — the thing that makes .avif files open
/// — adds a decoder only. macOS gets this one free from ImageIO
/// (<c>ImageEncoder.swift:179</c>), so matching the spec here means carrying an AV1
/// encoder rather than finding a platform answer that does not exist.
/// </para>
/// <para>
/// That encoder is <c>windows/native/macshot-avif</c>, a Rust <c>cdylib</c> over the
/// <c>ravif</c> crate. Rust because the alternatives in C are libaom and SVT-AV1, each of
/// which is a CMake build and a cross-compile story for win-arm64; <c>cargo build
/// --target aarch64-pc-windows-msvc</c> is the whole of the equivalent here. The library
/// is built without rav1e's assembly so that neither nasm nor a C compiler is needed on
/// whatever machine is doing the building.
/// </para>
/// <para>
/// Nothing here is behind <c>OFFLINE</c>: an image encoder touches no network, and the
/// offline build saves AVIF exactly as the normal one does.
/// </para>
/// </remarks>
internal static class AvifEncoder
{
    /// <summary>
    /// The name the build lands beside the app, per architecture. Extensionless, so the
    /// runtime resolves it the way it resolves any native library.
    /// </summary>
    private const string Library = "macshot_avif";

    /// <summary>
    /// The contract this file was written against. The library is built from this tree
    /// and shipped in the same directory, so a mismatch means a stale copy rather than a
    /// version to negotiate with — and is treated exactly like a missing one.
    /// </summary>
    private const uint ExpectedAbiVersion = 1;

    private const int StatusOk = 0;

    private static readonly Lazy<bool> Loaded = new(Probe);

    /// <summary>
    /// Whether the encoder answered, decided once. False takes AVIF out of the format
    /// list entirely rather than leaving an option that fails at every save.
    /// </summary>
    public static bool IsAvailable => Loaded.Value;

    /// <summary>
    /// The capture as AVIF bytes at the given quality.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// When rav1e declines the picture. The caller substitutes the fallback format and
    /// logs it, so the capture still lands.
    /// </exception>
    public static byte[] Encode(CapturedFrame frame, int quality)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var factor = Math.Clamp(quality, CaptureSettings.MinQuality, CaptureSettings.MaxQuality);

        // Alpha only where it means something, the same judgement WebpEncoder makes and
        // for the same reason: an ordinary capture is BGRX with an undefined fourth byte,
        // and encoding that as alpha punches holes in screenshots at random. The library
        // does the BGRA-to-RGBA reversal itself, which saves copying the frame twice.
        var buffer = default(AvifBuffer);
        var status = MacshotAvifEncodeBgra(
            frame.BgraPixels,
            frame.Width,
            frame.Height,
            frame.Width * 4,
            frame.HasAlpha,
            factor,
            ref buffer);

        if (status != StatusOk || buffer.Data == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"The AVIF encoder refused a {frame.Width}×{frame.Height} capture ({Describe(status)}).");
        }

        try
        {
            var bytes = new byte[(int)buffer.Length];
            Marshal.Copy(buffer.Data, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            // The allocation came from Rust's allocator and has to go back to it, which
            // is why the capacity travels with the pointer. Marshal.FreeHGlobal would be
            // the wrong heap, and freeing without the capacity would be undefined rather
            // than merely leaky.
            MacshotAvifFree(buffer);
        }
    }

    /// <summary>What a status code from the library means, for the log and the message.</summary>
    private static string Describe(int status) => status switch
    {
        -1 => "a null argument",
        -2 => "an empty image",
        -3 => "a pixel buffer too small for the stated size",
        -4 => "the encoder rejected the image",
        -5 => "the encoder panicked",
        _ => $"status {status}",
    };

    private static bool Probe()
    {
        try
        {
            var version = MacshotAvifAbiVersion();
            if (version != ExpectedAbiVersion)
            {
                // A DLL from another build of this tree. Loud, because the format simply
                // vanishing from preferences is otherwise indistinguishable from the
                // library being absent, and the fix is entirely different.
                DiagnosticLog.Write(
                    $"macshot_avif speaks ABI {version} but this build expects {ExpectedAbiVersion}; "
                    + "AVIF is not offered.");
                return false;
            }

            DiagnosticLog.Write($"macshot_avif ABI {version} is loaded; AVIF is available.");
            return true;
        }
        catch (Exception exception)
        {
            // Written down because "why is AVIF not in the format list on this machine" is
            // otherwise a question that needs a debugger on the user's machine. A missing
            // file arrives as DllNotFoundException and one built for the other
            // architecture as BadImageFormatException.
            DiagnosticLog.Write($"macshot_avif did not load, so AVIF is not offered: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// The encoded bytes, still owned by Rust's allocator until <c>macshot_avif_free</c>.
    /// </summary>
    /// <remarks>
    /// Sequential and blittable so it crosses as-is. <c>UIntPtr</c> rather than
    /// <c>ulong</c> because the Rust side declares <c>usize</c>, which is 8 bytes on x64
    /// and on arm64 but is not the same type as a 64-bit integer on either.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct AvifBuffer
    {
        public IntPtr Data;
        public UIntPtr Length;
        public UIntPtr Capacity;
    }

    [DllImport(Library, EntryPoint = "macshot_avif_abi_version", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint MacshotAvifAbiVersion();

    /// <param name="hasAlpha">
    /// Marshalled as one byte. The default for <c>bool</c> is the four-byte Win32 BOOL,
    /// which Rust's one-byte <c>bool</c> is not, and the mismatch would read whatever
    /// followed it on the stack rather than fail.
    /// </param>
    [DllImport(Library, EntryPoint = "macshot_avif_encode_bgra", CallingConvention = CallingConvention.Cdecl)]
    private static extern int MacshotAvifEncodeBgra(
        byte[] bgra,
        int width,
        int height,
        int stride,
        [MarshalAs(UnmanagedType.U1)] bool hasAlpha,
        int quality,
        ref AvifBuffer output);

    [DllImport(Library, EntryPoint = "macshot_avif_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void MacshotAvifFree(AvifBuffer buffer);
}
