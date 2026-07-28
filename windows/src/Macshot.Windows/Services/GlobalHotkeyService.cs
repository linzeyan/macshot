using System.Runtime.InteropServices;

namespace Macshot.Windows.Services;

/// <summary>
/// Registers process-wide hotkeys against a <see cref="MessageWindow"/>.
/// </summary>
/// <remarks>
/// This used to subclass a visible window's <c>WndProc</c>, which meant the
/// hotkey stopped working the moment that window closed. Owning the messages
/// through the process-lifetime message window is what lets macshot behave like
/// the background tool it is.
/// </remarks>
public sealed class GlobalHotkeyService : IDisposable
{
    private const uint WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;

    /// <summary>Stops the shortcut repeating while the key is held down.</summary>
    private const uint ModNoRepeat = 0x4000;

    private readonly MessageWindow _window;
    private readonly Dictionary<int, Action> _handlers = [];
    private bool _disposed;

    public GlobalHotkeyService(MessageWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _window.MessageReceived += OnMessageReceived;
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
        if (!RegisterHotKey(_window.Handle, id, ModControl | ModShift | ModNoRepeat, virtualKey))
        {
            throw new InvalidOperationException(
                $"Unable to register Ctrl+Shift+{virtualKey}. Another application may already own it.");
        }

        _handlers.Add(id, handler);
    }

    /// <summary>
    /// Claims a key with no modifiers, for as long as the caller keeps it. Answers
    /// whether the claim succeeded rather than throwing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists for the stretches when macshot is driving the desktop rather than
    /// watching it — a scroll capture turning someone else's wheel — where the key
    /// that stops it has to arrive even though the foreground belongs to the window
    /// being captured. Taking a bare key process-wide is only defensible because it
    /// is given straight back, so <see cref="Unregister"/> is not optional.
    /// </para>
    /// <para>
    /// A refusal is survivable and so is not thrown on: another app already owning
    /// the key costs the shortcut, not the capture, which still ends on its own.
    /// </para>
    /// </remarks>
    public bool TryRegisterBareKey(int id, uint virtualKey, Action handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        ArgumentNullException.ThrowIfNull(handler);

        if (_handlers.ContainsKey(id) || !RegisterHotKey(_window.Handle, id, ModNoRepeat, virtualKey))
        {
            return false;
        }

        _handlers.Add(id, handler);
        return true;
    }

    /// <summary>Gives a hotkey back, doing nothing when it was never held.</summary>
    public void Unregister(int id)
    {
        if (_disposed || !_handlers.Remove(id))
        {
            return;
        }

        UnregisterHotKey(_window.Handle, id);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.MessageReceived -= OnMessageReceived;
        foreach (var id in _handlers.Keys)
        {
            UnregisterHotKey(_window.Handle, id);
        }

        _handlers.Clear();
        GC.SuppressFinalize(this);
    }

    private void OnMessageReceived(object? sender, WindowMessageEventArgs args)
    {
        if (args.Message != WmHotkey || !_handlers.TryGetValue(args.WParam.ToInt32(), out var handler))
        {
            return;
        }

        args.Handled = true;
        handler();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
