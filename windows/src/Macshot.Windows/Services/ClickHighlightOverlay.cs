using System.Runtime.InteropServices;
using Macshot.Windows.Core.Capture;
using Microsoft.UI.Dispatching;

namespace Macshot.Windows.Services;

/// <summary>
/// Blooms a ring out of every click for as long as a recording is running.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>MouseHighlightOverlay</c>. It watches for presses with a low-level mouse
/// hook rather than a global event monitor, and draws each ring in its own layered
/// window rather than into one full-screen panel — a full-screen WinUI window would be an
/// opaque sheet over the desktop, and a full-screen <em>layered</em> window would mean
/// pushing several megabytes through <c>UpdateLayeredWindow</c> thirty times a second to
/// move a 72-pixel circle.
/// </para>
/// <para>
/// The windows are pooled and reused. Creating one costs a window class lookup, a DC and
/// a DIB section, which is not something to do on the click itself; four is enough for
/// every ring macshot would have alive at once, since a ring lasts 0.3 s and nobody
/// clicks fourteen times a second.
/// </para>
/// </remarks>
internal sealed class ClickHighlightOverlay : IDisposable
{
    /// <summary>WH_MOUSE_LL.</summary>
    private const int LowLevelMouse = 14;

    private const int LeftButtonDown = 0x0201;
    private const int RightButtonDown = 0x0204;

    /// <summary>macshot's 1/30 s animation tick, in milliseconds.</summary>
    private const int TickMilliseconds = 33;

    /// <summary>How many rings may be alive at once.</summary>
    private const int MostAtOnce = 4;

    private readonly double _scale;
    private readonly int _extent;
    private readonly byte[] _pixels;
    private readonly Ring?[] _rings = new Ring?[MostAtOnce];
    private readonly DispatcherQueueTimer? _timer;

    // Held in a field for the hook's whole life: a collected delegate would be called
    // from inside the message pump and take the process down somewhere unrelated.
    private readonly HookProc _onMouse;

    private IntPtr _hook;
    private bool _disposed;

    /// <summary>
    /// Prepares the overlay for a display at <paramref name="scale"/>. Must be made on
    /// the UI thread: the hook is delivered to the thread that installed it, and that
    /// thread has to be one with a message pump.
    /// </summary>
    public ClickHighlightOverlay(double scale)
    {
        _scale = Math.Max(scale, 0.1);
        _extent = ClickHighlightRing.ExtentAt(_scale);
        _pixels = new byte[_extent * _extent * 4];
        _onMouse = OnMouse;
        _timer = DispatcherQueue.GetForCurrentThread()?.CreateTimer();

        if (_timer is not null)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(TickMilliseconds);
            _timer.Tick += (_, _) => Advance();
        }
    }

    /// <summary>
    /// Starts watching for clicks. Returns false when Windows refused the hook, which is
    /// not worth stopping a recording over — the recording is the point and the rings are
    /// decoration.
    /// </summary>
    public bool Start()
    {
        if (_disposed || _hook != IntPtr.Zero || _timer is null)
        {
            return false;
        }

        _hook = SetWindowsHookEx(LowLevelMouse, _onMouse, GetModuleHandle(null), 0);

        if (_hook == IntPtr.Zero)
        {
            DiagnosticLog.Verbose(
                $"click highlight: no mouse hook ({Marshal.GetLastWin32Error()}), clicks will not be marked");
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer?.Stop();

        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        foreach (var ring in _rings)
        {
            ring?.Window.Dispose();
        }
    }

    /// <summary>
    /// The hook itself. It runs on the thread that installed it — the UI thread — as part
    /// of that thread's message pump, so it may touch the pool directly, and it must
    /// return quickly: everything it delays is every mouse event on the machine.
    /// </summary>
    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        var button = (int)message;

        if (code >= 0 && (button == LeftButtonDown || button == RightButtonDown))
        {
            var click = Marshal.PtrToStructure<MouseHookInput>(data);
            Add(click.X, click.Y);
        }

        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private void Add(int x, int y)
    {
        if (_disposed || _timer is null)
        {
            return;
        }

        var slot = FreeSlot();

        try
        {
            _rings[slot] ??= new Ring(new LayeredOverlayWindow(_extent, _extent));
        }
        catch (InvalidOperationException error)
        {
            // A window that cannot be made is a missing ring, not a failed recording.
            DiagnosticLog.Verbose($"click highlight: {error.Message}");
            return;
        }

        var ring = _rings[slot]!;
        ring.Started = Environment.TickCount64;
        ring.X = x - (_extent / 2);
        ring.Y = y - (_extent / 2);
        ring.Alive = true;

        // Drawn here rather than waiting for the next tick, so the ring appears on the
        // press rather than up to a frame after it.
        Draw(ring, 0);
        _timer.Start();
    }

    /// <summary>
    /// The oldest slot, preferring one that is free — a fifth click inside 0.3 s takes
    /// the ring that has least of its life left.
    /// </summary>
    private int FreeSlot()
    {
        var oldest = 0;

        for (var i = 0; i < _rings.Length; i++)
        {
            if (_rings[i] is not { Alive: true })
            {
                return i;
            }

            if (_rings[i]!.Started < _rings[oldest]!.Started)
            {
                oldest = i;
            }
        }

        return oldest;
    }

    private void Advance()
    {
        var now = Environment.TickCount64;
        var living = 0;

        foreach (var ring in _rings)
        {
            if (ring is not { Alive: true })
            {
                continue;
            }

            var age = (now - ring.Started) / 1000.0;

            if (!ClickHighlightRing.IsAlive(age))
            {
                ring.Window.Conceal();
                ring.Alive = false;
                continue;
            }

            Draw(ring, age);
            living++;
        }

        // Stopped rather than left running, as macshot's timer is: a recording lasting an
        // hour should not be redrawing nothing thirty times a second throughout.
        if (living == 0)
        {
            _timer?.Stop();
        }
    }

    private void Draw(Ring ring, double age)
    {
        // One buffer for every ring: they are drawn one after another inside a single
        // tick, and each Show has copied its own pixels out before the next overwrites
        // them.
        ClickHighlightRing.Rasterize(age, _scale, _pixels);
        ring.Window.Show(_pixels, ring.X, ring.Y);
    }

    private sealed class Ring(LayeredOverlayWindow window)
    {
        public LayeredOverlayWindow Window { get; } = window;

        public long Started { get; set; }

        public int X { get; set; }

        public int Y { get; set; }

        public bool Alive { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int type, HookProc callback, IntPtr module, uint thread);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true)]
    private static extern IntPtr GetModuleHandle([MarshalAs(UnmanagedType.LPWStr)] string? name);
}
