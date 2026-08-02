using System.Runtime.InteropServices;

using Macshot.Windows.Core.Output;

using Windows.UI.ViewManagement;

namespace Macshot.Windows.Services;

/// <summary>
/// The picture beside each line of the notification-area menu.
/// </summary>
/// <remarks>
/// <para>
/// macshot puts an SF Symbol on every item of its status-bar menu
/// (<c>AppDelegate.swift:709–806</c>), and a menu of bare words next to it reads as a
/// different program. Windows' own menus carry icons the same way — Explorer's and the
/// shell's do — so this is the platform's convention as much as macshot's.
/// </para>
/// <para>
/// A Win32 menu takes a picture as an <c>HBITMAP</c>, so the glyph has to be rasterized
/// rather than named. Segoe Fluent Icons is the face: it is the same set the preferences
/// window's tab strip already draws from, it ships with Windows, and it is drawn on the
/// same grid at every size — which an icon in a menu needs, because the shell asks for it
/// at whatever the current DPI makes of 16 points.
/// </para>
/// <para>
/// Drawn white on black and turned into alpha afterwards rather than drawn in the final
/// colour. GDI has no alpha: text drawn into a 32-bit surface leaves the alpha byte
/// untouched, and a menu given a bitmap whose alpha is zero draws nothing at all. Taking
/// the coverage from the grey the antialiaser produced, and multiplying the wanted colour
/// through it, is what makes one rasterization serve both a light and a dark menu.
/// </para>
/// </remarks>
internal static class MenuIcons
{
    /// <summary>SM_CXSMICON: the size the shell uses for a small icon at this DPI.</summary>
    private const int SmallIconWidth = 49;

    private const uint TransparentBackground = 1;

    private const uint DibRgbColors = 0;

    private const uint FormatCentre = 0x00000001;

    private const uint FormatVerticallyCentred = 0x00000004;

    private const uint FormatSingleLine = 0x00000020;

    /// <summary>ANTIALIASED_QUALITY: grey coverage rather than ClearType's colour fringes.</summary>
    private const byte AntialiasedQuality = 4;

    private const int DefaultCharSet = 1;

    /// <summary>
    /// The rasterized glyphs, by what was asked for. A tray menu is rebuilt on every
    /// right-click, and rasterizing eighteen glyphs each time would be work done over and
    /// over for a picture that cannot have changed.
    /// </summary>
    private static readonly Dictionary<(string Glyph, bool Dark, int Size), IntPtr> Drawn = [];

    /// <summary>
    /// The bitmap for <paramref name="glyph"/>, or zero when it could not be drawn.
    /// </summary>
    /// <remarks>
    /// Zero rather than an exception: <c>SetMenuItemInfo</c> takes it as "no bitmap" and
    /// the item comes up as it did before there were any. A menu that fails to open
    /// because a font is missing would be macshot with no way in.
    /// </remarks>
    public static IntPtr For(string? glyph, AppTheme theme)
    {
        if (string.IsNullOrEmpty(glyph))
        {
            return IntPtr.Zero;
        }

        var size = GetSystemMetrics(SmallIconWidth);
        if (size <= 0)
        {
            size = 16;
        }

        var dark = IsDark(theme);
        if (Drawn.TryGetValue((glyph, dark, size), out var cached))
        {
            return cached;
        }

        var bitmap = Draw(glyph, dark, size);
        Drawn[(glyph, dark, size)] = bitmap;
        return bitmap;
    }

    /// <summary>Gives back every bitmap this has made.</summary>
    public static void Clear()
    {
        foreach (var bitmap in Drawn.Values.Where(bitmap => bitmap != IntPtr.Zero))
        {
            DeleteObject(bitmap);
        }

        Drawn.Clear();
    }

    /// <summary>
    /// Whether the menu will come up dark, which decides what colour the glyph is drawn.
    /// </summary>
    /// <remarks>
    /// The system's own answer for <see cref="AppTheme.System"/>, taken the way uxtheme
    /// takes it: <see cref="MenuTheme"/> asks for AllowDark there, which follows the app
    /// setting in Windows' personalisation. A white glyph on a light menu is invisible,
    /// so this has to agree with that call rather than guess.
    /// </remarks>
    private static bool IsDark(AppTheme theme)
    {
        if (theme != AppTheme.System)
        {
            return theme == AppTheme.Dark;
        }

        try
        {
            var background = new UISettings().GetColorValue(UIColorType.Background);
            return background.R + background.G + background.B < 3 * 128;
        }
        catch (Exception exception)
        {
            // A process with no view management to ask — rare, and not worth failing a
            // menu over. Light is Windows' own default.
            DiagnosticLog.Write($"Could not read the system theme: {exception.Message}");
            return false;
        }
    }

