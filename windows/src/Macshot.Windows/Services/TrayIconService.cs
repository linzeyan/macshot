using System.Globalization;
using System.Runtime.InteropServices;

namespace Macshot.Windows.Services;

/// <summary>One entry in a submenu, built at the moment the menu is opened.</summary>
/// <summary>One line of a tray submenu.</summary>
/// <param name="Id">The command reported when it is chosen.</param>
/// <param name="Text">What it says.</param>
/// <param name="Checked">
/// Whether it carries a tick. macshot's capture-delay submenu marks the delay in force,
/// which is the only thing that says what a bare number of seconds is currently set to.
/// </param>
public readonly record struct TrayMenuEntry(int Id, string Text, bool Checked = false);

/// <summary>
/// The notification-area icon and its context menu, which is macshot's primary
/// entry point. It is the Windows counterpart of the macOS <c>NSStatusItem</c>.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const uint NotifyIconAdd = 0x00000000;
    private const uint NotifyIconModify = 0x00000001;
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
    private const uint MenuPopup = 0x00000010;
    private const uint MenuGrayed = 0x00000001;
    private const uint MenuChecked = 0x00000008;
    private const uint TrackReturnCommand = 0x0100;
    private const uint TrackRightButton = 0x0002;
    private const uint TrackNoNotify = 0x0080;

    private const int ApplicationIcon = 32512;
    private const uint IconId = 1;

    private const uint ImageTypeIcon = 1;
    private const uint LoadFromFile = 0x00000010;

    /// <summary>SM_CXSMICON / SM_CYSMICON: the size the shell wants for a tray icon.</summary>
    private const int SmallIconWidth = 49;

    private const int SmallIconHeight = 50;

    private readonly MessageWindow _window;
    private readonly List<MenuEntry> _menuItems = [];
    private readonly bool _visible;

    /// <summary>
    /// The icon the shell is currently showing, and whether it is ours to destroy.
    /// </summary>
    /// <remarks>
    /// The stock application icon is a shared system one and must be left alone; the two
    /// that <c>LoadImage</c> produces are ours, and replacing one without destroying it
    /// leaks a handle every time the user picks a different file.
    /// </remarks>
    private IntPtr _icon;
    private bool _iconOwned;

    private bool _disposed;

    /// <param name="visible">
    /// Whether the icon is put in the notification area at all. macshot's
    /// <c>hideMenuBarIcon</c>: the shortcuts still work without it, which is the point —
    /// someone who captures by hotkey has no use for an icon sitting in the tray. The
    /// menu is still built either way, so nothing downstream has to know.
    /// </param>
    /// <param name="iconPath">
    /// An icon file to show instead of macshot's own, or null for macshot's. Anything
    /// that cannot be read falls back to macshot's, so a file that has since been moved
    /// or deleted costs the user their choice and not their way into the app.
    /// </param>
    public TrayIconService(MessageWindow window, string tooltip, bool visible = true, string? iconPath = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        ArgumentException.ThrowIfNullOrWhiteSpace(tooltip);

        var data = CreateData();
        data.Flags = NotifyIconFlagMessage | NotifyIconFlagIcon | NotifyIconFlagTip;
        data.CallbackMessage = TrayCallbackMessage;

        (_icon, _iconOwned) = LoadTrayIcon(iconPath);
        data.Icon = _icon;
        data.Tip = tooltip;

        _visible = visible;

        if (!visible)
        {
            return;
        }

        if (!ShellNotifyIcon(NotifyIconAdd, ref data))
        {
            throw new InvalidOperationException("Unable to add the macshot notification area icon.");
        }

        _window.MessageReceived += OnMessageReceived;
    }

    /// <summary>
    /// Shows <paramref name="path"/> instead of macshot's own icon, or macshot's again
    /// when it is null.
    /// </summary>
    /// <remarks>
    /// Changed in place rather than at the next launch, because macshot's own setting
    /// takes effect as it is chosen — and a background app with no window is one the
    /// user would have to be told how to restart.
    /// </remarks>
    public void SetIcon(string? path)
    {
        if (_disposed || !_visible)
        {
            // Nothing is on screen to change. The path is still in the settings, so it
            // takes effect the moment the icon is shown again.
            return;
        }

        var (icon, owned) = LoadTrayIcon(path);

        var data = CreateData();
        data.Flags = NotifyIconFlagIcon;
        data.Icon = icon;

        if (!ShellNotifyIcon(NotifyIconModify, ref data))
        {
            // The shell kept the old icon, so the new one is ours to throw away. Left
            // alone it would be a handle leaked for a change that did not happen.
            DiagnosticLog.Write("Windows refused the new notification area icon.");
            if (owned)
            {
                DestroyIcon(icon);
            }

            return;
        }

        // Only after the shell has taken the new one: destroying an icon it is still
        // drawing is how a tray icon turns into a black square.
        ReleaseIcon();
        _icon = icon;
        _iconOwned = owned;
    }

    /// <summary>Raised with the id of the chosen context menu item.</summary>
    public event EventHandler<int>? CommandInvoked;

    /// <summary>Raised on a left click, which should do the most common thing.</summary>
    public event EventHandler? DefaultActionInvoked;

    public void AddMenuItem(int id, string text)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        _menuItems.Add(new MenuEntry(id, text, null));
    }

    /// <summary>
    /// Adds a submenu whose contents are asked for each time the menu is opened.
    /// </summary>
    /// <remarks>
    /// A callback rather than a list, because the entries this exists for — the recent
    /// captures — change with every capture, and a menu built once in the constructor
    /// would go on offering whatever was there when macshot started.
    /// </remarks>
