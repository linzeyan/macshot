using System.Runtime.InteropServices;

namespace Macshot.Windows.Services;

/// <summary>
/// The font families this machine has, for the text tool's face picker.
/// </summary>
/// <remarks>
/// <para>
/// WinUI has no way to ask. <c>FontFamily</c> takes a name and resolves it, but nothing
/// enumerates what is installed — that lives in DirectWrite, reachable from Win2D, which
/// this port dropped, or from GDI, which is three declarations and no dependency.
/// </para>
/// <para>
/// The list is read once and kept: enumerating is a few milliseconds, but the picker is
/// opened while the user is mid-capture and a few milliseconds on that thread is a
/// visible stutter under the pointer.
/// </para>
/// </remarks>
internal static class InstalledFonts
{
    /// <summary>
    /// The faces offered above the rule, in macshot's spirit: the ones a label is
    /// actually set in. Windows equivalents of its list rather than a translation of it —
    /// Helvetica is not on a Windows machine and Segoe UI is not on a Mac.
    /// </summary>
    public static IReadOnlyList<string> Popular { get; } =
    [
        "Segoe UI",
        "Arial",
        "Calibri",
        "Cascadia Code",
        "Consolas",
        "Georgia",
        "Impact",
        "Times New Roman",
        "Trebuchet MS",
        "Verdana",
    ];

    private static IReadOnlyList<string>? _families;

    /// <summary>
    /// Every family installed, sorted, with the ones GDI hides from menus left out.
    /// </summary>
    /// <remarks>
    /// A failure gives back <see cref="Popular"/> rather than nothing: the picker still
    /// works, and every name in it is one Windows has shipped for twenty years.
    /// </remarks>
    public static IReadOnlyList<string> Families()
    {
        if (_families is { } cached)
        {
            return cached;
        }

        try
        {
            _families = Enumerate();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            _families = Popular;
        }

        return _families;
    }

    private static IReadOnlyList<string> Enumerate()
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            return Popular;
        }

        try
        {
            // Every charset, and no family named: that is what asks for all of them.
            var request = new LogFont { CharSet = DefaultCharSet, FaceName = string.Empty };
            EnumFontFamiliesEx(screen, ref request, Collect, IntPtr.Zero, 0);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }

        return found.Count == 0 ? Popular : [.. found];

        int Collect(ref EnumLogFont font, IntPtr metric, uint type, IntPtr context)
        {
            var name = font.LogFont.FaceName;

            // A name beginning with @ is the vertical-writing form of a family already
            // in the list, and picking one sets a label sideways.
            if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith('@'))
            {
                found.Add(name);
            }

            return 1;
        }
    }

    private const byte DefaultCharSet = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumFontFamiliesExW")]
    private static extern int EnumFontFamiliesEx(
        IntPtr deviceContext,
        ref LogFont font,
        EnumFontFamiliesCallback callback,
        IntPtr context,
        uint flags);

    private delegate int EnumFontFamiliesCallback(ref EnumLogFont font, IntPtr metric, uint type, IntPtr context);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
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
        public byte CharSet;
        public byte OutPrecision;
        public byte ClipPrecision;
        public byte Quality;
        public byte PitchAndFamily;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct EnumLogFont
    {
        public LogFont LogFont;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string FullName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Style;
    }
}
