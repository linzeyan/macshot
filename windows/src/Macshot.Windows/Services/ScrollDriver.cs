using System.Runtime.InteropServices;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Services;

/// <summary>
/// Scrolls someone else's window, by sending the wheel input a user would.
/// </summary>
/// <remarks>
/// <para>
/// The obvious alternative is to post <c>WM_MOUSEWHEEL</c> straight at the window,
/// which needs neither the foreground nor the pointer. It was not taken because it
/// does not work where it matters: Chromium, Electron and WinUI apps route wheel
/// input through the hovered surface and real input queues, and ignore a message
/// posted at the top-level window. Those are most of what anyone wants a long
/// screenshot of, so the mechanism that works everywhere wins over the one that is
/// tidier.
/// </para>
/// <para>
/// The cost is that this is real input. The target has to be foreground, the
/// pointer has to be over it, and both are put back afterwards. Two things follow
/// that no amount of care here removes: a window running elevated cannot be driven
/// by an unelevated macshot, because UIPI drops the input silently; and if the user
/// moves the mouse mid-capture, the wheel goes wherever they moved it. Neither is
/// papered over — both show up as a capture that stops advancing.
/// </para>
/// </remarks>
public sealed class ScrollDriver
{
    /// <summary>One wheel notch, as Windows defines it.</summary>
    private const int WheelDelta = 120;

    private const uint InputMouse = 0;
    private const uint MouseEventWheel = 0x0800;

    private readonly int _notchesPerStep;

    /// <param name="notchesPerStep">
    /// Wheel notches sent per step. Small enough that consecutive frames still
    /// overlap by more than the stitcher's match band — without an overlap there is
    /// nothing to match and the page tears silently — and large enough that a long
    /// page does not take hundreds of frames.
    /// </param>
    public ScrollDriver(int notchesPerStep = 3)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(notchesPerStep);
        _notchesPerStep = notchesPerStep;
    }

    /// <summary>
    /// Brings <paramref name="window"/> forward and parks the pointer in the middle
    /// of it, which is where wheel input has to come from to reach it.
    /// </summary>
    /// <remarks>
    /// Windows only grants the foreground to a process that already has it, so this
    /// has to run while macshot is the foreground app — from the click that starts
    /// the capture, not from a timer later on.
    /// </remarks>
    public bool TryTakeOver(CaptureWindow window, out CapturePoint restoreCursorTo)
    {
        restoreCursorTo = GetCursorPos(out var cursor)
            ? new CapturePoint(cursor.X, cursor.Y)
            : default;

        if (!WindowEnumerator.TryGetBounds(window.Id, out _, out var bounds) || bounds.IsEmpty)
        {
            return false;
        }

        _ = SetForegroundWindow((IntPtr)window.Id);

        // The middle of the window, because that is where the scrollable content is
        // on almost anything: a corner is as likely to be a sidebar or a ruler, and
        // wheel input lands wherever the pointer actually is.
        return SetCursorPos(
            (int)(bounds.X + (bounds.Width / 2)),
            (int)(bounds.Y + (bounds.Height / 2)));
    }

    /// <summary>Sends one step's worth of wheel-down.</summary>
    public void StepDown() => SendWheel(-_notchesPerStep * WheelDelta);

    /// <summary>Puts the pointer back where the user left it.</summary>
    public void Restore(CapturePoint cursor) => SetCursorPos((int)cursor.X, (int)cursor.Y);

    private static void SendWheel(int amount)
    {
        var input = new Input
        {
            Type = InputMouse,
            Mouse = new MouseInput
            {
                MouseData = unchecked((uint)amount),
                Flags = MouseEventWheel,
            },
        };

        // A failure here is not thrown on: a wheel that went nowhere reaches the
        // capture as a frame that did not advance, which is the same thing it has to
        // handle when the page simply ends.
        _ = SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    /// <remarks>
    /// Only the mouse arm of the native union is declared. It is the largest of the
    /// three, so the struct is still the size <c>SendInput</c> checks against, and
    /// declaring the other two would add nothing but a chance to get their layout
    /// wrong.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
