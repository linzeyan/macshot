using System.Runtime.InteropServices;

using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Services;

/// <summary>
/// Makes a picture of some copied text, so that text can be pinned.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>ClipboardTextPinRenderer</c>, for plain text. A pin window holds pixels
/// and nothing else, so "Pin from Clipboard" with text on the clipboard has to draw the
/// text first — which is also why the result is a picture the user can annotate and save
/// like any other capture rather than a text window.
/// </para>
/// <para>
/// Drawn with GDI rather than with XAML. Rendering a XAML element to a bitmap needs the
/// element to be in a live visual tree, so it would mean putting a window on screen to
/// take a picture of it and then hiding it again — visible, and slower than the drawing
/// it is doing. GDI draws into a buffer nobody has to see.
/// </para>
/// <para>
/// macshot also pins HTML and RTF, keeping their fonts and colours. That is a rich-text
/// layout engine's worth of work; this renders the plain text such a paste always also
/// carries, which is what a paste from a terminal, an editor or a chat window is.
/// </para>
/// </remarks>
internal static class ClipboardTextImage
{
    /// <summary>Fixed-width, because a paste is as often code as prose.</summary>
    private const string FaceName = "Consolas";

    /// <summary>
    /// Points to pixels at the 96-DPI baseline every other number in this port is in, so
    /// an 18-point line here is the same size as an 18-point line on the Mac.
    /// </summary>
    private const int PointsPerInch = 72;
    private const int BaselineDpi = 96;

    /// <summary>
    /// How much of a paste is measured. The picture is never taller than 82% of the
    /// display, which at this line height is a few dozen lines; everything past that is
    /// laid out only to be cropped away. A paste of a whole file would otherwise be
    /// word-wrapped in full before any of it could be shown.
    /// </summary>
    private const int MostCharacters = 20_000;

    private const int TransparentBackground = 1;
    private const uint Black = 0x00000000;
    private const uint White = 0x00FFFFFF;
    private const uint DrawTextFormat = WordBreak | NoPrefix | ExpandTabs;
    private const uint WordBreak = 0x0010;
    private const uint ExpandTabs = 0x0040;
    private const uint CalculateOnly = 0x0400;
    private const uint NoPrefix = 0x0800;

    /// <summary>
    /// Draws the text on white and answers it as a capture, or null when there is
    /// nothing to draw or GDI would not give up the objects to draw it with.
    /// </summary>
    public static CapturedFrame? Render(string text, double screenWidth, double screenHeight)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var laid = text.Length > MostCharacters ? text[..MostCharacters] : text;
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
                // Negative asks for a cell of this many pixels for the characters
                // themselves rather than for the line box, which is what a point size means.
                Height = -((int)TextPinLayout.FontSize * BaselineDpi / PointsPerInch),
                Weight = 400,
                CharacterSet = 1,
                Quality = 5,
                FaceName = FaceName,
            };

            font = CreateFontIndirectW(ref description);
            if (font == IntPtr.Zero)
            {
                return null;
            }

            previousFont = SelectObject(dc, font);

            var measured = new Rect
            {
                Right = (int)Math.Ceiling(TextPinLayout.MaxContentWidth(screenWidth)),
            };

            if (DrawTextW(dc, laid, laid.Length, ref measured, DrawTextFormat | CalculateOnly) == 0)
            {
                return null;
            }

            var (width, height) = TextPinLayout.Fit(
                measured.Right - measured.Left,
                measured.Bottom - measured.Top,
                screenWidth,
                screenHeight);

            var header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,

                // Negative for top-down rows, which is the order every other buffer in
                // this port is in. A bottom-up DIB would come back mirrored.
                Height = -height,
                Planes = 1,
                BitCount = 32,
            };

            bitmap = CreateDIBSection(dc, ref header, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return null;
            }

            previousBitmap = SelectObject(dc, bitmap);

            var whole = new Rect { Right = width, Bottom = height };
            brush = CreateSolidBrush(White);
            FillRect(dc, ref whole, brush);

            SetBkMode(dc, TransparentBackground);
            SetTextColor(dc, Black);

            var inset = new Rect
            {
                Left = (int)TextPinLayout.PaddingHorizontal,
                Top = (int)TextPinLayout.PaddingVertical,
                Right = width - (int)TextPinLayout.PaddingHorizontal,
                Bottom = height - (int)TextPinLayout.PaddingVertical,
            };

            DrawTextW(dc, laid, laid.Length, ref inset, DrawTextFormat);

            var pixels = new byte[checked(width * height * 4)];
            Marshal.Copy(bits, pixels, 0, pixels.Length);

            // GDI leaves the fourth byte at zero, which every consumer here reads as
            // fully transparent: without this the picture is drawn and then invisible.
            for (var offset = 3; offset < pixels.Length; offset += 4)
            {
                pixels[offset] = byte.MaxValue;
            }

            // No place on the virtual desktop: this came from the clipboard, so the pin
            // window is free to open it wherever a new pin belongs.
            return new CapturedFrame(0, 0, width, height, pixels);
        }
        finally
        {
            // Selected objects are given back before they are deleted; a handle still
            // selected into a DC is one GDI keeps, which is how a process leaks its way
            // to the 10,000-object limit.
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
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
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

        /// <summary>DEFAULT_CHARSET, so the face is asked for whatever the text needs.</summary>
        public byte CharacterSet;

        public byte OutPrecision;
        public byte ClipPrecision;

        /// <summary>CLEARTYPE_QUALITY. Text this size is read, not glanced at.</summary>
        public byte Quality;

        public byte PitchAndFamily;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr handle);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr CreateFontIndirectW(ref LogFont font);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BitmapInfoHeader header,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr hdc, uint color);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hdc, ref Rect rect, IntPtr brush);

    [DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int DrawTextW(IntPtr hdc, string text, int length, ref Rect rect, uint format);
}
