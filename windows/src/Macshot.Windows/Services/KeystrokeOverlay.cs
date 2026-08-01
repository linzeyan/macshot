using System.Runtime.InteropServices;

using Macshot.Windows.Core.Capture;
using Microsoft.UI.Dispatching;

namespace Macshot.Windows.Services;

/// <summary>
/// Shows what is being typed, at the foot of the recorded region, for as long as a
/// recording runs.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>KeystrokeOverlay</c>. It watches with a low-level keyboard hook where
/// macshot uses a <c>CGEvent</c> tap; on Windows this needs no permission and no prompt,
/// which is the one place the port has less to explain than the Mac does.
/// </para>
/// <para>
/// The pill is a layered window rather than a WinUI one for the reason every fading
/// overlay here is: WinUI has no per-pixel transparency, and a pill at 65% black with a
/// white rim is nothing else.
/// </para>
/// </remarks>
internal sealed class KeystrokeOverlay : IDisposable
{
    /// <summary>WH_KEYBOARD_LL.</summary>
    private const int LowLevelKeyboard = 13;

    private const int KeyDown = 0x0100;
    private const int SystemKeyDown = 0x0104;
    private const int KeyUp = 0x0101;
    private const int SystemKeyUp = 0x0105;

    /// <summary>MAPVK_VK_TO_CHAR.</summary>
    private const uint ToCharacter = 2;

    private const int VirtualShift = 0x10;
    private const int VirtualControl = 0x11;
    private const int VirtualAlt = 0x12;
    private const int VirtualLeftWindows = 0x5B;
    private const int VirtualRightWindows = 0x5C;
    private const int VirtualCapsLock = 0x14;

    /// <summary>The high bit of GetKeyState's answer: the key is down now.</summary>
    private const short Down = unchecked((short)0x8000);

    private readonly double _scale;
    private readonly int _bufferWidth;
    private readonly int _bufferHeight;
    private readonly byte[] _pixels;
    private readonly int _centreX;
    private readonly int _foot;
    private readonly DispatcherQueueTimer? _timer;

    // Held in a field for the hook's whole life: a collected delegate would be called
    // from inside the message pump and take the process down somewhere unrelated.
    private readonly HookProc _onKey;

    private LayeredOverlayWindow? _window;
    private string _showing = string.Empty;
    private bool _modifiersOnly;
    private double _opacity;
    private long _shownAt;
    private IntPtr _hook;
    private bool _disposed;

