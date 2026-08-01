using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Core.Input;
using Macshot.Windows.Core.Output;
using Macshot.Windows.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using static Macshot.Windows.Services.Localization;

namespace Macshot.Windows;

/// <summary>
/// Owns macshot's background lifetime: the notification-area icon, the global
/// hotkeys, the capture overlays, and the preview window.
/// </summary>
/// <remarks>
/// This orchestration used to live in a preview window, which made the app a normal
/// windowed program: closing the window took the hotkeys with it.
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
    private const int CommandHistory = 7;
    private const int CommandRecordArea = 8;

    /// <summary>
    /// The capture-delay submenu. One command per choice rather than one command
    /// carrying a number, because a tray menu reports nothing but an id.
    /// </summary>
    private const int CommandDelayFirst = 50;

    /// <summary>
    /// Where the recent-capture entries start. They are numbered at the moment the
    /// menu is opened, so they need a range of their own that the fixed commands
    /// above can never grow into.
    /// </summary>
    /// <summary>
    /// How many floating panels may stand at once. Three is enough for the "capture,
    /// capture, capture, now deal with them" pass the tool invites, and few enough that
    /// the column stays in the corner it belongs in.
    /// </summary>
    private const int MaxThumbnails = 3;

    private const int CommandRecentFirst = 100;

    /// <summary>
    /// How many past captures the menu offers, whatever the history is allowed to
    /// keep. A menu is for reaching the one just missed, not for browsing an archive.
    /// </summary>
    private const int RecentMenuCount = 10;

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

    /// <summary>
    /// Held only while a delayed capture counts down. Bare, because the countdown
    /// panel deliberately never takes the foreground, so a key sent to macshot's own
    /// window would never arrive.
    /// </summary>
    private const int HotkeyCancelCountdown = 6;

    private const uint VirtualKeyEscape = 0x1B;

    private readonly ScreenCaptureService _screenCapture = new();
    private readonly ScreenRecorder _recorder = new();
    private readonly SettingsStore _settings = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly MessageWindow _messageWindow;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly TrayIconService _trayIcon;
    private readonly List<CaptureOverlayWindow> _overlays = [];
    private readonly List<PinWindow> _pins = [];
    private EditorWindow? _editor;
    private HistoryWindow? _history;
    /// <summary>The floating panels on show, oldest first.</summary>
    private readonly List<ThumbnailWindow> _thumbnails = [];
    private PreferencesWindow? _preferences;

    /// <summary>
    /// Held for the length of a countdown, and the only sign one is running: asking
    /// for a second delayed capture while this is set does nothing rather than
    /// stacking two counters on top of each other.
    /// </summary>
    private CountdownWindow? _countdown;

    /// <summary>
    /// The editor waiting for the next capture to be added to it, rather than delivered
    /// the usual way. Cleared as soon as that capture arrives or is given up on.
    /// </summary>
    private EditorWindow? _addingTo;

    /// <summary>
    /// The captures the menu was last built from. Kept because the menu hands back a
    /// number and nothing else, and the file that number meant has to survive until
    /// the click arrives.
    /// </summary>
    private IReadOnlyList<HistoryEntry> _recent = [];

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

        // Before anything else that could be worth tracing, and re-read on every save
        // so turning it on does not need a restart — the fault being chased is often
        // one the user has just reproduced and does not want to lose.
        DiagnosticLog.IsVerbose = _settings.Current.VerboseLogging;
        _settings.Changed += (_, settings) => DiagnosticLog.IsVerbose = settings.VerboseLogging;

        // Before the first string is asked for — the tray menu below is built from
        // literals that go through the lookup. Re-resolved on every save so choosing a
        // language reaches the next window opened, which is where this falls short of
        // macshot: it swaps its bundle and redraws, where a XAML page has already been
        // built by the time the setting changes.
        Localization.Use(_settings.Current.Language);
        _settings.Changed += (_, settings) => Localization.Use(settings.Language);

        DiagnosticLog.Verbose(
            $"macshot starting: {BuildVariant.DisplayName}, settings at {_settings.Path}");

        _messageWindow = new MessageWindow();

        _hotkeys = new GlobalHotkeyService(_messageWindow);

        // The variant's own name, so someone running both can tell which icon is which
        // from the tooltip alone.
        // Read once, as macshot reads hideMenuBarIcon once: an icon that came and went
        // as the checkbox was clicked would leave the tray reordering itself under the
        // user's pointer.
        _trayIcon = new TrayIconService(
            _messageWindow,
            BuildVariant.DisplayName,
            !_settings.Current.HideTrayIcon);

        // macshot's own menu, item for item and in its order — AppDelegate.swift:707–805.
        // The strings are macshot's source strings rather than paraphrases of them, which
        // is what makes them resolve: the translations are keyed on the English macshot
        // ships, so "Capture area" found nothing where "Capture Area" finds every
        // language the Mac app has.
        _trayIcon.AddMenuItem(CommandCaptureArea, L("Capture Area"));
        _trayIcon.AddMenuItem(CommandCaptureAllScreens, L("Capture Screen"));
        _trayIcon.AddSubmenu(L("Capture Delay"), DelayMenuEntries, L("None"));
        _trayIcon.AddSeparator();
        _trayIcon.AddMenuItem(CommandRecordArea, L("Record Area"));
        _trayIcon.AddMenuItem(CommandRecordScreen, L("Record Screen"));
        _trayIcon.AddSeparator();
        _trayIcon.AddSubmenu(L("Recent Captures"), RecentMenuEntries, L("No recent captures"));
        _trayIcon.AddMenuItem(CommandHistory, L("Show History Panel"));
        _trayIcon.AddSeparator();
        _trayIcon.AddMenuItem(CommandPreferences, L("Settings..."));
        _trayIcon.AddMenuItem(CommandQuit, L("Quit macshot"));
        _trayIcon.CommandInvoked += OnTrayCommandInvoked;
        _trayIcon.DefaultActionInvoked += (_, _) => Post(BeginAreaCaptureHonouringDelayAsync);

        // After the menu exists, because applying a shortcut also writes it into the
        // menu entry that names it.
        ApplyHotkeys(_settings.Current);

        // Re-applied rather than read once, so a shortcut changed in preferences takes
        // effect without restarting macshot — which, for a background app with no
        // window, is something the user would have to be told how to do.
        _settings.Changed += (_, settings) => ApplyHotkeys(settings);
    }

    /// <summary>
    /// Claims the three configured shortcuts, and says so once when Windows refuses
    /// any of them.
    /// </summary>
    /// <remarks>
    /// The refusals are collected and reported together. Told one at a time they would
    /// be three message boxes in a row for a user who has just typed one shortcut that
    /// another program owns, and the second and third would be about shortcuts they
    /// did not touch.
    /// </remarks>
    private void ApplyHotkeys(CaptureSettings settings)
    {
        var refused = new List<HotkeyBinding>(3);

        // Through L, and through macshot's own strings. These rewrite the menu entries
        // to append the shortcut, so an English literal here silently replaced whatever
        // the constructor had translated — which is why the menu came up half in one
        // language and half in the other.
        Bind(
            HotkeyCaptureArea,
            CommandCaptureArea,
            L("Capture Area"),
            settings.CaptureAreaBinding,
            BeginAreaCaptureHonouringDelayAsync);
        Bind(
            HotkeyCaptureAllScreens,
            CommandCaptureAllScreens,
            L("Capture Screen"),
            settings.CaptureAllScreensBinding,
            CaptureAllScreensAsync);
        Bind(
            HotkeyRecordScreen,
            CommandRecordScreen,
            L("Record Screen"),
            settings.RecordScreenBinding,
            ToggleRecordingAsync);

        if (refused.Count > 0)
        {
            FailureReport.Notice(
                _messageWindow.Handle,
                "Windows would not give macshot these shortcuts, so they are not active: "
                    + string.Join(", ", refused)
                    + ". Another program may already own them. The notification-area menu still works.");
        }

        void Bind(int hotkey, int command, string label, HotkeyBinding binding, Func<Task> action)
        {
            // Given back first: re-registering an id Windows still holds fails, which
            // would turn every preferences save into a lost shortcut.
            _hotkeys.Unregister(hotkey);

            if (_hotkeys.TryRegister(hotkey, binding, () => Post(action)))
            {
                _trayIcon.SetMenuItemText(command, $"{label}\t{binding}");
                DiagnosticLog.Verbose($"hotkey {binding} registered for {label}");
            }
            else
            {
                // Named without a shortcut rather than with one that does nothing.
                _trayIcon.SetMenuItemText(command, label);
                refused.Add(binding);
                DiagnosticLog.Verbose($"hotkey {binding} refused for {label}");
            }
        }
    }

    /// <summary>
    /// Takes an area, waiting first when a capture delay is set.
    /// </summary>
    /// <remarks>
    /// macshot has no separate "capture after a delay" command: the delay is a setting,
    /// chosen from the menu's own submenu, and every area capture honours it. A menu with
    /// both a delay setting and a delayed-capture command would let the two disagree.
    /// </remarks>
    public Task BeginAreaCaptureHonouringDelayAsync() =>
        _settings.Current.DelaySeconds > 0
            ? BeginDelayedAreaCaptureAsync()
            : BeginAreaCaptureAsync();

    /// <summary>
    /// Opens the selection overlay armed to record, so the drag that would have taken a
    /// picture starts a recording of that rectangle instead.
    /// </summary>
    public Task BeginAreaRecordingAsync() =>
        _recording is null ? BeginAreaCaptureAsync(armRecording: true) : Task.CompletedTask;

    /// <summary>Puts one selection overlay on every display.</summary>
    /// <param name="armRecording">
    /// Whether confirming the selection records it rather than capturing it —
    /// macshot's <c>pendingRecordAreaMode</c>.
    /// </param>
    public async Task BeginAreaCaptureAsync(bool armRecording = false)
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

        // The facts a misplaced overlay is diagnosed from. Which display, at what scale,
        // in what part of the virtual desktop — all three have to be right for the
        // pointer to land where the user is pointing, and none is visible from a
        // screenshot of the result.
        foreach (var monitor in layout.Monitors)
        {
            DiagnosticLog.Verbose(
                $"overlay for {monitor.DeviceName} at {monitor.Bounds.X},{monitor.Bounds.Y} "
                    + $"{monitor.Bounds.Width}x{monitor.Bounds.Height} scale {monitor.Scale}"
                    + (monitor.IsPrimary ? " (primary)" : string.Empty));
        }

        DiagnosticLog.Verbose($"{snapCandidates.Length} window(s) offered for snapping");

        foreach (var monitor in layout.Monitors)
        {
            var overlay = new CaptureOverlayWindow(
                desktopFrame,
                layout,
                monitor,
                _settings,
                snapCandidates,
                _screenCapture.TryCaptureWindowAsync)
            {
                ArmRecording = armRecording,
            };
            overlay.CaptureCompleted += OnCaptureCompleted;
            overlay.SelectionCommitted += OnSelectionCommitted;
            overlay.Cancelled += OnCaptureCancelled;
            overlay.ScrollCaptureRequested += OnScrollCaptureRequested;
            overlay.RecordingRequested += OnRecordingRequested;
            overlay.EditorRequested += OnEditorRequested;
            overlay.WindowSnapToggled += OnWindowSnapToggled;
            _overlays.Add(overlay);
        }

        try
        {
            foreach (var overlay in _overlays)
            {
                await overlay.ShowAsync();
            }
        }
        catch
        {
            // An overlay that failed on the way up is still in the list, and a non-empty
            // list is what turns the next hotkey into a no-op. Without this, one failed
            // capture stops every capture for the rest of the session, and the app looks
            // like it has stopped responding to its own shortcut.
            DismissOverlays();
            throw;
        }
    }

    /// <summary>
    /// Counts down, then puts the selection overlays up on what the screen has
    /// become by then.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the only way to capture the things that end when another window is
    /// clicked: an open menu, a hover state, a tooltip. Reaching for macshot closes
    /// them, so macshot has to be told first and arrive later.
    /// </para>
    /// <para>
    /// It is a separate action rather than a preference on every capture. A wait in
    /// front of the shortcut would cost the ordinary case far more than it buys the
    /// rare one, so the shortcut stays immediate and the preference only says how
    /// long this waits.
    /// </para>
    /// </remarks>
    public async Task BeginDelayedAreaCaptureAsync()
    {
        if (_countdown is not null || _overlays.Count > 0)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        var countdown = new CountdownWindow();
        _countdown = countdown;

        var holdsEscape = _hotkeys.TryRegisterBareKey(
            HotkeyCancelCountdown,
            VirtualKeyEscape,
            cancellation.Cancel);

        bool elapsed;
        try
        {
            var seconds = _settings.Current.DelaySeconds;
            DiagnosticLog.Verbose($"countdown starting: {seconds}s, escape held: {holdsEscape}");
            elapsed = await countdown.RunAsync(seconds, cancellation.Token);
            DiagnosticLog.Verbose(elapsed ? "countdown elapsed" : "countdown cancelled");
        }
        finally
        {
            _countdown = null;
            if (holdsEscape)
            {
                _hotkeys.Unregister(HotkeyCancelCountdown);
            }
        }

        // A cancelled countdown takes no capture at all. Showing the overlays anyway
        // would make Escape mean "wait less" rather than "stop", and the user who
        // pressed it would then have to press it again.
        if (elapsed)
        {
            await BeginAreaCaptureAsync();
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

        // Which backend actually ran is otherwise only knowable from the absence of a
        // message box, which is the weakest kind of evidence there is.
        DiagnosticLog.Verbose(
            $"desktop captured {frame.Width}x{frame.Height} at {frame.VirtualX},{frame.VirtualY} "
                + $"via {_screenCapture.Backend}");

        if (_screenCapture.FellBackUnexpectedly && !_reportedCaptureFallback)
        {
            _reportedCaptureFallback = true;
            FailureReport.Notice(
                _messageWindow.Handle,
                $"Screen capture fell back to the older backend: {_screenCapture.FallbackReason}");
        }

        return frame;
    }

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

        // Closing the countdown completes the wait as a cancellation, so the delayed
        // capture it belongs to does not go on to raise overlays over a quitting app.
        _countdown?.Close();

        foreach (var thumbnail in _thumbnails.ToArray())
        {
            thumbnail.Close();
        }

        _editor?.Close();
        _history?.Close();
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
            Post(BeginAreaCaptureHonouringDelayAsync);
            break;
        case CommandCaptureAllScreens:
            Post(CaptureAllScreensAsync);
            break;
        case CommandRecordScreen:
            Post(ToggleRecordingAsync);
            break;
        case CommandRecordArea:
            Post(BeginAreaRecordingAsync);
            break;
        case >= CommandDelayFirst and < CommandDelayFirst + 16:
            SetCaptureDelay(DelayChoices[command - CommandDelayFirst]);
            break;
        case CommandHistory:
            Post(ShowHistoryAsync);
            return;
        case CommandPreferences:
            _dispatcher.TryEnqueue(ShowPreferences);
            break;
        case >= CommandRecentFirst:
            OpenRecent(command - CommandRecentFirst);
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

    private async void OnCaptureCompleted(object? sender, CaptureCompletion result)
    {
        // Inside the try with the delivery: this runs from the overlay's own input
        // handler, closing the very window whose event is still on the stack, and an
        // exception escaping an async void method has nobody above it to catch it.
        try
        {
            // Read before the overlays go, because dismissing them is what puts a
            // waiting editor back on screen and forgets it.
            var pending = _addingTo;
            DismissOverlays();

            // An editor that asked for this one takes it instead: it is a piece of a
            // picture being assembled, not a capture to copy, save and archive.
            if (pending is not null)
            {
                pending.AddCapture(result.Frame);
                return;
            }

            await DeliverAsync(result);
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    /// <summary>
    /// Hands a finished capture to the one place the user named on the toolbar, rather
    /// than to everything the preferences ask for.
    /// </summary>
    /// <remarks>
    /// History is written whichever button was pressed, the same as it is for an ordinary
    /// delivery: it is the net under every capture, not one of the destinations.
    /// </remarks>
    private async Task DeliverAsync(CaptureCompletion completion)
    {
        var frame = completion.Frame;
        if (completion.Outcome is CaptureOutcome.Deliver)
        {
            await DeliverAsync(frame, completion.Editable, completion.WindowTitle);
            return;
        }

        DiagnosticLog.Verbose(
            $"delivering {frame.Width}x{frame.Height} to {completion.Outcome}, asked for on the toolbar");

        var settings = _settings.Current;
        switch (completion.Outcome)
        {
            case CaptureOutcome.Copy:
                await ImageDelivery.CopyToClipboardAsync(frame);
                break;

            case CaptureOutcome.Save:
                await ImageDelivery.SaveAsync(frame, settings, completion.WindowTitle);
                break;

            default:
                break;
        }

        _ = await ScreenshotHistory.RecordAsync(frame, settings, completion.Editable);

        if (completion.Outcome is CaptureOutcome.Pin)
        {
            await PinAsync(frame);
        }
    }

    /// <summary>
    /// Hands a finished capture to whatever the preferences ask for. The overlay has
    /// already cropped the selection and burned in the annotations, so this stage
    /// only decides where the pixels go.
    /// </summary>
    private async Task DeliverAsync(
        CapturedFrame frame,
        EditableCapture? editable = null,
        string? windowTitle = null)
    {
        var settings = _settings.Current;

        // A capture that appears to vanish is the hardest failure to place: it could be
        // the crop, the encoder, the clipboard, or a preference nobody remembers
        // setting. This line says which of those was even attempted.
        DiagnosticLog.Verbose(
            $"delivering {frame.Width}x{frame.Height} as {settings.Format}: "
                + $"clipboard {settings.CopyToClipboard}, save {settings.AutoSave}, "
                + $"thumbnail {settings.ShowThumbnail}, history {settings.HistorySize}");

        if (settings.CopyToClipboard)
        {
            await ImageDelivery.CopyToClipboardAsync(frame);
        }

        if (settings.AutoSave)
        {
            await ImageDelivery.SaveAsync(frame, settings, windowTitle);
        }

        // After the actions the user asked for, so the extra encode is never in front
        // of the clipboard. History is the safety net under delivery, not part of it,
        // and it is written whether or not the capture was saved anywhere else.
        var archived = await ScreenshotHistory.RecordAsync(frame, settings, editable);

        if (settings.ShowThumbnail)
        {
            // The archive copy travels with the panel, so its Delete can take this
            // capture back out of the history rather than only closing the panel.
            await ShowThumbnailAsync(frame, archived);
            return;
        }

        // With every delivery turned off the capture would otherwise vanish, which is
        // indistinguishable from macshot being broken. The editor is the fallback that
        // keeps the pixels reachable, and it can copy and save them.
        if (!settings.CopyToClipboard && !settings.AutoSave)
        {
            await ShowEditorAsync(frame);
        }
    }

    /// <summary>
    /// Offers a capture in a floating panel, stacked above whatever is already there.
    /// </summary>
    /// <remarks>
    /// Stacked rather than replaced, because taking three captures in a row is how the
    /// tool is used, and a panel that vanishes as the next capture is taken takes its
    /// copy of those pixels with it. Capped at <see cref="MaxThumbnails"/>: the point of
    /// the panel is that a capture does not interrupt the work, and a column of them
    /// climbing the screen would.
    /// </remarks>
    private async Task ShowThumbnailAsync(CapturedFrame frame, string? archived = null)
    {
        // The oldest go, so the one just taken is always the one on show. Counted up
        // front rather than looped until short enough: how promptly a closed window is
        // taken off the list is WinUI's business, not a condition to spin on.
        foreach (var oldest in _thumbnails.Take(_thumbnails.Count - MaxThumbnails + 1).ToArray())
        {
            oldest.Close();
        }

        var thumbnail = new ThumbnailWindow(frame, _settings, archived);
        thumbnail.PinRequested += (_, pinned) => Post(() => PinAsync(pinned));
        thumbnail.EditRequested += (_, captured) => Post(() => ShowEditorAsync(captured));
        thumbnail.CloseAllRequested += (_, _) => CloseThumbnails();
        thumbnail.Closed += (_, _) =>
        {
            _thumbnails.Remove(thumbnail);
            Restack();
        };

        _thumbnails.Add(thumbnail);
        await thumbnail.ShowAsync(_thumbnails.Count - 1);
    }

    /// <summary>
    /// Dismisses the whole column at once, which is what someone who took six captures
    /// in a row and is done with all of them wants.
    /// </summary>
    /// <remarks>
    /// Copied first: closing a panel takes it out of the list from inside this loop.
    /// </remarks>
    private void CloseThumbnails()
    {
        foreach (var panel in _thumbnails.ToArray())
        {
            panel.Close();
        }
    }

    /// <summary>
    /// Closes the gap a dismissed panel leaves, so the column stays against the corner
    /// instead of floating in the middle of the screen with a hole in it.
    /// </summary>
    private void Restack()
    {
        for (var index = 0; index < _thumbnails.Count; index++)
        {
            _thumbnails[index].Restack(index);
        }
    }

    private async Task PinAsync(CapturedFrame frame)
    {
        var pin = new PinWindow(frame, _settings);
        pin.EditRequested += (_, captured) => Post(() => ShowEditorAsync(captured));

        // Tracked so quitting takes the always-on-top windows with it instead of
        // leaving them stranded over everything else.
        pin.Closed += (_, _) => _pins.Remove(pin);
        _pins.Add(pin);
        await pin.ShowPinnedAsync();
    }

    /// <summary>
    /// The recent captures, numbered for the menu that is about to be drawn.
    /// </summary>
    /// <summary>
    /// The delays macshot offers — <c>AppDelegate.swift:723</c>. Zero is "None"; the
    /// rest are read as seconds.
    /// </summary>
    private static readonly int[] DelayChoices = [0, 3, 5, 10, 30];

    /// <summary>
    /// The capture-delay submenu, rebuilt each time the menu opens so the tick follows
    /// a delay changed in preferences rather than the one that was set at startup.
    /// </summary>
    private IReadOnlyList<TrayMenuEntry> DelayMenuEntries()
    {
        var current = _settings.Current.DelaySeconds;

        return
        [
            .. DelayChoices.Select((seconds, index) => new TrayMenuEntry(
                CommandDelayFirst + index,
                seconds == 0
                    ? L("None")

                    // The translated string carries macshot's printf placeholder, which
                    // no .NET formatter reads — so the number goes in by hand rather than
                    // through string.Format, which would leave "%d seconds" on screen.
                    : L("%d seconds").Replace(
                        "%d",
                        seconds.ToString(CultureInfo.CurrentCulture),
                        StringComparison.Ordinal),
                seconds == current)),
        ];
    }

    private void SetCaptureDelay(int seconds) =>
        _settings.Save(_settings.Current with { DelaySeconds = seconds });

    private IReadOnlyList<TrayMenuEntry> RecentMenuEntries()
    {
        _recent = ScreenshotHistory.Recent(RecentMenuCount);
        return [.. _recent.Select((entry, index) => new TrayMenuEntry(CommandRecentFirst + index, entry.Label))];
    }

    /// <summary>
    /// Reopens a past capture in the editor, so it can be marked up further rather than
    /// only looked at.
    /// </summary>
    /// <remarks>
    /// It falls back to the shell when the file will not decode — pruned between the menu
    /// being built and the click arriving, or written by something else into macshot's
    /// folder. Whatever can open it is a better answer than a message box about a
    /// screenshot the user only wanted to see.
    /// </remarks>
    private void OpenRecent(int index)
    {
        if (index < 0 || index >= _recent.Count)
        {
            return;
        }

        var entry = _recent[index];
        Post(async () =>
        {
            try
            {
                await ReopenAsync(entry);
            }
            catch (Exception exception)
            {
                DiagnosticLog.Write($"Could not reopen '{entry.Path}': {exception.Message}");
                OpenWithShell(entry.Path);
            }
        });
    }

    /// <summary>
    /// Opens a past capture in the editor, with its marks still separate from the pixels
    /// when they were archived that way.
    /// </summary>
    /// <remarks>
    /// The unannotated copy and the marks are read rather than the finished image, which
    /// is what makes an arrow drawn last week something that can still be moved. An entry
    /// archived without them — every entry from before this existed, and every framed
    /// capture — opens as the flat image it is, so nothing that was reachable stops being
    /// reachable.
    /// </remarks>
    private async Task ReopenAsync(HistoryEntry entry)
    {
        if (entry is { IsEditable: true, RawPath: { } raw, NotesPath: { } notes })
        {
            try
            {
                var annotations = AnnotationFile.Read(await File.ReadAllTextAsync(notes));
                await ShowEditorAsync(await ImageLoader.LoadAsync(raw), annotations);
                return;
            }
            catch (Exception exception)
            {
                // Anything at all, because the answer to all of it is the same and it is
                // a good one: the finished image is still there and still worth opening.
                // Failing the whole reopen because the editable copy would not load —
                // deleted by hand, half-written, decoded by a codec that has changed —
                // would be losing the capture over the extra.
                DiagnosticLog.Write($"Could not reopen '{raw}' for editing: {exception.Message}");
            }
        }

        await ShowEditorAsync(await ImageLoader.LoadAsync(entry.Path));
    }

    private static void OpenWithShell(string path)
    {
        try
        {
            using var opened = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"Could not open the past capture '{path}': {exception.Message}");
        }
    }

    /// <summary>
    /// Opens the history panel, and opens whatever is picked there in the editor.
    /// </summary>
    /// <remarks>
    /// One at a time, like the editor: a second panel would be a second window called
    /// macshot history showing the same folder.
    /// </remarks>
    private async Task ShowHistoryAsync()
    {
        if (_history is { } existing)
        {
            existing.Activate();
            return;
        }

        var history = new HistoryWindow(_settings.Current.Theme);
        history.OpenRequested += (_, entry) => Post(() => ReopenAsync(entry));

        history.Closed += (_, _) =>
        {
            if (ReferenceEquals(_history, history))
            {
                _history = null;
            }
        };

        _history = history;
        await history.ShowAsync();
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

        // The overlays going away ends any add-capture, whichever way it ended.
        RestorePendingEditor();
    }

    private void Unsubscribe(CaptureOverlayWindow overlay)
    {
        overlay.CaptureCompleted -= OnCaptureCompleted;
        overlay.SelectionCommitted -= OnSelectionCommitted;
        overlay.Cancelled -= OnCaptureCancelled;
        overlay.ScrollCaptureRequested -= OnScrollCaptureRequested;
        overlay.RecordingRequested -= OnRecordingRequested;
        overlay.EditorRequested -= OnEditorRequested;
    }

    /// <summary>
    /// The overlay is handing its capture to the editor rather than to delivery. The
    /// overlays go first: they are always on top, so the editor would open behind them.
    /// </summary>
    private void OnEditorRequested(object? sender, CapturedFrame frame) => Post(() =>
    {
        DismissOverlays();
        return ShowEditorAsync(frame);
    });

    private void OnScrollCaptureRequested(object? sender, ScrollCaptureRequest request) =>
        Post(() => ScrollCaptureAsync(request));

    private void OnRecordingRequested(object? sender, RecordingRequest request) =>
        Post(() => RecordAsync(request));

    /// <summary>
    /// Tells the other displays' overlays that window snap has been turned on or off.
    /// </summary>
    /// <remarks>
    /// Each overlay shows the state in its own instruction pill, so one of them being
    /// told is the state being reported two different ways on a two-monitor desk. The
    /// overlay that raised it has already refreshed itself.
    /// </remarks>
    private void OnWindowSnapToggled(object? sender, EventArgs e)
    {
        foreach (var overlay in _overlays)
        {
            if (!ReferenceEquals(overlay, sender))
            {
                overlay.RefreshWindowSnapState();
            }
        }
    }

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
    private async Task ScrollCaptureAsync(ScrollCaptureRequest request)
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

        // Beside the window being captured rather than on the HUD: the panel is read
        // against the thing it is a picture of, and the HUD sits at the bottom of the
        // screen so it is not under the pointer driving the wheel.
        var preview = new ScrollCapturePreviewWindow();
        preview.ShowBeside(request.Region ?? request.Window.Bounds);

        var session = new ScrollCaptureSession(_screenCapture.TryCaptureWindowAsync);
        session.Progressed += (_, progress) => hud.Report(progress.Frames, progress.Rows);
        session.Previewed += (_, picture) => preview.ShowStitched(picture);

        ScrollCaptureResult result;
        try
        {
            result = await session.RunAsync(request.Window, request.Region, cancellation.Token);
        }
        finally
        {
            if (holdsEscape)
            {
                _hotkeys.Unregister(HotkeyStopScrollCapture);
            }

            // Before delivery, so the panels are not still claiming to be scrolling
            // while the thumbnail for the finished capture appears next to them.
            hud.Close();
            preview.Close();
        }

        DiagnosticLog.Verbose(
            $"scroll capture ended as {result.Stop}: {result.Frame.Width}x{result.Frame.Height}");

        if (result.Stop == ScrollCaptureStop.HeightLimit)
        {
            FailureReport.Notice(
                _messageWindow.Handle,
                "That page was longer than macshot will capture in one go, so the bottom of it is missing.");
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
    /// to record, and macshot already knows which one is being worked on. Someone who
    /// does want to aim it has the toolbar's Record button, which arrives at
    /// <see cref="RecordAsync"/> with the region already chosen.
    /// </para>
    /// </remarks>
    public Task ToggleRecordingAsync()
    {
        if (_recording is { } running)
        {
            running.Cancel();
            return Task.CompletedTask;
        }

        var displays = MonitorEnumerator.Enumerate();
        var monitor = displays.Layout.MonitorAt(PointerPosition()) ?? displays.Layout.Primary;
        return RecordAsync(new RecordingRequest(monitor));
    }

    /// <summary>
    /// Records one display, or one region of it, until something asks it to stop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The overlays go first. They are always on top and they cover the display being
    /// recorded, so a recording started from the toolbar would otherwise open with a
    /// dimmed screen and macshot's own chrome across it.
    /// </para>
    /// <para>
    /// The display is looked up again by name rather than taken from the request. The
    /// request may have been made against an enumeration from before the overlays went
    /// up, and a capture item is opened from a handle Windows has to still recognize.
    /// </para>
    /// </remarks>
    private async Task RecordAsync(RecordingRequest request)
    {
        if (_recording is not null)
        {
            return;
        }

        if (!ScreenRecorder.IsSupported)
        {
            throw new InvalidOperationException(
                "This build of Windows does not offer the screen recording API.");
        }

        DismissOverlays();

        var displays = MonitorEnumerator.Enumerate();
        var monitor = displays.Layout.Monitors.FirstOrDefault(
            candidate => candidate.DeviceName == request.Monitor.DeviceName)
            ?? request.Monitor;

        if (!displays.Handles.TryGetValue(monitor.DeviceName, out var handle))
        {
            throw new InvalidOperationException($"No display handle for '{monitor.DeviceName}'.");
        }

        // Virtual space out of the overlay, the display's own pixels into the recorder:
        // a capture item is one display, and it starts at its own top-left corner.
        var region = request.Region is { } aimed ? monitor.VirtualToLocal(aimed) : (CaptureRegion?)null;
        if (region is { IsEmpty: true })
        {
            throw new InvalidOperationException("That region is not on the display being recorded.");
        }

        using var cancellation = new CancellationTokenSource();
        var hud = new RecordingHudWindow();
        hud.StopRequested += (_, _) => cancellation.Cancel();
        hud.PauseToggled += (_, held) => _recorder.SetPaused(held);

        // The panel belongs to what is being recorded, so it is placed against the
        // region — or against the whole display, when that is what is being recorded.
        hud.ShowHud(request.Region ?? monitor.Bounds, monitor);

        // And a frame round the same rectangle, which is what still says where the
        // recording is once that panel has been dragged out of the way.
        RecordedRegionWindow? border = null;
        if (_settings.Current.ShowRecordedRegionBorder)
        {
            border = new RecordedRegionWindow();
            border.ShowAround(request.Region ?? monitor.Bounds, monitor.Scale);
        }

        // And a ring out of every click, which unlike the frame is meant to be in the
        // file: it is the only thing that tells a viewer a press happened at all.
        ClickHighlightOverlay? clicks = null;
        if (_settings.Current.ShowClickHighlight)
        {
            clicks = new ClickHighlightOverlay(monitor.Scale);
            clicks.Start();
        }

        var holdsEscape = _hotkeys.TryRegisterBareKey(
            HotkeyStopRecording,
            VirtualKeyEscape,
            cancellation.Cancel);

        _recording = cancellation;

        var format = _settings.Current.RecordingFormat;

        // Each format has its own rate, because they are answers to different questions:
        // how smooth the recording should be, and how large a GIF may get.
        var frameRate = format == RecordingFormat.Gif
            ? _settings.Current.GifFrameRate
            : _settings.Current.RecordingFrameRate;

        // GIF has nowhere to put sound, so it is not even opened for one — the same
        // answer macshot gives.
        var audio = format == RecordingFormat.Gif
            ? default
            : new RecordingAudio(_settings.Current.RecordSystemAudio, _settings.Current.RecordMicAudio);

        try
        {
            var path = ResolveRecordingPath(format);
            DiagnosticLog.Verbose(
                $"recording {monitor.DeviceName} ({monitor.Bounds.Width}x{monitor.Bounds.Height}) "
                    + $"as {format} at {frameRate} fps to {path}"
                    + (region is { } cropped
                        ? $", cropped to {cropped.Width}x{cropped.Height} at {cropped.X},{cropped.Y}"
                        : string.Empty));

            var result = await _recorder.RecordDisplayAsync(
                handle,
                path,
                format,
                cancellation.Token,
                region,
                frameRate,
                audio);

            DiagnosticLog.Verbose($"recording finished: {result.Path}");

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

            // With the recording rather than with the panel: the panel stays a few
            // seconds to name the file, and a frame still standing round nothing would
            // say a recording was running that had already finished. The click hook goes
            // with it — leaving a low-level mouse hook installed after the recording has
            // ended would put macshot in the path of every mouse event on the machine.
            border?.Close();
            clicks?.Dispose();

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
            settings.RecordingFilenameTemplate,
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

    /// <summary>
    /// Opens an image in the editor.
    /// </summary>
    /// <remarks>
    /// One at a time. Two editors would be two windows called macshot holding two
    /// versions of nearly the same screenshot, and the one thing worse than losing an
    /// annotation is saving the wrong copy of it.
    /// </remarks>
    private async Task ShowEditorAsync(CapturedFrame frame, IReadOnlyList<Annotation>? annotations = null)
    {
        _editor?.Close();

        var editor = new EditorWindow(frame, _settings, annotations);
        editor.PinRequested += (_, pinned) => Post(() => PinAsync(pinned));
        editor.AddCaptureRequested += (_, _) => Post(() => AddCaptureAsync(editor));

        // Delivered exactly as a capture is, so the editor needs no opinion about
        // clipboards, folders or history, and what Done means cannot drift between the
        // two paths.
        editor.Finished += (_, finished) => Post(() => DeliverAsync(finished));
        editor.Closed += (_, _) =>
        {
            if (ReferenceEquals(_editor, editor))
            {
                _editor = null;
            }

            // An editor closed from under an add-capture must not still be waiting for
            // one: the next capture would be handed to a window that no longer exists.
            if (ReferenceEquals(_addingTo, editor))
            {
                _addingTo = null;
            }
        };

        _editor = editor;
        await editor.ShowAsync();
    }

    /// <summary>
    /// Takes a capture for an editor to add under the image it already has.
    /// </summary>
    /// <remarks>
    /// The editor is hidden first, as macshot's is: the overlay is a still of the desktop
    /// taken a moment earlier, and an editor left on screen would be in the pixels the
    /// user is about to select from — so the obvious gesture, dragging a box over
    /// something next to the editor, would come back with a picture of the editor.
    /// </remarks>
    private async Task AddCaptureAsync(EditorWindow editor)
    {
        if (_overlays.Count > 0)
        {
            return;
        }

        _addingTo = editor;
        editor.GetAppWindow().Hide();

        try
        {
            await BeginAreaCaptureAsync();
        }
        catch (Exception)
        {
            // Whatever went wrong, the editor must not be left hidden with no way back.
            RestorePendingEditor();
            throw;
        }
    }

    /// <summary>
    /// Puts back the editor hidden for an add-capture, and answers which one it was so
    /// the caller can hand it the capture. Null when no editor was waiting.
    /// </summary>
    /// <remarks>
    /// Called from <see cref="DismissOverlays"/> rather than from the one path that
    /// succeeds, because every way out of the overlay ends the add — cancelling, opening
    /// the editor, starting a recording — and an editor left hidden by any of them looks
    /// like a window that closed itself.
    /// </remarks>
    private EditorWindow? RestorePendingEditor()
    {
        if (_addingTo is not { } editor)
        {
            return null;
        }

        _addingTo = null;
        editor.GetAppWindow().Show();
        editor.Activate();
        return editor;
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
    private void ReportError(Exception exception) => FailureReport.Show(_messageWindow.Handle, exception);

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
