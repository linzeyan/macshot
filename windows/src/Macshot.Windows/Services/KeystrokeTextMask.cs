using System.Runtime.InteropServices;

using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Services;

/// <summary>
/// Turns "Ctrl + Shift + P" into the coverage the keystroke pill paints its glyphs with.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="KeystrokePill"/> composes the pill and the text, but it cannot draw letters:
/// that needs a font engine and a font, neither of which belongs in a project that has to
/// build and run on a machine with no Windows on it. This is the seam — one line of text
/// in, one byte of coverage per pixel out.
/// </para>
/// <para>
/// Antialiased rather than ClearType on purpose. ClearType colours the edge of every stroke
/// for a particular subpixel order; read back as a single channel it comes out lopsided,
/// and the pill it lands on is not a monitor pixel grid but a recording that may be
/// scaled, rotated on a phone, or re-encoded twice.
/// </para>
/// </remarks>
internal static class KeystrokeTextMask
{
    /// <summary>The Windows UI face, as macshot uses the macOS one.</summary>
    private const string FaceName = "Segoe UI";

    /// <summary>macshot's <c>.medium</c> weight.</summary>
    private const int Medium = 600;

    private const int Antialiased = 4;
    private const int TransparentBackground = 1;
    private const uint White = 0x00FFFFFF;
    private const uint Black = 0x00000000;

    /// <summary>
    /// Draws one line and answers its coverage, or null when GDI would not give up the
    /// objects to draw it with — in which case the pill simply does not appear.
    /// </summary>
    public static (byte[] Mask, int Width, int Height)? Render(string text, double scale)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
        {
            return null;
        }

        var font = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var brush = IntPtr.Zero;
        var previousFont = IntPtr.Zero;
        var previousBitmap = IntPtr.Zero;

        try
        {
            var description = new LogFont
            {
                // Negative for an em height rather than a cell height, which is what a
                // point size means. Already in pixels: the pill's numbers are DIPs, and
                // the scale is this display's.
                Height = -(int)Math.Round(KeystrokePill.FontSize * scale),
                Weight = Medium,
                Quality = Antialiased,
                FaceName = FaceName,
            };

            font = CreateFontIndirectW(ref description);
            if (font == IntPtr.Zero)
            {
                return null;
            }

            previousFont = SelectObject(dc, font);

            if (!GetTextExtentPoint32W(dc, text, text.Length, out var extent)
                || extent.Width <= 0
                || extent.Height <= 0)
            {
                return null;
            }

            var header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = extent.Width,

                // Negative for top-down rows, so row 0 of the mask is the top row of the
                // text rather than its baseline seen from below.
                Height = -extent.Height,
                Planes = 1,
                BitCount = 32,
            };

            bitmap = CreateDIBSection(dc, ref header, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return null;
            }

            previousBitmap = SelectObject(dc, bitmap);

            // White on black, so what comes back is coverage: black where the paper shows
            // through, white in the middle of a stroke, and the antialiased edge in between.
            var whole = new Rect { Right = extent.Width, Bottom = extent.Height };
            brush = CreateSolidBrush(Black);
            FillRect(dc, ref whole, brush);

            SetBkMode(dc, TransparentBackground);
            SetTextColor(dc, White);
            TextOutW(dc, 0, 0, text, text.Length);

            var pixels = new byte[checked(extent.Width * extent.Height * 4)];
            Marshal.Copy(bits, pixels, 0, pixels.Length);

            var mask = new byte[extent.Width * extent.Height];
            for (var i = 0; i < mask.Length; i++)
            {
                // Any channel would do — the three are equal without ClearType — and the
                // first is the one that is there whatever the byte order turns out to be.
                mask[i] = pixels[i * 4];
            }

            return (mask, extent.Width, extent.Height);
        }
        finally
        {
            if (previousBitmap != IntPtr.Zero)
            {
                SelectObject(dc, previousBitmap);
            }

            if (previousFont != IntPtr.Zero)
            {
                SelectObject(dc, previousFont);
            }

            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (brush != IntPtr.Zero)
            {
                DeleteObject(brush);
            }

            if (font != IntPtr.Zero)
            {
                DeleteObject(font);
            }

            DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int ImageSize;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public int ColorsUsed;
        public int ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct LogFont
    {
        public int Height;
        public int Width;
        public int Escapement;
        public int Orientation;
        public int Weight;
        public byte Italic;
        public byte Underline;
        public byte StrikeOut;
        public byte CharacterSet;
        public byte OutPrecision;
        public byte ClipPrecision;
        public byte Quality;
        public byte PitchAndFamily;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr handle);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr CreateFontIndirectW(ref LogFont font);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(
        IntPtr dc,
        ref BitmapInfoHeader header,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr dc, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr dc, uint color);

    [DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTextExtentPoint32W(
        IntPtr dc,
        string text,
        int length,
        out NativeSize size);

    [DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TextOutW(IntPtr dc, int x, int y, string text, int length);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr dc, ref Rect rect, IntPtr brush);
}
