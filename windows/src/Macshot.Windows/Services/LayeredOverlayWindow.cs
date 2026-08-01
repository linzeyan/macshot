using System.Runtime.InteropServices;

namespace Macshot.Windows.Services;

/// <summary>
/// A borderless, click-through, always-on-top window whose every pixel carries its own
/// alpha, fed from a premultiplied BGRA buffer.
/// </summary>
/// <remarks>
/// <para>
/// Not a WinUI <c>Window</c>, and it cannot be one. WinUI renders through a swap chain
/// with no per-pixel transparency: a WinUI window showing a yellow disc at 35% would be
/// an opaque rectangle with a disc on it. The two escapes the rest of the port uses —
/// <c>SetWindowRgn</c> for the countdown dial and the recorded-region frame, and DWM's
/// own corner and border for the pin — both give a hard-edged shape at one opacity, which
/// is enough for a frame and quite wrong for a ring that fades as it grows.
/// </para>
/// <para>
/// So this is a plain Win32 popup with <c>WS_EX_LAYERED</c>, updated through
/// <c>UpdateLayeredWindow</c>, which takes premultiplied BGRA and composites it against
/// the desktop itself. That is the only route on Windows to what macshot gets for free
/// from an <c>NSPanel</c> with a clear background.
/// </para>
/// <para>
/// Deliberately <em>not</em> excluded from capture: these overlays exist to be in the
/// recording. That is the opposite of the recording panel, which is excluded because it
/// is chrome rather than content.
/// </para>
/// </remarks>
internal sealed class LayeredOverlayWindow : IDisposable
{
    private const uint PopupStyle = 0x80000000;

    private const uint Layered = 0x00080000;
    private const uint ClickThrough = 0x00000020;
    private const uint NoActivate = 0x08000000;
    private const uint ToolWindow = 0x00000080;
    private const uint Topmost = 0x00000008;

    private const int ShowNoActivate = 4;
    private const int Hide = 0;

    /// <summary>ULW_ALPHA: use the bitmap's own alpha channel.</summary>
    private const uint UseAlpha = 0x00000002;

    /// <summary>AC_SRC_OVER with AC_SRC_ALPHA, the only blend that reads per-pixel alpha.</summary>
    private static readonly BlendFunction PerPixel = new()
    {
        BlendOp = 0,
        BlendFlags = 0,
        SourceConstantAlpha = 255,
        AlphaFormat = 1,
    };

    private const string OverlayClassName = "MacshotLayeredOverlay";

    private static readonly Lazy<string> Class = new(Register, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly int _width;
    private readonly int _height;
    private readonly IntPtr _window;
    private readonly IntPtr _memoryDc;
    private readonly IntPtr _bitmap;
    private readonly IntPtr _previous;
    private readonly IntPtr _bits;

    private bool _visible;
    private bool _disposed;

    /// <summary>
    /// Makes a hidden overlay <paramref name="width"/> × <paramref name="height"/>
    /// physical pixels. The size is fixed for the window's life, because the bitmap
    /// behind it is: a ring that grows is drawn into a buffer sized for its largest.
    /// </summary>
    public LayeredOverlayWindow(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _width = width;
        _height = height;

        _window = CreateWindowEx(
            Layered | ClickThrough | NoActivate | ToolWindow | Topmost,
            Class.Value,
            string.Empty,
            PopupStyle,
            0,
            0,
            width,
            height,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (_window == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Could not create a layered overlay window ({Marshal.GetLastWin32Error()}).");
        }

        // Negative height for a top-down bitmap, so row 0 of the buffer is the top row
        // on screen — bottom-up would silently draw everything upside down.
        var header = new BitmapInfoHeader
        {
            Size = Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
            Compression = 0,
        };

        var screen = GetDC(IntPtr.Zero);
        try
        {
            _memoryDc = CreateCompatibleDC(screen);
            _bitmap = CreateDIBSection(screen, ref header, 0, out _bits, IntPtr.Zero, 0);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }

        if (_memoryDc == IntPtr.Zero || _bitmap == IntPtr.Zero)
        {
            Dispose();
            throw new InvalidOperationException("Could not create the overlay's bitmap.");
        }

        _previous = SelectObject(_memoryDc, _bitmap);
    }

    /// <summary>
    /// Puts <paramref name="premultipliedBgra"/> on screen with its top-left corner at
    /// <paramref name="x"/>, <paramref name="y"/> in virtual-screen pixels.
    /// </summary>
    public void Show(byte[] premultipliedBgra, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(premultipliedBgra);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Marshal.Copy(premultipliedBgra, 0, _bits, _width * _height * 4);

        if (!_visible)
        {
            ShowWindow(_window, ShowNoActivate);
            _visible = true;
        }

        var position = new NativePoint { X = x, Y = y };
        var size = new NativeSize { Width = _width, Height = _height };
        var origin = default(NativePoint);
        var blend = PerPixel;

        UpdateLayeredWindow(
            _window,
            IntPtr.Zero,
            ref position,
            ref size,
            _memoryDc,
            ref origin,
            0,
            ref blend,
            UseAlpha);
    }

    /// <summary>Takes the overlay off screen without giving up its window or bitmap.</summary>
    public void Conceal()
    {
        if (_disposed || !_visible)
        {
            return;
        }

        ShowWindow(_window, Hide);
        _visible = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_memoryDc != IntPtr.Zero)
        {
            if (_previous != IntPtr.Zero)
            {
                SelectObject(_memoryDc, _previous);
            }

            DeleteDC(_memoryDc);
        }

        if (_bitmap != IntPtr.Zero)
        {
            DeleteObject(_bitmap);
        }

        if (_window != IntPtr.Zero)
        {
            DestroyWindow(_window);
        }
    }

    /// <summary>
    /// Registers the shared window class once per process.
    /// </summary>
    /// <remarks>
    /// The window procedure is <c>DefWindowProcW</c> taken by address rather than a
    /// managed delegate, so there is no callback whose lifetime has to outlive every
    /// window made from the class — a collected delegate would crash the process from
    /// inside the message pump, at a moment unrelated to anything this file did.
    /// </remarks>
    private static string Register()
    {
        var user32 = GetModuleHandle("user32.dll");
        var defaultProc = GetProcAddress(user32, "DefWindowProcW");

        var description = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            WindowProc = defaultProc,
            Instance = GetModuleHandle(null),
            ClassName = OverlayClassName,
        };

        var registered = RegisterClassEx(ref description);
        if (registered == 0)
        {
            throw new InvalidOperationException(
                $"Could not register the overlay window class ({Marshal.GetLastWin32Error()}).");
        }

        // The name rather than the atom: CreateWindowEx takes a pointer-sized argument
        // there, and an atom passed as a 16-bit value would be marshalled as two bytes
        // where eight are read.
        return OverlayClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string ClassName;
        public IntPtr SmallIcon;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass description);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        [MarshalAs(UnmanagedType.LPWStr)] string className,
        [MarshalAs(UnmanagedType.LPWStr)] string title,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(
        IntPtr window,
        IntPtr destinationDc,
        ref NativePoint destination,
        ref NativeSize size,
        IntPtr sourceDc,
        ref NativePoint source,
        uint colorKey,
        ref BlendFunction blend,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(
        IntPtr dc,
        ref BitmapInfoHeader header,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr handle);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true)]
    private static extern IntPtr GetModuleHandle([MarshalAs(UnmanagedType.LPWStr)] string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, [MarshalAs(UnmanagedType.LPStr)] string name);
}
