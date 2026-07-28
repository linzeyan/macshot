using System.Runtime.InteropServices;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Output;
using Macshot.Windows.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Macshot.Windows;

/// <summary>
/// Owns macshot's background lifetime: the notification-area icon, the global
/// hotkeys, the capture overlays, and the preview window.
/// </summary>
/// <remarks>
/// This orchestration used to live in <see cref="MainWindow"/>, which made the
/// app a normal windowed program: closing the window took the hotkeys with it.
/// macshot is a background tool, so the controller outlives every window and no
/// window is shown at startup. It is the counterpart of the macOS
/// <c>AppDelegate</c>.
/// </remarks>
public sealed class CaptureController : IDisposable
{
    private const int CommandCaptureArea = 1;
    private const int CommandCaptureAllScreens = 2;
    private const int CommandPreferences = 3;
    private const int CommandQuit = 4;

    private const int HotkeyCaptureArea = 1;
    private const int HotkeyCaptureAllScreens = 2;

    private const uint MessageBoxIconError = 0x00000010;

    private readonly NativeScreenCaptureService _screenCapture = new();
    private readonly SettingsStore _settings = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly MessageWindow _messageWindow;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly TrayIconService _trayIcon;
    private readonly List<CaptureOverlayWindow> _overlays = [];
    private readonly List<PinWindow> _pins = [];
    private MainWindow? _preview;
    private ThumbnailWindow? _thumbnail;
    private PreferencesWindow? _preferences;
    private bool _disposed;

    public CaptureController()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The capture controller must be created on the UI thread.");

        _messageWindow = new MessageWindow();

        _hotkeys = new GlobalHotkeyService(_messageWindow);
        _hotkeys.RegisterControlShift(HotkeyCaptureArea, 'X', () => Post(BeginAreaCaptureAsync));
        _hotkeys.RegisterControlShift(HotkeyCaptureAllScreens, 'F', () => Post(CaptureAllScreensAsync));

