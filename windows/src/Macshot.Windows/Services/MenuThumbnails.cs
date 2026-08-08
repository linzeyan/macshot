using System.Runtime.InteropServices;

namespace Macshot.Windows.Services;

/// <summary>
/// The picture of a past capture, at the size a menu line can carry.
/// </summary>
/// <remarks>
/// <para>
/// macshot puts one on every item of its Recent Captures submenu
/// (<c>AppDelegate.swift:3183</c>), and it is what makes the submenu answerable at a
/// glance: two captures taken the same afternoon are told apart by looking at them, not
/// by reading their dimensions.
/// </para>
/// <para>
/// GDI+ rather than the imaging stack the rest of the app decodes with. A tray menu is
/// built inside <c>WM_RBUTTONUP</c>, where nothing can be awaited — and
/// <c>Windows.Graphics.Imaging</c> is asynchronous all the way down. GDI+ ships with
/// Windows, decodes and scales in one synchronous call, and hands back the
/// <c>HBITMAP</c> a menu item wants anyway.
/// </para>
/// <para>
/// Nothing is cached. <see cref="MenuIcons"/> keeps its glyphs forever because eighteen
/// of them are the same eighteen every time; these are a different five after every
/// capture, and a cache of them would be a cache of whatever the history used to hold.
/// The caller gives each one back with <see cref="Release"/> once the menu has closed.
/// </para>
/// </remarks>
internal static class MenuThumbnails
{
    /// <summary>
    /// macshot's longest edge for one of these, in points
    /// (<c>ScreenshotHistory.swift:148</c>). Scaled by the DPI here because macOS's 36 is
    /// already in points and Windows' menus are laid out in pixels.
    /// </summary>
    private const int Points = 36;

    /// <summary>What a transparent pixel is composited onto: white, as a menu is.</summary>
    private const uint Background = 0xFFFFFFFF;

    private const int Ok = 0;

    private static readonly Lazy<bool> Started = new(Start);

    /// <summary>
    /// The capture at <paramref name="path"/> as a menu bitmap, or zero when it could not
    /// be read.
    /// </summary>
    /// <remarks>
    /// Zero rather than an exception, for the reason <see cref="MenuIcons.For"/> returns
    /// it: <c>SetMenuItemInfo</c> reads it as "no bitmap" and the line comes up as text.
    /// A menu that refuses to open because one archived capture went missing would be
    /// macshot with no way in.
    /// </remarks>
    public static IntPtr TryLoad(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!Started.Value)
        {
            return IntPtr.Zero;
        }

        var image = IntPtr.Zero;
        var thumbnail = IntPtr.Zero;

        try
        {
            if (GdipLoadImageFromFile(path, out image) != Ok || image == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (GdipGetImageWidth(image, out var width) != Ok
                || GdipGetImageHeight(image, out var height) != Ok
                || width == 0
                || height == 0)
            {
                return IntPtr.Zero;
            }

            // The longest edge, so a tall capture and a wide one take the same amount of
            // the menu. macshot scales by the same rule.
            var extent = Extent();
            var scale = Math.Min(extent / (double)width, extent / (double)height);
            var thumbWidth = Math.Max(1, (int)Math.Round(width * scale));
            var thumbHeight = Math.Max(1, (int)Math.Round(height * scale));

            if (GdipGetImageThumbnail(image, thumbWidth, thumbHeight, out thumbnail, IntPtr.Zero, IntPtr.Zero) != Ok
                || thumbnail == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return GdipCreateHBITMAPFromBitmap(thumbnail, out var bitmap, Background) == Ok
                ? bitmap
                : IntPtr.Zero;
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"Could not make a menu thumbnail of '{path}': {exception.Message}");
            return IntPtr.Zero;
        }
        finally
        {
            if (thumbnail != IntPtr.Zero)
            {
                GdipDisposeImage(thumbnail);
            }

            if (image != IntPtr.Zero)
            {
                GdipDisposeImage(image);
            }
        }
    }

    /// <summary>Gives back a bitmap this made. Zero is ignored, as the caller's may be.</summary>
    public static void Release(IntPtr bitmap)
    {
        if (bitmap != IntPtr.Zero)
        {
            DeleteObject(bitmap);
        }
    }

    private static int Extent()
    {
        try
        {
            var dpi = GetDpiForSystem();
            return dpi > 0 ? (int)Math.Round(Points * dpi / 96.0) : Points;
        }
        catch (EntryPointNotFoundException)
        {
            return Points;
        }
    }

    /// <remarks>
    /// Never shut down. GDI+ is torn down by the process ending, and calling
    /// <c>GdiplusShutdown</c> from a finalizer or an exit handler while a menu still holds
    /// a bitmap it made is how that call is documented to crash.
    /// </remarks>
    private static bool Start()
    {
        var input = new GdiplusStartupInput { Version = 1 };
        var started = GdiplusStartup(out _, ref input, IntPtr.Zero) == Ok;
        if (!started)
        {
            DiagnosticLog.Write("GDI+ would not start, so the menu shows no capture thumbnails.");
        }

        return started;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GdiplusStartupInput
    {
        public uint Version;
        public IntPtr DebugEventCallback;
        [MarshalAs(UnmanagedType.Bool)]
        public bool SuppressBackgroundThread;
        [MarshalAs(UnmanagedType.Bool)]
        public bool SuppressExternalCodecs;
    }

    [DllImport("gdiplus.dll")]
    private static extern int GdiplusStartup(out IntPtr token, ref GdiplusStartupInput input, IntPtr output);

    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    private static extern int GdipLoadImageFromFile(string filename, out IntPtr image);

    [DllImport("gdiplus.dll")]
    private static extern int GdipGetImageWidth(IntPtr image, out uint width);

    [DllImport("gdiplus.dll")]
    private static extern int GdipGetImageHeight(IntPtr image, out uint height);

    [DllImport("gdiplus.dll")]
    private static extern int GdipGetImageThumbnail(
        IntPtr image,
        int width,
        int height,
        out IntPtr thumbnail,
        IntPtr callback,
        IntPtr callbackData);

    [DllImport("gdiplus.dll")]
    private static extern int GdipCreateHBITMAPFromBitmap(IntPtr bitmap, out IntPtr result, uint background);

    [DllImport("gdiplus.dll")]
    private static extern int GdipDisposeImage(IntPtr image);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);
}
