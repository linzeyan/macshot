using System.Runtime.InteropServices;
using System.Text;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Services;

/// <summary>
/// The top-level windows currently on screen, front to back, in virtual-screen
/// pixels. This is the input <see cref="WindowSnapper"/> needs, and the only part
/// of window snapping that has to ask Windows anything.
/// </summary>
/// <remarks>
/// <para>
/// Bounds come from <c>DWMWA_EXTENDED_FRAME_BOUNDS</c> rather than
/// <c>GetWindowRect</c>. Since Vista the latter includes the invisible resize
/// border the compositor keeps outside the frame, so a highlight drawn from it
/// sits several pixels wide of the window on every edge, and a capture taken from
/// it carries a band of whatever is behind.
/// </para>
/// <para>
/// The list is taken once, when the screenshot is, because the overlay shows
/// frozen pixels: re-enumerating while the user hovers would start answering
/// about a desktop that no longer matches what they are looking at.
/// </para>
/// </remarks>
public static class WindowEnumerator
{
    private const int ExtendedFrameBounds = 9;
    private const int Cloaked = 14;

    private const int ExStyleIndex = -20;
    private const int ToolWindow = 0x00000080;

    /// <summary>
    /// The shell's own windows. The desktop ones cover a whole display, so
    /// hovering over empty wallpaper would light the entire screen up as though it
    /// were a window; the taskbars are chrome nobody screenshots by pointing at
    /// them.
    /// </summary>
    private static readonly string[] ShellClasses =
    [
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
    ];

    public static IReadOnlyList<CaptureWindow> EnumerateFrontToBack()
    {
        var windows = new List<CaptureWindow>();
        var ownProcess = GetCurrentProcessId();

        // EnumWindows walks top-level windows in z-order, front first, which is the
        // order the snapper needs and cannot recover on its own.
        var callback = new EnumWindowsProc((window, _) =>
        {
            if (IsSnapCandidate(window, ownProcess) && TryGetFrameBounds(window, out var bounds))
            {
                windows.Add(new CaptureWindow((long)window, bounds));
            }

            return true;
        });

        // A failure here leaves nothing to snap to rather than failing the capture:
        // dragging out a selection still works.
        _ = EnumWindows(callback, IntPtr.Zero);
        return windows;
    }

    /// <summary>
    /// Both rectangles Windows has for one window: the outer one a capture item
    /// covers, and the visible one inside it. Asked again at capture time rather
    /// than carried from the enumeration, because only a capture of the window
    /// itself needs the outer rectangle, and only as one half of the pair
    /// <see cref="WindowFrameCrop"/> takes.
    /// </summary>
    public static bool TryGetBounds(long windowId, out CaptureRegion windowRect, out CaptureRegion visibleBounds)
    {
        var window = (IntPtr)windowId;

        windowRect = GetWindowRect(window, out var outer)
            ? CaptureRegion.FromPoints(outer.Left, outer.Top, outer.Right, outer.Bottom)
            : default;

        return TryGetFrameBounds(window, out visibleBounds);
    }

    private static bool IsSnapCandidate(IntPtr window, uint ownProcess)
    {
        // macshot's own overlays are always on top and cover every display, so
        // without this every hover would snap to the overlay under the pointer.
        _ = GetWindowThreadProcessId(window, out var process);
        if (process == ownProcess)
        {
            return false;
        }

        if (!IsWindowVisible(window) || IsIconic(window))
        {
            return false;
        }

        // A suspended store app keeps a visible window with real bounds that draws
        // nothing. It is not in the screenshot, so it must not be snappable either.
        if (DwmGetWindowAttribute(window, Cloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        if ((GetWindowLong(window, ExStyleIndex) & ToolWindow) != 0)
        {
            return false;
        }

        return !IsShellWindow(window);
    }

    private static bool IsShellWindow(IntPtr window)
    {
        var name = new StringBuilder(64);
        if (GetClassName(window, name, name.Capacity) == 0)
        {
            return false;
        }

        var className = name.ToString();
        return Array.Exists(ShellClasses, shell => string.Equals(shell, className, StringComparison.Ordinal));
    }

    private static bool TryGetFrameBounds(IntPtr window, out CaptureRegion bounds)
    {
        bounds = default;

        // GetWindowRect is the fallback rather than the source: it is only reached
        // when DWM has nothing to say, which on a composited desktop means a window
        // that is not being composited at all.
        if (DwmGetWindowAttribute(window, ExtendedFrameBounds, out Rect frame, Marshal.SizeOf<Rect>()) != 0
            && !GetWindowRect(window, out frame))
        {
            return false;
        }

        bounds = CaptureRegion.FromPoints(frame.Left, frame.Top, frame.Right, frame.Bottom);
        return !bounds.IsEmpty;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    // The 32-bit entry point, which is right for GWL_EXSTYLE on every
    // architecture: only the pointer-sized fields need GetWindowLongPtr.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