        _trayIcon = new TrayIconService(_messageWindow, "macshot");
        _trayIcon.AddMenuItem(CommandCaptureArea, "Capture area\tCtrl+Shift+X");
        _trayIcon.AddMenuItem(CommandCaptureAllScreens, "Capture all screens\tCtrl+Shift+F");
        _trayIcon.AddSeparator();
        _trayIcon.AddMenuItem(CommandPreferences, "Preferences...");
        _trayIcon.AddMenuItem(CommandQuit, "Quit macshot");
        _trayIcon.CommandInvoked += OnTrayCommandInvoked;
        _trayIcon.DefaultActionInvoked += (_, _) => Post(BeginAreaCaptureAsync);
    }

    /// <summary>Puts one selection overlay on every display.</summary>
    public async Task BeginAreaCaptureAsync()
    {
        if (_overlays.Count > 0)
        {
            return;
        }

        // One overlay per display: a single window spanning displays with different
        // DPI cannot map pointer input to pixels. See the architecture notes, D6.
        var layout = MonitorEnumerator.Enumerate();
        var desktopFrame = _screenCapture.CaptureVirtualDesktop();

        foreach (var monitor in layout.Monitors)
        {
            var overlay = new CaptureOverlayWindow(desktopFrame, layout, monitor, _settings);
            overlay.CaptureCompleted += OnCaptureCompleted;
            overlay.SelectionCommitted += OnSelectionCommitted;
            overlay.Cancelled += OnCaptureCancelled;
            _overlays.Add(overlay);
        }

        foreach (var overlay in _overlays)
        {
            await overlay.ShowAsync();
        }
    }

    public Task CaptureAllScreensAsync()
    {
        return DeliverAsync(_screenCapture.CaptureVirtualDesktop());
    }

    /// <summary>The preferences the delivery path is currently using.</summary>
    public CaptureSettings Settings => _settings.Current;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DismissOverlays();

        _thumbnail?.Close();
        _preferences?.Close();
        foreach (var pin in _pins.ToArray())
        {
            pin.Close();
        }

        _trayIcon.Dispose();
        _hotkeys.Dispose();
        _messageWindow.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnTrayCommandInvoked(object? sender, int command)
    {
        switch (command)
        {
        case CommandCaptureArea:
            Post(BeginAreaCaptureAsync);
            break;
        case CommandCaptureAllScreens:
            Post(CaptureAllScreensAsync);
            break;
        case CommandPreferences:
            _dispatcher.TryEnqueue(ShowPreferences);
            break;
        case CommandQuit:
            _dispatcher.TryEnqueue(() =>
            {
                // Dispose before exiting so the shell drops the icon immediately;
                // otherwise a dead icon lingers until something hovers over it.
                Dispose();
                Application.Current.Exit();
            });
            break;
        }
    }

    /// <summary>
    /// One overlay has taken the capture, so the rest are closed. They are always on
    /// top, and leaving them up would cover the other displays for the whole time
    /// the user spends annotating.
    /// </summary>
    private void OnSelectionCommitted(object? sender, EventArgs args)
    {
        foreach (var overlay in _overlays.ToArray())
        {
            if (ReferenceEquals(overlay, sender))
            {
                continue;
            }

            _overlays.Remove(overlay);
            Unsubscribe(overlay);
            overlay.Close();
        }
    }

    private async void OnCaptureCompleted(object? sender, CapturedFrame result)
    {
        DismissOverlays();

        try
        {
            await DeliverAsync(result);
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    /// <summary>
    /// Hands a finished capture to whatever the preferences ask for. The overlay has
    /// already cropped the selection and burned in the annotations, so this stage
    /// only decides where the pixels go.
    /// </summary>
    private async Task DeliverAsync(CapturedFrame frame)
    {
        var settings = _settings.Current;

        if (settings.CopyToClipboard)
        {
            await ImageDelivery.CopyToClipboardAsync(frame);
        }

        if (settings.AutoSave)
        {
            await ImageDelivery.SaveAsync(frame, settings);
        }

        if (settings.ShowThumbnail)
        {
            await ShowThumbnailAsync(frame);
            return;
        }

        // With every delivery turned off the capture would otherwise vanish, which
        // is indistinguishable from macshot being broken. The preview window is the
        // fallback that keeps the pixels reachable.
        if (!settings.CopyToClipboard && !settings.AutoSave)
        {
            await ShowPreviewAsync(frame, null);
        }
    }

    private async Task ShowThumbnailAsync(CapturedFrame frame)
    {
        // Only the newest capture is offered: a stack of panels would cover the
        // corner of the screen the user is trying to work in.
        _thumbnail?.Close();

        var thumbnail = new ThumbnailWindow(frame, _settings);
        thumbnail.PinRequested += (_, pinned) => Post(() => PinAsync(pinned));
        thumbnail.EditRequested += (_, captured) => Post(() => ShowPreviewAsync(captured, null));
        thumbnail.Closed += (_, _) =>
        {
            if (ReferenceEquals(_thumbnail, thumbnail))
            {
                _thumbnail = null;
            }
        };

        _thumbnail = thumbnail;
        await thumbnail.ShowAsync();
    }

    private async Task PinAsync(CapturedFrame frame)
    {
        var pin = new PinWindow(frame, _settings);

        // Tracked so quitting takes the always-on-top windows with it instead of
        // leaving them stranded over everything else.
        pin.Closed += (_, _) => _pins.Remove(pin);
        _pins.Add(pin);
        await pin.ShowPinnedAsync();
    }

    private void ShowPreferences()
    {
        if (_preferences is null)
        {
            var preferences = new PreferencesWindow(_settings);
            preferences.Closed += (_, _) => _preferences = null;
            _preferences = preferences;
        }

        _preferences.Activate();
    }

    private void OnCaptureCancelled(object? sender, EventArgs args) => DismissOverlays();

    private void DismissOverlays()
    {
        var overlays = _overlays.ToArray();
        _overlays.Clear();

        foreach (var overlay in overlays)
        {
            Unsubscribe(overlay);
            overlay.Close();
        }
    }

    private void Unsubscribe(CaptureOverlayWindow overlay)
    {
        overlay.CaptureCompleted -= OnCaptureCompleted;
        overlay.SelectionCommitted -= OnSelectionCommitted;
        overlay.Cancelled -= OnCaptureCancelled;
    }

    private async Task ShowPreviewAsync(CapturedFrame frame, CaptureRegion? selection)
    {
        if (_preview is null)
        {
            var preview = new MainWindow(this);
            preview.Closed += (_, _) => _preview = null;
            _preview = preview;
        }

        await _preview.PresentAsync(frame, selection);
        _preview.Activate();
    }

    private void Post(Func<Task> action)
    {
        _dispatcher.TryEnqueue(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                ReportError(exception);
            }
        });
    }

    /// <summary>
    /// Reports a failure through the shell rather than a XAML dialog, because a
    /// capture triggered by a hotkey may have no window to host one, and a
    /// swallowed exception would look like macshot simply doing nothing.
    /// </summary>
    private void ReportError(Exception exception)
    {
        MessageBox(_messageWindow.Handle, exception.Message, "macshot", MessageBoxIconError);
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
