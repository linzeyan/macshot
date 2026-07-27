using System.Runtime.InteropServices;
using Macshot.Windows.Core.Capture;
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
    private const int CommandQuit = 3;

    private const int HotkeyCaptureArea = 1;
    private const int HotkeyCaptureAllScreens = 2;

    private const uint MessageBoxIconError = 0x00000010;

    private readonly NativeScreenCaptureService _screenCapture = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly MessageWindow _messageWindow;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly TrayIconService _trayIcon;
    private readonly List<CaptureOverlayWindow> _overlays = [];
    private CapturedFrame? _pendingDesktopFrame;
    private MainWindow? _preview;
    private bool _disposed;

    public CaptureController()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The capture controller must be created on the UI thread.");

        _messageWindow = new MessageWindow();

        _hotkeys = new GlobalHotkeyService(_messageWindow);
        _hotkeys.RegisterControlShift(HotkeyCaptureArea, 'X', () => Post(BeginAreaCaptureAsync));
        _hotkeys.RegisterControlShift(HotkeyCaptureAllScreens, 'F', () => Post(CaptureAllScreensAndSaveAsync));

        _trayIcon = new TrayIconService(_messageWindow, "macshot");
        _trayIcon.AddMenuItem(CommandCaptureArea, "Capture area\tCtrl+Shift+X");
        _trayIcon.AddMenuItem(CommandCaptureAllScreens, "Capture all screens");
        _trayIcon.AddSeparator();
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
        _pendingDesktopFrame = desktopFrame;

        foreach (var monitor in layout.Monitors)
        {
            var overlay = new CaptureOverlayWindow(desktopFrame, layout, monitor);
            overlay.SelectionCompleted += OnSelectionCompleted;
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
        return ShowPreviewAsync(_screenCapture.CaptureVirtualDesktop(), selection: null);
    }

    /// <summary>Captures everything and writes it out without showing any UI.</summary>
    public async Task CaptureAllScreensAndSaveAsync()
    {
        var frame = _screenCapture.CaptureVirtualDesktop();
        await NativeScreenCaptureService.SavePngAsync(frame, selection: null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DismissOverlays();
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

    private async void OnSelectionCompleted(object? sender, CaptureRegion selection)
    {
        var desktopFrame = _pendingDesktopFrame;
        DismissOverlays();
        if (desktopFrame is null)
        {
            return;
        }

        try
        {
            // The selection is already in frame space for the whole virtual desktop,
            // so it indexes the captured frame directly whichever display it came from.
            await ShowPreviewAsync(desktopFrame, selection);
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    private void OnCaptureCancelled(object? sender, EventArgs args) => DismissOverlays();

    private void DismissOverlays()
    {
        var overlays = _overlays.ToArray();
        _overlays.Clear();
        _pendingDesktopFrame = null;

        foreach (var overlay in overlays)
        {
            overlay.SelectionCompleted -= OnSelectionCompleted;
            overlay.Cancelled -= OnCaptureCancelled;
            overlay.Close();
        }
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
