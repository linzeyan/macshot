using System.Runtime.InteropServices;

namespace Macshot.Windows.Services;

public sealed class WindowMessageEventArgs(uint message, IntPtr wParam, IntPtr lParam) : EventArgs
{
    public uint Message { get; } = message;

    public IntPtr WParam { get; } = wParam;

    public IntPtr LParam { get; } = lParam;

    /// <summary>Set to stop the message reaching <c>DefWindowProc</c>.</summary>
    public bool Handled { get; set; }

    public IntPtr Result { get; set; }
}

/// <summary>
/// A message-only window that lives for the whole process.
/// </summary>
/// <remarks>
/// macshot is a background tool: it must keep receiving global hotkeys and
/// notification-area callbacks while no UI is open. Hanging those off a visible
/// window's message loop ties them to that window's lifetime, so closing the
/// preview would silently kill the hotkey. A window parented to
/// <c>HWND_MESSAGE</c> never appears, never takes focus, and costs nothing.
/// </remarks>
public sealed class MessageWindow : IDisposable
{
    private const int HwndMessage = -3;

    private readonly WindowProcedure _windowProcedure;
    private readonly string _className;
    private readonly IntPtr _instance;
    private bool _disposed;

    public MessageWindow()
    {
        // The delegate must stay reachable for as long as the window exists, or the
        // GC collects the thunk Windows is calling through.
        _windowProcedure = ProcessMessage;
        _instance = GetModuleHandle(null);

        // A unique class name keeps a rebuilt window from colliding with a class a
        // previous instance has not unregistered yet.
        _className = $"MacshotMessageWindow-{Guid.NewGuid():N}";

        var windowClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
            Instance = _instance,
            ClassName = _className,
        };

        if (RegisterClassEx(ref windowClass) == 0)
        {
            throw new InvalidOperationException("Unable to register the macshot message window class.");
        }

        Handle = CreateWindowEx(
            0,
            _className,
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            new IntPtr(HwndMessage),
            IntPtr.Zero,
            _instance,
            IntPtr.Zero);

        if (Handle == IntPtr.Zero)
        {
            UnregisterClass(_className, _instance);
            throw new InvalidOperationException("Unable to create the macshot message window.");
        }
    }

    public event EventHandler<WindowMessageEventArgs>? MessageReceived;

    public IntPtr Handle { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Handle != IntPtr.Zero)
        {
            DestroyWindow(Handle);
        }

        UnregisterClass(_className, _instance);
        GC.SuppressFinalize(this);
    }

    private IntPtr ProcessMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        var args = new WindowMessageEventArgs(message, wParam, lParam);
        MessageReceived?.Invoke(this, args);
        return args.Handled ? args.Result : DefWindowProc(window, message, wParam, lParam);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
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

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProcedure;
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
}