/// <param name="emptyText">
    /// What the submenu says when there is nothing in it. Passed in rather than written
    /// here so it goes through the same translation lookup as every other menu string.
    /// </param>
    public void AddSubmenu(string text, Func<IReadOnlyList<TrayMenuEntry>> items, string emptyText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(emptyText);
        _menuItems.Add(new MenuEntry(0, text, items, emptyText));
    }

    /// <summary>
    /// Renames an entry already in the menu, doing nothing when there is no such id.
    /// </summary>
    /// <remarks>
    /// The entries that name a keyboard shortcut have to be able to change: a menu
    /// still offering Ctrl+Shift+X after the user has moved that shortcut is worse
    /// than one that never mentioned it.
    /// </remarks>
    public void SetMenuItemText(int id, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var index = _menuItems.FindIndex(entry => entry.Text is not null && entry.Submenu is null && entry.Id == id);
        if (index >= 0)
        {
            _menuItems[index] = _menuItems[index] with { Text = text };
        }
    }

    /// <summary>
    /// Puts the entries named by <paramref name="ids"/> into that order, in the places
    /// they already occupy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The slots stay where they are and only their contents are dealt out again, so
    /// nothing else in the menu moves and no separator can end up in the wrong half. An
    /// id that is not in the menu is ignored, and an entry not named keeps its own slot.
    /// </para>
    /// <para>
    /// Rearranged rather than rebuilt because the entries carry their keyboard shortcuts
    /// in their text by then — rebuilding the menu would either lose those or need every
    /// shortcut applied again afterwards, in the right order, every time the order
    /// changed.
    /// </para>
    /// </remarks>
    public void SetMenuItemOrder(IReadOnlyList<int> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var slots = new List<int>(ids.Count);
        foreach (var id in ids)
        {
            var index = _menuItems.FindIndex(entry =>
                entry.Text is not null && entry.Submenu is null && entry.Id == id);

            if (index >= 0)
            {
                slots.Add(index);
            }
        }

        // The slots in the order they appear in the menu, so the first named entry lands
        // in the topmost of them however the ids were given.
        var ordered = slots.Select(slot => _menuItems[slot]).ToList();
        slots.Sort();

        for (var position = 0; position < slots.Count; position++)
        {
            _menuItems[slots[position]] = ordered[position];
        }
    }

    public void AddSeparator() => _menuItems.Add(new MenuEntry(0, null, null));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);

        if (!_visible)
        {
            ReleaseIcon();
            return;
        }

        _window.MessageReceived -= OnMessageReceived;

        var data = CreateData();
        ShellNotifyIcon(NotifyIconDelete, ref data);

        // After the icon is out of the tray, for the reason SetIcon destroys late.
        ReleaseIcon();
    }

    /// <summary>Gives back the current icon, if it was ours to give back.</summary>
    private void ReleaseIcon()
    {
        if (_iconOwned && _icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
        }

        _icon = IntPtr.Zero;
        _iconOwned = false;
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
            foreach (var entry in _menuItems)
            {
                if (entry.Text is null)
                {
                    AppendMenu(menu, MenuSeparator, UIntPtr.Zero, null);
                }
                else if (entry.Submenu is { } items)
                {
                    // Owned by the parent from here: DestroyMenu takes the submenus
                    // with it, so the popup below needs no cleanup of its own.
                    AppendMenu(
                        menu,
                        MenuPopup,
                        new UIntPtr((ulong)BuildSubmenu(items(), entry.EmptyText!).ToInt64()),
                        Literal(entry.Text));
                }
                else
                {
                    AppendMenu(menu, MenuString, new UIntPtr((uint)entry.Id), Literal(entry.Text));
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

    /// <summary>
    /// The macshot icon at the size the shell wants for the current DPI, falling back
    /// to the stock application icon.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked for at an explicit size rather than taking whatever frame comes first. The
    /// notification area wants a small-icon-sized bitmap, which is 16 pixels at 100% and
    /// 24 at 150%, and <c>LoadImage</c> given those dimensions picks the matching frame
    /// out of the icon group. Letting the shell scale a 256-pixel frame down instead is
    /// what makes a tray icon look muddy next to every other one in the row.
    /// </para>
    /// <para>
    /// The loose file first, then the copy embedded in the executable. Two sources
    /// because only one of them is certain: the embedded copy is there whenever the
    /// build succeeded, while the loose one depends on the output being copied and can
    /// be replaced without a rebuild.
    /// </para>
    /// <para>
    /// A failure falls back to the stock icon rather than throwing. An icon that is not
    /// macshot's is a cosmetic fault; no icon at all is an app with no way in.
    /// </para>
    /// </remarks>
    /// <param name="customPath">
    /// The user's own icon file, tried before macshot's two. Not their problem if it has
    /// gone: an icon file names a place on disk, and places on disk are renamed, moved
    /// and deleted by people who have forgotten what pointed at them.
    /// </param>
    /// <returns>
    /// The icon, and whether the caller owns it. Only the stock system icon is not ours.
    /// </returns>
    private static (IntPtr Icon, bool Owned) LoadTrayIcon(string? customPath)
    {
        var width = GetSystemMetrics(SmallIconWidth);
        var height = GetSystemMetrics(SmallIconHeight);

        try
        {
            if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            {
                var chosen = LoadImage(IntPtr.Zero, customPath, ImageTypeIcon, width, height, LoadFromFile);
                if (chosen != IntPtr.Zero)
                {
                    return (chosen, true);
                }

                // Named, because this is the one failure here the user can act on: they
                // chose the file, and a settings window that silently kept macshot's icon
                // would leave them thinking the setting does nothing.
                DiagnosticLog.Write($"'{customPath}' is not an icon Windows can read; using macshot's own.");
            }

            // The loose file first, because it is the copy that can be replaced without
            // rebuilding.
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "macshot.ico");
            if (File.Exists(path))
            {
                var fromFile = LoadImage(IntPtr.Zero, path, ImageTypeIcon, width, height, LoadFromFile);
                if (fromFile != IntPtr.Zero)
                {
                    return (fromFile, true);
                }
            }

            // Then the copy embedded in the executable by ApplicationIcon, which is
            // there whether or not the loose one was copied to the output. Asking for a
            // size still picks the right frame out of the icon group.
            var embedded = LoadImage(
                GetModuleHandle(null),
                "#" + ApplicationIcon.ToString(CultureInfo.InvariantCulture),
                ImageTypeIcon,
                width,
                height,
                0);

            if (embedded != IntPtr.Zero)
            {
                return (embedded, true);
            }

            DiagnosticLog.Write("The macshot icon could not be loaded; using the stock Windows icon.");
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"The macshot icon could not be loaded: {exception.Message}");
        }

        // Shared and owned by the system: destroying this one is destroying every
        // program's copy of it.
        return (LoadIcon(IntPtr.Zero, new IntPtr(ApplicationIcon)), false);
    }

    /// <summary>
    /// A popup holding the given entries, or a single greyed line when there are
    /// none — an empty submenu opens as a blank rectangle, which reads as a defect
    /// rather than as "nothing here yet".
    /// </summary>
    private static IntPtr BuildSubmenu(IReadOnlyList<TrayMenuEntry> entries, string emptyText)
    {
        var submenu = CreatePopupMenu();
        if (submenu == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        if (entries.Count == 0)
        {
            AppendMenu(submenu, MenuString | MenuGrayed, UIntPtr.Zero, Literal(emptyText));
            return submenu;
        }

        foreach (var entry in entries)
        {
            AppendMenu(
                submenu,
                entry.Checked ? MenuString | MenuChecked : MenuString,
                new UIntPtr((uint)entry.Id),
                Literal(entry.Text));
        }

        return submenu;
    }

    /// <summary>
    /// Menu text that means what it says.
    /// </summary>
    /// <remarks>
    /// A single ampersand in a Win32 menu underlines the letter after it and swallows
    /// itself, so "Capture OCR &amp; QR" would come up as "Capture OCR QR" with the Q
    /// underlined — and a capture entitled "Q&amp;A notes" in the recent list would lose
    /// its ampersand too. Doubling turns every one of them back into a character.
    /// Nothing here sets a mnemonic deliberately: the entries are translated strings and
    /// the captures are file names, neither of which can carry one.
    /// </remarks>
    private static string Literal(string text) =>
        text.Replace("&", "&&", StringComparison.Ordinal);

    /// <summary>
    /// A menu line: a command, a separator (no text), or a submenu (a callback that
    /// produces the entries when the menu is opened).
    /// </summary>
    private sealed record MenuEntry(
        int Id,
        string? Text,
        Func<IReadOnlyList<TrayMenuEntry>>? Submenu,
        string? EmptyText = null);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", EntryPoint = "LoadIconW", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr instance,
        string name,
        uint type,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

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
