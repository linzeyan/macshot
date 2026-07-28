using System.Runtime.InteropServices;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
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
    private const int CommandRecordScreen = 5;

    private const int HotkeyCaptureArea = 1;
    private const int HotkeyCaptureAllScreens = 2;

    /// <summary>
    /// Held only while a scroll capture runs. Escape is taken bare, and process-wide,
    /// because the foreground during a scroll capture belongs to the window being
    /// captured rather than to macshot.
    /// </summary>
    private const int HotkeyStopScrollCapture = 3;

    private const int HotkeyRecordScreen = 4;

    /// <summary>
    /// Held only while a recording runs, and bare for the same reason as the scroll
    /// capture's: whatever is being demonstrated has the foreground, not macshot.
    /// </summary>
    private const int HotkeyStopRecording = 5;

    private const uint VirtualKeyEscape = 0x1B;

    private const uint MessageBoxIconError = 0x00000010;

    private readonly ScreenCaptureService _screenCapture = new();
    private readonly ScreenRecorder _recorder = new();
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

    /// <summary>
    /// Held for the length of a recording, and the only sign one is running: asking
    /// to record while this is set stops the recording instead of starting a second.
    /// </summary>
    private CancellationTokenSource? _recording;

    private bool _reportedCaptureFallback;
    private bool _disposed;

    public CaptureController()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The capture controller must be created on the UI thread.");

        _messageWindow = new MessageWindow();

        _hotkeys = new GlobalHotkeyService(_messageWindow);
        _hotkeys.RegisterControlShift(HotkeyCaptureArea, 'X', () => Post(BeginAreaCaptureAsync));
        _hotkeys.RegisterControlShift(HotkeyCaptureAllScreens, 'F', () => Post(CaptureAllScreensAsync));
        _hotkeys.RegisterControlShift(HotkeyRecordScreen, 'R', () => Post(ToggleRecordingAsync));

        _trayIcon = new TrayIconService(_messageWindow, "macshot");
        _trayIcon.AddMenuItem(CommandCaptureArea, "Capture area\tCtrl+Shift+X");
        _trayIcon.AddMenuItem(CommandCaptureAllScreens, "Capture all screens\tCtrl+Shift+F");
        _trayIcon.AddMenuItem(CommandRecordScreen, "Record screen\tCtrl+Shift+R");
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
        var displays = MonitorEnumerator.Enumerate();
        var layout = displays.Layout;
        var desktopFrame = await CaptureDesktopAsync(displays);

        // Taken once, next to the screenshot and before any overlay exists, so the
        // windows offered for snapping are the ones in the frozen pixels the user is
        // about to look at — and so macshot's own overlays cannot be among them.
        var snapCandidates = WindowEnumerator.EnumerateFrontToBack()
            .Select(window => window with { Bounds = layout.VirtualToFrame(window.Bounds) })
            .ToArray();

        foreach (var monitor in layout.Monitors)
        {
            var overlay = new CaptureOverlayWindow(
                desktopFrame,
                layout,
                monitor,
                _settings,
                snapCandidates,
                _screenCapture.TryCaptureWindowAsync);
            overlay.CaptureCompleted += OnCaptureCompleted;
            overlay.SelectionCommitted += OnSelectionCommitted;
            overlay.Cancelled += OnCaptureCancelled;
            overlay.ScrollCaptureRequested += OnScrollCaptureRequested;
            _overlays.Add(overlay);
        }

        foreach (var overlay in _overlays)
        {
            await overlay.ShowAsync();
        }
    }

    public async Task CaptureAllScreensAsync()
    {
        await DeliverAsync(await CaptureDesktopAsync(MonitorEnumerator.Enumerate()));
    }

    /// <summary>
    /// Takes the desktop capture and, once per session, says so when the preferred
    /// backend was available but failed.
    /// </summary>
    /// <remarks>
    /// Falling back still produces the screenshot, so interrupting every capture with
    /// the same message would be worse than the fault. Saying it once is what stops
    /// the app from silently running on the older backend forever. A build of Windows
    /// that simply does not offer the API is not a fault and is not reported.
    /// </remarks>
    private async Task<CapturedFrame> CaptureDesktopAsync(DisplaySet displays)
    {
        var frame = await _screenCapture.CaptureVirtualDesktopAsync(displays);
        if (_screenCapture.FellBackUnexpectedly && !_reportedCaptureFallback)
        {
            _reportedCaptureFallback = true;
            MessageBox(
                _messageWindow.Handle,
                $"Screen capture fell back to the older backend: {_screenCapture.FallbackReason}",
                "macshot",
                MessageBoxIconError);
        }

        return frame;
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

        // Stopped rather than abandoned: a recording in flight owns a file, and
        // quitting out from under the encoder would leave it unplayable.
        _recording?.Cancel();

        _thumbnail?.Close();
        _preferences?.Close();
        foreach (var pin in _pins.ToArray())
        {
            pin.Close();
        }

        _screenCapture.Dispose();
        _recorder.Dispose();
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
        case CommandRecordScreen:
            Post(ToggleRecordingAsync);
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
        overlay.ScrollCaptureRequested -= OnScrollCaptureRequested;
    }

    private void OnScrollCaptureRequested(object? sender, CaptureWindow window) =>
        Post(() => ScrollCaptureAsync(window));

    /// <summary>
    /// Runs a scroll capture of one window and delivers the tall image it produces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The overlays go first and the panel comes up second, and the order is not
    /// cosmetic. The wheel lands on whatever sits under the pointer, so an
    /// always-on-top overlay covering the desktop would take every notch itself;
    /// and Windows only hands the foreground to a process that already has it, so
    /// activating the panel is what leaves macshot able to bring the target window
    /// forward a moment later.
    /// </para>
    /// <para>
    /// Stopping early is not a failure. Whatever was scrolled through is delivered
    /// the same way a whole page would be.
    /// </para>
    /// </remarks>
    private async Task ScrollCaptureAsync(CaptureWindow window)
    {
        DismissOverlays();

        using var cancellation = new CancellationTokenSource();
        var hud = new ScrollCaptureHudWindow();
        hud.StopRequested += (_, _) => cancellation.Cancel();
        hud.ShowHud();

        var holdsEscape = _hotkeys.TryRegisterBareKey(
            HotkeyStopScrollCapture,
            VirtualKeyEscape,
            cancellation.Cancel);

        var session = new ScrollCaptureSession(_screenCapture.TryCaptureWindowAsync);
        session.Progressed += (_, progress) => hud.Report(progress.Frames, progress.Rows);

        ScrollCaptureResult result;
        try
        {
            result = await session.RunAsync(window, cancellation.Token);
        }
        finally
        {
            if (holdsEscape)
            {
                _hotkeys.Unregister(HotkeyStopScrollCapture);
            }

            // Before delivery, so the panel is not still claiming to be scrolling
            // while the thumbnail for the finished capture appears next to it.
            hud.Close();
        }

        if (result.Stop == ScrollCaptureStop.HeightLimit)
        {
            MessageBox(
                _messageWindow.Handle,
                "That page was longer than macshot will capture in one go, so the bottom of it is missing.",
                "macshot",
                MessageBoxIconError);
        }

        await DeliverAsync(result.Frame);
    }

    /// <summary>
    /// Starts recording the display the pointer is on, or stops the recording that is
    /// already running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One shortcut both ways. A recording is started to demonstrate something, which
    /// means the screen fills with whatever is being demonstrated; the panel's Stop
    /// button cannot be the only way out when a full-screen app is over it.
    /// </para>
    /// <para>
    /// The display is taken from the pointer rather than asked for. Putting a
    /// selection overlay up first would record the moment of choosing which display
    /// to record, and macshot already knows which one is being worked on.
    /// </para>
    /// </remarks>
    public async Task ToggleRecordingAsync()
    {
        if (_recording is { } running)
        {
            running.Cancel();
            return;
        }

        if (!ScreenRecorder.IsSupported)
        {
            throw new InvalidOperationException(
                "This build of Windows does not offer the screen recording API.");
        }

        var displays = MonitorEnumerator.Enumerate();
        var monitor = displays.Layout.MonitorAt(PointerPosition()) ?? displays.Layout.Primary;
        if (!displays.Handles.TryGetValue(monitor.DeviceName, out var handle))
        {
            throw new InvalidOperationException($"No display handle for '{monitor.DeviceName}'.");
        }

        using var cancellation = new CancellationTokenSource();
        var hud = new RecordingHudWindow();
        hud.StopRequested += (_, _) => cancellation.Cancel();
        hud.ShowHud();

        var holdsEscape = _hotkeys.TryRegisterBareKey(
            HotkeyStopRecording,
            VirtualKeyEscape,
            cancellation.Cancel);

        _recording = cancellation;

        var format = _settings.Current.RecordingFormat;

        try
        {
            var result = await _recorder.RecordDisplayAsync(
                handle,
                ResolveRecordingPath(format),
                format,
                cancellation.Token);

            // The panel outlives the recording by a few seconds to say where the file
            // went. Video has no thumbnail and no clipboard to land in, so this is
            // the only thing that says the recording exists.
            hud.ShowSaved(Path.GetFileName(result.Path));
        }
        catch (Exception)
        {
            // A failure has nothing to report there; the message box reports it.
            hud.Close();
            throw;
        }
        finally
        {
            _recording = null;
            if (holdsEscape)
            {
                _hotkeys.Unregister(HotkeyStopRecording);
            }
        }
    }

    /// <summary>
    /// Where the next recording is written.
    /// </summary>
    /// <remarks>
    /// A recording is always saved, whatever <see cref="CaptureSettings.AutoSave"/>
    /// says, because there is nowhere else for it to go: minutes of video do not
    /// belong on the clipboard and there is no editor to hand it to yet.
    /// </remarks>
    private string ResolveRecordingPath(RecordingFormat format)
    {
        var settings = _settings.Current;
        var directory = ImageDelivery.ResolveDirectory(settings);
        Directory.CreateDirectory(directory);

        var name = FilenameTemplate.ResolveUnique(
            settings.FilenameTemplate,
            DateTimeOffset.Now,
            format.FileExtension(),
            candidate => File.Exists(Path.Combine(directory, candidate)));

        return Path.Combine(directory, name);
    }

    /// <summary>The pointer, in virtual-desktop pixels.</summary>
    private static CapturePoint PointerPosition()
    {
        // A pointer Windows will not report is treated as the origin, which resolves
        // to the primary display: recording the wrong display is a better failure
        // than refusing to record.
        return GetCursorPos(out var point)
            ? new CapturePoint(point.X, point.Y)
            : new CapturePoint(0, 0);
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorLocation point);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorLocation
    {
        public int X;
        public int Y;
    }
}