    /// <summary>
    /// Prepares the overlay for a region being recorded, in virtual-screen pixels. Must be
    /// made on the UI thread: the hook is delivered to the thread that installed it, and
    /// that thread has to be one with a message pump.
    /// </summary>
    public KeystrokeOverlay(CaptureRegion region, double scale)
    {
        _scale = Math.Max(scale, 0.1);
        _bufferWidth = KeystrokePill.BufferWidthAt(_scale);
        _bufferHeight = KeystrokePill.BufferHeightAt(_scale);
        _pixels = new byte[_bufferWidth * _bufferHeight * 4];

        _centreX = (int)Math.Round(region.X + (region.Width / 2));

        // The buffer's bottom edge is the pill's foot, so the window's own foot goes
        // macshot's 40 above the region's.
        _foot = (int)Math.Round(region.Bottom - (KeystrokePill.BottomInset * _scale));

        _onKey = OnKey;
        _timer = DispatcherQueue.GetForCurrentThread()?.CreateTimer();

        if (_timer is not null)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(KeystrokePill.FadeIntervalMilliseconds);
            _timer.Tick += (_, _) => Advance();
        }
    }

    /// <summary>
    /// Whether every keystroke is shown, or only the ones that make a shortcut. Read on
    /// each press rather than held, so changing it mid-recording takes effect at once —
    /// macshot reads its default the same way.
    /// </summary>
    public bool ShowAll { get; set; }

    /// <summary>
    /// Starts watching the keyboard. Returns false when Windows refused the hook, which is
    /// not worth stopping a recording over.
    /// </summary>
    public bool Start()
    {
        if (_disposed || _hook != IntPtr.Zero || _timer is null)
        {
            return false;
        }

        _hook = SetWindowsHookEx(LowLevelKeyboard, _onKey, GetModuleHandle(null), 0);

        if (_hook == IntPtr.Zero)
        {
            DiagnosticLog.Verbose(
                $"keystroke overlay: no keyboard hook ({Marshal.GetLastWin32Error()}), "
                    + "keystrokes will not be shown");
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

        _window?.Dispose();
        _window = null;
    }

    /// <summary>
    /// The hook itself. It runs on the thread that installed it — the UI thread — as part
    /// of that thread's message pump, so it may touch the state directly, and it must
    /// return quickly: everything it delays is every keystroke on the machine.
    /// </summary>
    private IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var kind = (int)message;
            if (kind is KeyDown or SystemKeyDown)
            {
                Pressed(Marshal.PtrToStructure<KeyboardHookInput>(data).VirtualKey);
            }
            else if (kind is KeyUp or SystemKeyUp)
            {
                Released(Marshal.PtrToStructure<KeyboardHookInput>(data).VirtualKey);
            }
        }

        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private void Pressed(int virtualKey)
    {
        var modifiers = Modifiers();

        if (!KeystrokeNames.WorthShowing(virtualKey, modifiers, ShowAll))
        {
            return;
        }

        // A modifier on its own describes nothing, so what it gets is the modifier line —
        // which is macshot's flagsChanged branch, reached here by the only route Windows
        // offers, since it sends modifiers as ordinary key presses.
        var bare = KeystrokeNames.IsModifier(virtualKey);

        Show(
            bare
                ? KeystrokeNames.DescribeModifiers(modifiers)
                : KeystrokeNames.Describe(virtualKey, Typed(virtualKey), modifiers),
            bare);
    }

    private void Released(int virtualKey)
    {
        // Only while the pill is showing the modifiers themselves. Letting go of Ctrl in
        // the middle of a chord already on screen must not rub the chord out from under
        // the viewer — the chord is the thing they are meant to read.
        if (!ShowAll || !_modifiersOnly || !KeystrokeNames.IsModifier(virtualKey))
        {
            return;
        }

        Show(KeystrokeNames.DescribeModifiers(Modifiers()), modifiersOnly: true);
    }

    private static KeystrokeModifiers Modifiers()
    {
        var held = KeystrokeModifiers.None;

        if ((GetKeyState(VirtualControl) & Down) != 0)
        {
            held |= KeystrokeModifiers.Control;
        }

        if ((GetKeyState(VirtualAlt) & Down) != 0)
        {
            held |= KeystrokeModifiers.Alt;
        }

        if ((GetKeyState(VirtualShift) & Down) != 0)
        {
            held |= KeystrokeModifiers.Shift;
        }

        if (((GetKeyState(VirtualLeftWindows) | GetKeyState(VirtualRightWindows)) & Down) != 0)
        {
            held |= KeystrokeModifiers.Windows;
        }

        // The low bit rather than the high one: Caps Lock is a light that is on, not a
        // key that is down.
        if ((GetKeyState(VirtualCapsLock) & 1) != 0)
        {
            held |= KeystrokeModifiers.CapsLock;
        }

        return held;
    }

    private static char Typed(int virtualKey)
    {
        // The layout's own answer, so a French keyboard's A is an A and not a Q. The top
        // bit is set for a dead key, which types nothing on its own.
        var mapped = MapVirtualKey((uint)virtualKey, ToCharacter);
        return mapped is 0 or >= 0x80000000 ? '\0' : (char)(mapped & 0xFFFF);
    }

    private void Show(string text, bool modifiersOnly)
    {
        if (_disposed || _timer is null)
        {
            return;
        }

        _modifiersOnly = modifiersOnly;

        if (text.Length == 0)
        {
            _showing = string.Empty;
            _opacity = 0;
            _window?.Conceal();
            _timer.Stop();
            return;
        }

        _showing = text;
        _opacity = 1;
        _shownAt = Environment.TickCount64;

        // Drawn now rather than on the next tick, so the pill appears with the keystroke
        // rather than up to a frame after it.
        Draw();
        _timer.Start();
    }

    private void Advance()
    {
        if (_showing.Length == 0)
        {
            _timer?.Stop();
            return;
        }

        // Held at full strength first, then taken down a step at a time — the two halves
        // of macshot's showKeystroke/fadeOut pair.
        if ((Environment.TickCount64 - _shownAt) / 1000.0 < KeystrokePill.HoldSeconds)
        {
            return;
        }

        _opacity -= KeystrokePill.FadeStep;

        if (_opacity <= 0)
        {
            _showing = string.Empty;
            _opacity = 0;
            _window?.Conceal();
            _timer?.Stop();
            return;
        }

        Draw();
    }

    private void Draw()
    {
        if (KeystrokeTextMask.Render(_showing, _scale) is not { } drawn)
        {
            return;
        }

        var pillHeight = KeystrokePill.Rasterize(
            drawn.Mask,
            drawn.Width,
            drawn.Height,
            _opacity,
            _scale,
            _pixels,
            _bufferWidth,
            _bufferHeight);

        if (pillHeight == 0)
        {
            return;
        }

        try
        {
            _window ??= new LayeredOverlayWindow(_bufferWidth, _bufferHeight);
        }
        catch (InvalidOperationException error)
        {
            // A window that cannot be made is a missing pill, not a failed recording.
            DiagnosticLog.Verbose($"keystroke overlay: {error.Message}");
            _timer?.Stop();
            return;
        }

        _window.Show(_pixels, _centreX - (_bufferWidth / 2), _foot - _bufferHeight);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookInput
    {
        public int VirtualKey;
        public int ScanCode;
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

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapping);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true)]
    private static extern IntPtr GetModuleHandle([MarshalAs(UnmanagedType.LPWStr)] string? name);
}
