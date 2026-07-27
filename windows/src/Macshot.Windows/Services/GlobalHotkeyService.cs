using System.Runtime.InteropServices;

namespace Macshot.Windows.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int GwlWndProc = -4;
    private const uint WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;

    private readonly IntPtr _windowHandle;
    private readonly WindowProcedure _windowProcedure;
    private readonly Dictionary<int, Action> _handlers = [];
    private IntPtr _previousWindowProcedure;
    private bool _disposed;

    public GlobalHotkeyService(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
        }

        _windowHandle = windowHandle;
        _windowProcedure = ProcessWindowMessage;
        _previousWindowProcedure = SetWindowLongPtr(_windowHandle, GwlWndProc, _windowProcedure);
        if (_previousWindowProcedure == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to attach the global hotkey message handler.");
        }
    }

    public void RegisterControlShift(int id, char key, Action handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        ArgumentNullException.ThrowIfNull(handler);

        if (_handlers.ContainsKey(id))
        {
            throw new InvalidOperationException($"A global hotkey is already registered with id {id}.");
        }

        var virtualKey = char.ToUpperInvariant(key);
        if (!RegisterHotKey(_windowHandle, id, ModControl | ModShift, virtualKey))
        {
            throw new InvalidOperationException($"Unable to register Ctrl+Shift+{virtualKey}.");
        }

        _handlers.Add(id, handler);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var id in _handlers.Keys)
        {
            UnregisterHotKey(_windowHandle, id);
        }

        _handlers.Clear();
        SetWindowLongPtr(_windowHandle, GwlWndProc, _previousWindowProcedure);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private IntPtr ProcessWindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmHotkey && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handler();
            return IntPtr.Zero;
        }

        return CallWindowProc(_previousWindowProcedure, window, message, wParam, lParam);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, WindowProcedure procedure);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr procedure);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(
        IntPtr previousWindowProcedure,
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);
}