    private static IntPtr Draw(string glyph, bool dark, int size)
    {
        var screen = GetDC(IntPtr.Zero);
        var canvas = CreateCompatibleDC(screen);
        ReleaseDC(IntPtr.Zero, screen);

        if (canvas == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var font = IntPtr.Zero;
        var bitmap = IntPtr.Zero;

        try
        {
            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = size,

                // Negative: top-down, so the bytes are in the order they are read below.
                Height = -size,
                Planes = 1,
                BitCount = 32,
            };

            bitmap = CreateDIBSection(canvas, ref header, DibRgbColors, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            // Zeroed by CreateDIBSection, which is the black the coverage is measured
            // against as well as the fully transparent the glyph is cut out of.
            var previousBitmap = SelectObject(canvas, bitmap);

            font = CreateFont(
                -size,
                0,
                0,
                0,
                weight: 400,
                italic: 0,
                underline: 0,
                strikeOut: 0,
                DefaultCharSet,
                outputPrecision: 0,
                clipPrecision: 0,
                AntialiasedQuality,
                pitchAndFamily: 0,

                // The Windows 11 set, with the set every earlier Windows has behind it.
                // GDI falls back on its own when the first is not installed, and both
                // carry these codepoints at the same places.
                "Segoe Fluent Icons");

            var previousFont = SelectObject(canvas, font);
            SetBkMode(canvas, TransparentBackground);
            SetTextColor(canvas, 0x00FFFFFF);

            var box = new Rect { Right = size, Bottom = size };
            DrawText(canvas, glyph, glyph.Length, ref box,
                FormatCentre | FormatVerticallyCentred | FormatSingleLine);

            SelectObject(canvas, previousFont);
            SelectObject(canvas, previousBitmap);

            Tint(bits, size, dark);
            var drawn = bitmap;
            bitmap = IntPtr.Zero;
            return drawn;
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"Could not draw the menu icon '{glyph}': {exception.Message}");
            return IntPtr.Zero;
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (font != IntPtr.Zero)
            {
                DeleteObject(font);
            }

            DeleteDC(canvas);
        }
    }

    /// <summary>
    /// Turns the white-on-black rasterization into a premultiplied colour with alpha.
    /// </summary>
    /// <remarks>
    /// The grey GDI left behind is how much of the pixel the glyph covers, so it is the
    /// alpha. The colour is then that alpha's worth of the menu's text colour, which is
    /// what premultiplied means and what the shell's <c>AlphaBlend</c> expects: written
    /// as straight alpha instead, every antialiased edge comes out with a bright halo.
    /// </remarks>
    private static void Tint(IntPtr bits, int size, bool dark)
    {
        // Not pure white and not pure black: Windows' own menu text is a little short of
        // both, and a glyph at full white beside it reads as a different weight.
        var (red, green, blue) = dark ? (255, 255, 255) : (26, 26, 26);

        var pixels = size * size;
        var bytes = new byte[pixels * 4];
        Marshal.Copy(bits, bytes, 0, bytes.Length);

        for (var pixel = 0; pixel < pixels; pixel++)
        {
            var at = pixel * 4;
            var coverage = Math.Max(bytes[at], Math.Max(bytes[at + 1], bytes[at + 2]));

            bytes[at] = (byte)(blue * coverage / 255);
            bytes[at + 1] = (byte)(green * coverage / 255);
            bytes[at + 2] = (byte)(red * coverage / 255);
            bytes[at + 3] = coverage;
        }

        Marshal.Copy(bytes, 0, bits, bytes.Length);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint ImageSize;
        public int HorizontalResolution;
        public int VerticalResolution;
        public uint UsedColours;
        public uint ImportantColours;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr canvas);

    [DllImport("user32.dll", EntryPoint = "DrawTextW", CharSet = CharSet.Unicode)]
    private static extern int DrawText(IntPtr canvas, string text, int length, ref Rect box, uint format);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr canvas);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr canvas);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(
        IntPtr canvas,
        ref BitmapInfoHeader header,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr canvas, IntPtr handle);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr canvas, uint mode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr canvas, uint colour);

    [DllImport("gdi32.dll", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFont(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        uint italic,
        uint underline,
        uint strikeOut,
        int charSet,
        uint outputPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string face);
}
