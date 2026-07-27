using System.Runtime.InteropServices;

namespace Macshot.Windows.Services;

/// <summary>
/// The notification-area icon and its context menu, which is macshot's primary
/// entry point. It is the Windows counterpart of the macOS <c>NSStatusItem</c>.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const uint NotifyIconAdd = 0x00000000;
    private const uint NotifyIconDelete = 0x00000002;
    private const uint NotifyIconFlagMessage = 0x00000001;
    private const uint NotifyIconFlagIcon = 0x00000002;
    private const uint NotifyIconFlagTip = 0x00000004;

    /// <summary>A private message in the WM_APP range, which is reserved for the application.</summary>
    private const uint TrayCallbackMessage = 0x8000 + 1;

    private const uint WmLeftButtonUp = 0x0202;
    private const uint WmRightButtonUp = 0x0205;
    private const uint WmNull = 0x0000;

    private const uint MenuString = 0x00000000;
    private const uint MenuSeparator = 0x00000800;
    private const uint TrackReturnCommand = 0x0100;
    private const uint TrackRightButton = 0x0002;
    private const uint TrackNoNotify = 0x0080;

    private const int ApplicationIcon = 32512;
    private const uint IconId = 1;

    private readonly MessageWindow _window;
    private readonly List<(int Id, string? Text)> _menuItems = [];
    private bool _disposed;

    public TrayIconService(MessageWindow window, string tooltip)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        ArgumentException.ThrowIfNullOrWhiteSpace(tooltip);

        var data = CreateData();
        data.Flags = NotifyIconFlagMessage | NotifyIconFlagIcon | NotifyIconFlagTip;
        data.CallbackMessage = TrayCallbackMessage;

        // The stock application icon keeps the shell entry point working without a
        // packaged asset. Replace it with the macshot icon once branding lands.
        data.Icon = LoadIcon(IntPtr.Zero, new IntPtr(ApplicationIcon));
        data.Tip = tooltip;

        if (!ShellNotifyIcon(NotifyIconAdd, ref data))
        {
            throw new InvalidOperationException("Unable to add the macshot notification area icon.");
        }

        _window.MessageReceived += OnMessageReceived;
    }

    /// <summary>Raised with the id of the chosen context menu item.</summary>
    public event EventHandler<int>? CommandInvoked;

    /// <summary>Raised on a left click, which should do the most common thing.</summary>
    public event EventHandler? DefaultActionInvoked;

    public void AddMenuItem(int id, string text)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        _menuItems.Add((id, text));
    }

    public void AddSeparator() => _menuItems.Add((0, null));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.MessageReceived -= OnMessageReceived;

        var data = CreateData();
        ShellNotifyIcon(NotifyIconDelete, ref data);
        GC.SuppressFinalize(this);
    }

    private NotifyIconData CreateData()
    {
        return new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = _window.Handle,
            Id = IconId,
            Tip = string.Empty,
            Info = string.Empty,
            InfoTitle = string.Empty,
        };
    }

    private void OnMessageReceived(object? sender, WindowMessageEventArgs args)
    {
        if (args.Message != TrayCallbackMessage)
        {
            return;
        }

        args.Handled = true;

        // The shell packs the mouse message that triggered the callback into the
        // low word of lParam.
        var mouseMessage = (uint)(args.LParam.ToInt64() & 0xFFFF);
        switch (mouseMessage)
        {
        case WmLeftButtonUp:
            DefaultActionInvoked?.Invoke(this, EventArgs.Empty);
            break;
        case WmRightButtonUp:
            ShowContextMenu();
            break;
        }
    }

    private void ShowContextMenu()
    {
        if (_menuItems.Count == 0)
        {
            return;
        }

        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            foreach (var (id, text) in _menuItems)
            {
                if (text is null)
                {
                    AppendMenu(menu, MenuSeparator, UIntPtr.Zero, null);
                }
                else
                {
                    AppendMenu(menu, MenuString, new UIntPtr((uint)id), text);
                }
            }

            if (!GetCursorPos(out var cursor))
            {
                return;
            }

            // Documented requirement: a tray menu only dismisses correctly when its
            // owner is foreground, and the window needs a nudge afterwards so the
            // menu closes when the user clicks away.
            SetForegroundWindow(_window.Handle);
            var command = TrackPopupMenuEx(
                menu,
                TrackReturnCommand | TrackRightButton | TrackNoNotify,
                cursor.X,
                cursor.Y,
                _window.Handle,
                IntPtr.Zero);
            PostMessage(_window.Handle, WmNull, IntPtr.Zero, IntPtr.Zero);

            if (command != 0)
            {
                CommandInvoked?.Invoke(this, command);
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", EntryPoint = "LoadIconW", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr itemId, string? item);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenuEx(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        IntPtr window,
        IntPtr parameters);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid ItemGuid;
        public IntPtr BalloonIcon;
    }
}
