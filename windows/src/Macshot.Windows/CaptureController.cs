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

using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

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
    private const int CommandCaptureText = 9;
    private const int CommandQuickCapture = 10;
    private const int CommandCaptureLastArea = 11;
    private const int CommandScrollCapture = 12;
    private const int CommandOpenImage = 13;
    private const int CommandOpenFromClipboard = 14;
    private const int CommandPinFromClipboard = 15;
    private const int CommandOpenVideo = 16;
    private const int CommandCheckForUpdates = 17;

    /// <summary>
    /// The picture on each menu line, as a Segoe Fluent Icons character.
    /// </summary>
    /// <remarks>
    /// One per item, chosen against the SF Symbol macshot puts on the same line
    /// (<c>AppDelegate.swift:709–806</c>) rather than by name: Crop for its <c>crop</c>,
    /// a monitor for its <c>desktopcomputer</c>, a scan frame for its
    /// <c>text.viewfinder</c>. Two lines share a picture where macshot's two are the same
    /// picture of the same thing — the whole screen, and doing something again.
    /// Quit carries none, as macshot's does not.
    /// </remarks>
    private const string GlyphCrop = "\uE7A8";
    private const string GlyphScreen = "\uE7F4";
    private const string GlyphScan = "\uE8FE";
    private const string GlyphDownload = "\uE896";
    private const string GlyphAgain = "\uE72C";
    private const string GlyphScroll = "\uEC8F";
    private const string GlyphStopwatch = "\uE916";
    private const string GlyphVideo = "\uE714";
    private const string GlyphHistory = "\uE81C";
    private const string GlyphGrid = "\uE7AA";
    private const string GlyphPicture = "\uE8B9";
    private const string GlyphMovies = "\uE8B2";
    private const string GlyphPaste = "\uE77F";
    private const string GlyphPinned = "\uE840";
    private const string GlyphSettings = "\uE713";

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
    /// The rest of macshot's configurable shortcuts. Numbered above the three that came
    /// first rather than renumbered into macshot's order: the id is what Windows is asked
    /// for and what a running registration is given back by, so moving one is a way to
    /// unregister something else.
    /// </summary>
    private const int HotkeyRecordArea = 7;
    private const int HotkeyHistory = 8;
    private const int HotkeyCaptureText = 9;
    private const int HotkeyQuickCapture = 10;
    private const int HotkeyScrollCapture = 11;
    private const int HotkeyOpenFromClipboard = 12;
    private const int HotkeyCaptureLastArea = 13;
    private const int HotkeyPinFromClipboard = 14;
    private const int HotkeyClearHistory = 15;

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

#if !OFFLINE
    /// <summary>
    /// The one uploader, shared by the overlay, the editor, the thumbnail and the video
    /// editor. One instance because it owns the connection pool and the single toast; a
    /// second would put two panels in the same place.
    /// </summary>
    private readonly Upload.UploadService _uploads;
#endif
    private readonly DispatcherQueue _dispatcher;

    /// <summary>
    /// The scheme registration and the pipe other launches hand URLs down. Held for the
    /// life of the app, whether or not the setting is on: the setting is what it is told,
    /// not whether it exists.
    /// </summary>
    private readonly UrlSchemeHost _urlScheme = new();

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

    /// <param name="startupUrl">
    /// The <c>macshot://</c> URL this launch was started to carry out, when it was. There
    /// was no macshot to hand it to, so this one does it as soon as it is ready.
    /// </param>
    public CaptureController(string? startupUrl = null)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The capture controller must be created on the UI thread.");

#if !OFFLINE
        _uploads = new Upload.UploadService(_settings);
#endif

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

        // After the language and before the first window, because the tracking depends on
        // the first and the face has to be in place for the second. Nothing built by this
        // point draws text: the tray menu is painted by Win32, not by XAML.
        AppFonts.Install(Application.Current.Resources);

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
            !_settings.Current.HideTrayIcon,
            ChosenTrayIcon(_settings.Current));

        _trayIcon.Theme = _settings.Current.Theme;

        // Unlike whether the icon is there at all, which is read once: the picture on it
        // can change under the pointer without the tray reordering itself, and macshot's
        // own icon setting takes effect as it is chosen.
        _settings.Changed += (_, settings) =>
        {
            _trayIcon.SetIcon(ChosenTrayIcon(settings));
            _trayIcon.Theme = settings.Theme;
        };

        // macshot's own menu, item for item and in its order — AppDelegate.swift:707–805.
        // The strings are macshot's source strings rather than paraphrases of them, which
        // is what makes them resolve: the translations are keyed on the English macshot
        // ships, so "Capture area" found nothing where "Capture Area" finds every
        // language the Mac app has.
        // The first six are macshot's CaptureMenuItemID.defaultOrder, in that order.
        _trayIcon.AddMenuItem(CommandCaptureArea, L("Capture Area"), GlyphCrop);
        _trayIcon.AddMenuItem(CommandCaptureAllScreens, L("Capture Screen"), GlyphScreen);
        _trayIcon.AddMenuItem(CommandCaptureText, L("Capture OCR & QR"), GlyphScan);
        _trayIcon.AddMenuItem(CommandQuickCapture, L("Quick Capture"), GlyphDownload);
        _trayIcon.AddMenuItem(CommandCaptureLastArea, L("Capture Last Area"), GlyphAgain);
        _trayIcon.AddMenuItem(CommandScrollCapture, L("Scroll Capture"), GlyphScroll);
        _trayIcon.AddSubmenu(L("Capture Delay"), DelayMenuEntries, L("None"), GlyphStopwatch);
        _trayIcon.AddSeparator();
        _trayIcon.AddMenuItem(CommandRecordArea, L("Record Area"), GlyphVideo);
        _trayIcon.AddMenuItem(CommandRecordScreen, L("Record Screen"), GlyphScreen);
        _trayIcon.AddSeparator();
        _trayIcon.AddSubmenu(
            L("Recent Captures"), RecentMenuEntries, L("No recent captures"), GlyphHistory);
        _trayIcon.AddMenuItem(CommandHistory, L("Show History Panel"), GlyphGrid);
        _trayIcon.AddSeparator();
        _trayIcon.AddMenuItem(CommandOpenImage, L("Open Image..."), GlyphPicture);
        _trayIcon.AddMenuItem(CommandOpenVideo, L("Open Video..."), GlyphMovies);
        _trayIcon.AddMenuItem(CommandOpenFromClipboard, L("Open from Clipboard"), GlyphPaste);
        _trayIcon.AddMenuItem(CommandPinFromClipboard, L("Pin from Clipboard"), GlyphPinned);
        _trayIcon.AddSeparator();
        _trayIcon.AddMenuItem(CommandPreferences, L("Settings..."), GlyphSettings);

        // macshot's own next item. What it does here is the check without the install:
        // there is no installer to hand a downloaded build to yet, so a newer release
        // opens its page. See UpdateService.
        _trayIcon.AddMenuItem(CommandCheckForUpdates, L("Check for Updates..."), GlyphAgain);

        _trayIcon.AddSeparator();
        _trayIcon.AddMenuItem(CommandQuit, L("Quit macshot"));
        _trayIcon.CommandInvoked += OnTrayCommandInvoked;
        _trayIcon.DefaultActionInvoked += (_, _) => Post(BeginAreaCaptureHonouringDelayAsync);

        // The six above are macshot's default order; this deals them out again in
        // whatever order the user has since put them in.
        ApplyCaptureMenuOrder(_settings.Current);
        _settings.Changed += (_, settings) => ApplyCaptureMenuOrder(settings);

        // After the menu exists, because applying a shortcut also writes it into the
        // menu entry that names it.
        ApplyHotkeys(_settings.Current);

        // Re-applied rather than read once, so a shortcut changed in preferences takes
        // effect without restarting macshot — which, for a background app with no
        // window, is something the user would have to be told how to do.
        _settings.Changed += (_, settings) => ApplyHotkeys(settings);

        // Marshalled here rather than in the host: the URL is read on a pipe thread, and
        // everything it can ask for touches windows.
        _urlScheme.CommandReceived += (_, command) => _dispatcher.TryEnqueue(() => Run(command));
        _urlScheme.Apply(_settings.Current.UrlSchemeEnabled);
        _settings.Changed += (_, settings) => _urlScheme.Apply(settings.UrlSchemeEnabled);

        // Last, and only after everything a command can reach exists. Queued rather than
        // run here, because this is still the constructor: a command that puts overlays
        // up would be doing it with the controller half-built.
        if (UrlSchemeCommands.Parse(startupUrl) is { } startupCommand)
        {
            _dispatcher.TryEnqueue(() => Run(startupCommand));
        }

        // macshot checks on its own as well as on request, which is what the setting on
        // the General page says. Queued like everything else here rather than awaited, so
        // a slow or hanging network cannot delay the tray icon appearing: the first thing
        // a user does after launching a background app is look for it.
        if (_settings.Current.AutomaticUpdateChecks)
        {
            Post(() => CheckForUpdatesAsync(asked: false));
        }
    }

    /// <summary>
    /// Does what a <c>macshot://</c> URL asked for.
    /// </summary>
    /// <remarks>
    /// macshot's <c>handleURLSchemeAction</c>. Every one of these arrives at the same
    /// method the menu item does, rather than at a path of its own: a link that captured
    /// slightly differently from the menu would be a second capture command to keep
    /// working.
    /// </remarks>
    private void Run(UrlSchemeCommand command)
    {
        switch (command.Action)
        {
        case UrlSchemeAction.Capture:
            Post(BeginAreaCaptureHonouringDelayAsync);
            break;
        case UrlSchemeAction.CaptureFullScreen:
            Post(CaptureAllScreensAsync);
            break;
        case UrlSchemeAction.CaptureLastArea:
            Post(() => BeginAreaCaptureAsync(restoreLastArea: true));
            break;
        case UrlSchemeAction.QuickCapture:
            Post(() => BeginAreaCaptureAsync(CaptureIntent.Quick));
            break;
        case UrlSchemeAction.Ocr:
            Post(() => BeginAreaCaptureAsync(CaptureIntent.Recognize));
            break;
#if !OFFLINE
        case UrlSchemeAction.OcrTranslate:
            Post(() => BeginAreaCaptureAsync(CaptureIntent.Translate, translateTarget: command.Argument));
            break;
#endif
        case UrlSchemeAction.Record:
            Post(BeginAreaRecordingAsync);
            break;
        case UrlSchemeAction.RecordFullScreen:
            // Not the toggle the shortcut uses. A link that stopped the recording when
            // one was running would be a link that means two different things, and the
            // one to stop with is the next command along.
            if (_recording is null)
            {
                Post(ToggleRecordingAsync);
            }

            break;
        case UrlSchemeAction.StopRecording:
            _recording?.Cancel();
            break;
        case UrlSchemeAction.ScrollCapture:
            Post(() => BeginAreaCaptureAsync(CaptureIntent.Scroll));
            break;
        case UrlSchemeAction.History:
            Post(ShowHistoryAsync);
            break;
        case UrlSchemeAction.Settings:
            ShowPreferences();
            break;
        case UrlSchemeAction.Open:
            if (command.Argument is { } file)
            {
                Post(() => OpenFileAsync(file));
            }

            break;
        case UrlSchemeAction.Edit:
            if (command.Argument is { } id)
            {
                Post(() => EditPastCaptureAsync(id));
            }

            break;
        default:
            // The offline build's ocr-translate, and nothing else: Core carries the whole
            // table because it is compiled once for both variants.
            break;
        }
    }

    /// <summary>
    /// Opens the image at <paramref name="path"/> in the editor — what
    /// <c>macshot://open?file=…</c> asks for.
    /// </summary>
    /// <remarks>
    /// Reported rather than swallowed. The user wrote the path into a link, and a link
    /// that does nothing gives them nothing to correct.
    /// </remarks>
    private async Task OpenFileAsync(string path)
    {
        try
        {
            await ShowEditorAsync(await ImageLoader.LoadAsync(path));
        }
        catch (Exception exception)
        {
            FailureReport.Notice(
                _messageWindow.Handle,
                $"macshot could not open '{path}': {exception.Message}");
        }
    }

    /// <summary>
    /// Opens a past capture in the editor by name — what <c>macshot://edit?id=…</c> asks
    /// for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The id is the capture's file name without its extension, which is the timestamp
    /// macshot's history names its files by. macOS has a UUID per entry to use instead;
    /// here the name is already the identity, and inventing a second one would mean
    /// nothing on disk could be pointed at.
    /// </para>
    /// <para>
    /// Whatever the history is currently keeping is searched, so an id from a capture
    /// that has since been dropped finds nothing — and says so, because the alternative
    /// is a link that quietly opens the wrong picture or none.
    /// </para>
    /// </remarks>
    private async Task EditPastCaptureAsync(string id)
    {
        var entry = ScreenshotHistory
            .Recent(int.MaxValue, _settings.Current)
            .FirstOrDefault(past => string.Equals(
                Path.GetFileNameWithoutExtension(past.Path),
                id,
                StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            FailureReport.Notice(
                _messageWindow.Handle,
                $"macshot has no capture called '{id}' in its history.");
            return;
        }

        try
        {
            await ReopenAsync(entry);
        }
        catch (Exception exception)
        {
            FailureReport.Notice(
                _messageWindow.Handle,
                $"macshot could not open '{entry.Path}': {exception.Message}");
        }
    }

    /// <summary>
    /// Puts the capture commands at the top of the menu in the order the settings say.
    /// </summary>
    /// <remarks>
    /// Re-applied on every save rather than read once, so the settings page's Up and Down
    /// change the menu while it is still open — which is the only way to see what was
    /// changed, a tray menu having no preview.
    /// </remarks>
    private void ApplyCaptureMenuOrder(CaptureSettings settings) =>
        _trayIcon.SetMenuItemOrder(
            [.. CaptureMenuItems.Resolve(settings.CaptureMenuOrder).Select(CommandOf)]);

    /// <summary>
    /// The icon file the settings ask for, or null for macshot's own.
    /// </summary>
    /// <remarks>
    /// The mode decides, not the path: a path left behind by someone who has since gone
    /// back to macshot's icon is a path they still want kept, and reading it would put
    /// their old icon back on a setting that says Default.
    /// </remarks>
    private static string? ChosenTrayIcon(CaptureSettings settings) =>
        settings.TrayIcon is TrayIconSource.Custom && !string.IsNullOrWhiteSpace(settings.TrayIconPath)
            ? settings.TrayIconPath
            : null;

    private static int CommandOf(CaptureMenuItem item) => item switch
    {
        CaptureMenuItem.CaptureArea => CommandCaptureArea,
        CaptureMenuItem.CaptureScreen => CommandCaptureAllScreens,
        CaptureMenuItem.CaptureOcr => CommandCaptureText,
        CaptureMenuItem.QuickCapture => CommandQuickCapture,
        CaptureMenuItem.CaptureLastArea => CommandCaptureLastArea,
        _ => CommandScrollCapture,
    };

    /// <summary>
    /// Claims the configured shortcuts, and says so once when Windows refuses any of
    /// them.
    /// </summary>
    /// <remarks>
    /// The refusals are collected and reported together. Told one at a time they would
    /// be a row of message boxes for a user who has just typed one shortcut that
    /// another program owns, and all but the first would be about shortcuts they did
    /// not touch.
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
            HotkeyRecordArea,
            CommandRecordArea,
            L("Record Area"),
            settings.RecordAreaBinding,
            BeginAreaRecordingAsync);
        Bind(
            HotkeyRecordScreen,
            CommandRecordScreen,
            L("Record Screen"),
            settings.RecordScreenBinding,
            ToggleRecordingAsync);
        Bind(
            HotkeyHistory,
            CommandHistory,
            L("Show History Panel"),
            settings.HistoryBinding,
            ShowHistoryAsync);
        Bind(
            HotkeyCaptureText,
            CommandCaptureText,
            L("Capture OCR & QR"),
            settings.CaptureTextBinding,
            () => BeginAreaCaptureAsync(CaptureIntent.Recognize));
        Bind(
            HotkeyQuickCapture,
            CommandQuickCapture,
            L("Quick Capture"),
            settings.QuickCaptureBinding,
            () => BeginAreaCaptureAsync(CaptureIntent.Quick));
        Bind(
            HotkeyScrollCapture,
            CommandScrollCapture,
            L("Scroll Capture"),
            settings.ScrollCaptureBinding,
            () => BeginAreaCaptureAsync(CaptureIntent.Scroll));
        Bind(
            HotkeyOpenFromClipboard,
            CommandOpenFromClipboard,
            L("Open from Clipboard"),
            settings.OpenFromClipboardBinding,
            OpenFromClipboardAsync);
        Bind(
            HotkeyCaptureLastArea,
            CommandCaptureLastArea,
            L("Capture Last Area"),
            settings.CaptureLastAreaBinding,
            () => BeginAreaCaptureAsync(restoreLastArea: true));
        Bind(
            HotkeyPinFromClipboard,
            CommandPinFromClipboard,
            L("Pin from Clipboard"),
            settings.PinFromClipboardBinding,
            PinFromClipboardAsync);

        // No menu entry of its own: macshot clears the history from the history panel,
        // and a notification-area menu that can wipe it in one click is a menu with a
        // trap in it. The shortcut exists because macshot offers one, unbound.
        Bind(
            HotkeyClearHistory,
            command: 0,
            L("Clear History"),
            settings.ClearHistoryBinding,
            ClearHistoryAsync);

        if (refused.Count > 0)
        {
            FailureReport.Notice(
                _messageWindow.Handle,
                "Windows would not give macshot these shortcuts, so they are not active: "
                    + string.Join(", ", refused)
                    + ". Another program may already own them. The notification-area menu still works.");
        }

        void Bind(int hotkey, int command, string label, HotkeyBinding? binding, Func<Task> action)
        {
            // Given back first: re-registering an id Windows still holds fails, which
            // would turn every preferences save into a lost shortcut.
            _hotkeys.Unregister(hotkey);

            // A slot with no menu entry of its own. Nothing to name, and asking the menu
            // to rename an item it does not have would be a silent no-op at best.
            var named = command != 0;

            if (binding is null)
            {
                // Half of macshot's shortcuts ship like this, and any of the rest can be
                // taken off. Nothing to register and nothing to complain about — only a
                // menu entry that has to stop claiming a shortcut it no longer has.
                if (named)
                {
                    _trayIcon.SetMenuItemText(command, label);
                }

                return;
            }

            if (_hotkeys.TryRegister(hotkey, binding, () => Post(action)))
            {
                if (named)
                {
                    _trayIcon.SetMenuItemText(command, $"{label}\t{binding}");
                }

                DiagnosticLog.Verbose($"hotkey {binding} registered for {label}");
            }
            else
            {
                // Named without a shortcut rather than with one that does nothing.
                if (named)
                {
                    _trayIcon.SetMenuItemText(command, label);
                }

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
        _recording is null ? BeginAreaCaptureAsync(CaptureIntent.Record) : Task.CompletedTask;

    /// <summary>Puts one selection overlay on every display.</summary>
    /// <param name="intent">What the region the user is about to draw is for.</param>
    /// <param name="restoreLastArea">
    /// Whether to take the remembered region straight away instead of waiting for a drag
    /// — macshot's <c>pendingRestoreLastArea</c>. Nothing remembered means an ordinary
    /// capture, which is macshot's fallback too.
    /// </param>
    /// <param name="translateTarget">
    /// The language <see cref="CaptureIntent.Translate"/> translates into, or null for
    /// the one the settings name. Only <c>macshot://ocr-translate?target=…</c> passes
    /// one.
    /// </param>
    public async Task BeginAreaCaptureAsync(
        CaptureIntent intent = CaptureIntent.Capture,
        bool restoreLastArea = false,
        string? translateTarget = null)
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
                Intent = intent,
                TranslateTarget = translateTarget,
            };
            overlay.CaptureCompleted += OnCaptureCompleted;
            overlay.SelectionCommitted += OnSelectionCommitted;
            overlay.Cancelled += OnCaptureCancelled;
            overlay.ScrollCaptureRequested += OnScrollCaptureRequested;
            overlay.RecordingRequested += OnRecordingRequested;
            overlay.EditorRequested += OnEditorRequested;
            overlay.WindowSnapToggled += OnWindowSnapToggled;

            // The overlay is dismissed first: the preferences window is titled and would
            // otherwise open underneath a full-screen always-on-top overlay.
            overlay.PreferencesRequested += (_, _) =>
            {
                DismissOverlays();
                ShowPreferences();
            };

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

        // After every overlay is up rather than as each one appears: taking a region
        // closes the other displays' overlays, and one closed before it was shown is
        // left standing over everything with nothing listening to it.
        if (restoreLastArea)
        {
            foreach (var overlay in _overlays.ToArray())
            {
                if (overlay.AcceptRememberedSelection())
                {
                    break;
                }
            }
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
        var frame = await _screenCapture.CaptureVirtualDesktopAsync(
            displays,
            _settings.Current.CaptureCursor);

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

        _urlScheme.Dispose();
        _screenCapture.Dispose();
        _recorder.Dispose();
        _trayIcon.Dispose();
        _hotkeys.Dispose();
        _messageWindow.Dispose();

        // After the menu that draws from them is gone.
        MenuIcons.Clear();
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
        case CommandCaptureText:
            Post(() => BeginAreaCaptureAsync(CaptureIntent.Recognize));
            break;
        case CommandQuickCapture:
            Post(() => BeginAreaCaptureAsync(CaptureIntent.Quick));
            break;
        case CommandCaptureLastArea:
            Post(() => BeginAreaCaptureAsync(restoreLastArea: true));
            break;
        case CommandScrollCapture:
            Post(() => BeginAreaCaptureAsync(CaptureIntent.Scroll));
            break;
        case CommandOpenImage:
            Post(OpenImageAsync);
            break;
        case CommandOpenVideo:
            Post(OpenVideoAsync);
            break;
        case CommandOpenFromClipboard:
            Post(OpenFromClipboardAsync);
            break;
        case CommandPinFromClipboard:
            Post(PinFromClipboardAsync);
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
        case CommandCheckForUpdates:
            Post(() => CheckForUpdatesAsync(asked: true));
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
    private async Task DeliverAsync(CaptureCompletion completion, string? origin = null)
    {
        var frame = completion.Frame;
        if (completion.Outcome is CaptureOutcome.Deliver)
        {
            await DeliverAsync(frame, completion.Editable, completion.WindowTitle, origin);
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
                await SavePrompt.SaveAsync(
                    _messageWindow.Handle,
                    frame,
                    settings,
                    completion.WindowTitle);
                break;

#if !OFFLINE
            case CaptureOutcome.Upload:
                // Not awaited: an upload takes as long as the network does, and the
                // history below is what makes the capture recoverable if it fails. The
                // toast is the only thing waiting on it.
                Post(() => _uploads.UploadAsync(frame));
                break;
#endif

            default:
                break;
        }

        _ = await ArchiveAsync(frame, settings, completion.Editable, origin);

        // Only for the two that put the capture somewhere. Pinning and uploading each
        // leave a window on screen saying so, and a sound on top of that is noise.
        if (completion.Outcome is CaptureOutcome.Copy or CaptureOutcome.Save)
        {
            CaptureSound.Play(settings.PlayCaptureSound);
        }

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
        string? windowTitle = null,
        string? origin = null)
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
            // Through the prompt rather than straight to the folder, because "Ask where
            // to save" has to reach the capture that is saved without anyone pressing
            // Save — which is most of them.
            await SavePrompt.SaveAsync(_messageWindow.Handle, frame, settings, windowTitle);
        }

        // Once for the capture rather than once per destination: saving and copying the
        // same picture is one thing happening, and it should sound like one.
        if (settings.CopyToClipboard || settings.AutoSave)
        {
            CaptureSound.Play(settings.PlayCaptureSound);
        }

        // After the actions the user asked for, so the extra encode is never in front
        // of the clipboard. History is the safety net under delivery, not part of it,
        // and it is written whether or not the capture was saved anywhere else.
        var archived = await ArchiveAsync(frame, settings, editable, origin);

        // Alongside whatever else was done with it, not instead: someone who wants every
        // capture annotated still wants it copied, and an editor that swallowed the copy
        // would make the setting cost something. macshot's quickCaptureOpenEditor.
        if (settings.QuickCaptureOpenEditor)
        {
            await ShowEditorAsync(frame);
            return;
        }

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
        // One panel rather than three when the user asked for replacing — macshot's
        // thumbnailStacking, where the column is the whole difference between the two.
        var room = _settings.Current.StackThumbnails ? MaxThumbnails : 1;

        foreach (var oldest in _thumbnails.Take(_thumbnails.Count - room + 1).ToArray())
        {
            oldest.Close();
        }

        var thumbnail = new ThumbnailWindow(frame, _settings, archived);
        thumbnail.PinRequested += (_, pinned) => Post(() => PinAsync(pinned));
#if !OFFLINE
        thumbnail.UploadRequested += (_, taken) => Post(() => _uploads.UploadAsync(taken));
#endif
        // With the archive copy, so that annotating the capture just taken writes back
        // over the entry it already has instead of leaving the history holding the same
        // capture twice, once with the marks and once without.
        thumbnail.EditRequested += (_, captured) => Post(() => ShowEditorAsync(captured, origin: archived));
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
    /// What "Open Video..." offers, which is what Windows can play: macshot's list less
    /// the QuickTime movie only its own platform reads.
    /// </summary>
    private static readonly string[] VideoExtensions = [".mp4", ".mov", ".m4v", ".gif"];

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
        _recent = ScreenshotHistory.Recent(RecentMenuCount, _settings.Current);
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
    /// Puts a finished capture into the history, over the entry it came from when it came
    /// from one.
    /// </summary>
    private static Task<string?> ArchiveAsync(
        CapturedFrame frame,
        CaptureSettings settings,
        EditableCapture? editable,
        string? origin) =>
        origin is null
            ? ScreenshotHistory.RecordAsync(frame, settings, editable)
            : ScreenshotHistory.RewriteAsync(origin, frame, settings, editable);

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
                await ShowEditorAsync(await ImageLoader.LoadAsync(raw), annotations, entry.Path);
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

        await ShowEditorAsync(await ImageLoader.LoadAsync(entry.Path), origin: entry.Path);
    }

    /// <summary>
    /// Carries out the two things the history panel cannot do for itself, because both
    /// need a window this keeps: the editor, and the pins.
    /// </summary>
    private async Task RunHistoryAsync(HistoryRequest request)
    {
        try
        {
            if (request.Action is HistoryAction.Open)
            {
                await ReopenAsync(request.Entry);
                return;
            }

            await PinAsync(await ImageLoader.LoadAsync(request.Entry.Path));
        }
        catch (Exception exception)
        {
            // The panel drew this capture a moment ago, so a failure here is the file
            // going away underneath it. Nothing to ask the user about.
            DiagnosticLog.Write($"Could not reopen '{request.Entry.Path}': {exception.Message}");
        }
    }

    /// <summary>
    /// macshot's Check for Updates..., and the check the setting above it promises.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one difference between the two callers is what silence means. Asked for from
    /// the menu, every outcome has to be said — a menu item that does nothing visible has
    /// been pressed and has failed, as far as the person who pressed it knows. Run on
    /// startup, only an update is worth a box: a laptop that was opened on a train would
    /// otherwise greet its owner with a network error every morning.
    /// </para>
    /// <para>
    /// The startup check is what makes <c>AutomaticUpdateChecks</c> mean something. It
    /// was in the settings file and on the General page before anything read it, which is
    /// a checkbox that lies.
    /// </para>
    /// </remarks>
    /// <param name="asked">Whether the user asked, rather than the app checking by itself.</param>
    private async Task CheckForUpdatesAsync(bool asked)
    {
        try
        {
            var offer = await UpdateService.FindUpdateAsync(_settings.Current.BetaUpdates);

            if (offer is not { } update)
            {
                if (asked)
                {
                    Message.Say(
                        _messageWindow.Handle,
                        $"{L("You're up to date!")}{Environment.NewLine}{Environment.NewLine}"
                            + $"{BuildVariant.DisplayName} {UpdateService.CurrentVersion}");
                }

                return;
            }

            var page = update.PageUrl.Length > 0 ? update.PageUrl : UpdateService.ReleasesPage;
            if (Message.Ask(
                _messageWindow.Handle,
                $"{L("A new version of macshot is available.")}{Environment.NewLine}{Environment.NewLine}"
                    + $"{update.Tag}{Environment.NewLine}{Environment.NewLine}"
                    + L("Open the download page?")))
            {
                OpenWithShell(page);
            }
        }
        catch (Exception exception)
        {
            // Silent unless the user asked: a check nobody asked for that could not be
            // made is not news, and the log is where it belongs.
            DiagnosticLog.Write($"The update check failed: {exception.Message}");

            if (asked)
            {
                FailureReport.Notice(
                    _messageWindow.Handle,
                    L("Could not check for updates.") + Environment.NewLine + exception.Message);
            }
        }
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

        var history = new HistoryWindow(_settings);
        history.ActionRequested += (_, request) => Post(() => RunHistoryAsync(request));

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

    /// <summary>
    /// Throws the history away, and puts an open panel back with what is left.
    /// </summary>
    /// <remarks>
    /// The panel reads the folder when it opens rather than watching it, so one left
    /// standing would go on offering captures whose files have just been deleted.
    /// Closing and reopening it is the whole of the refresh it needs.
    /// </remarks>
    private Task ClearHistoryAsync()
    {
        ScreenshotHistory.Clear();

        if (_history is { } panel)
        {
            panel.Close();
            return ShowHistoryAsync();
        }

        return Task.CompletedTask;
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

        // Read now rather than held: a scroll capture is started long after the settings
        // window was last open, and taking the values here is what lets a change made in
        // between apply to this run.
        var settings = _settings.Current;
        var session = new ScrollCaptureSession(
            _screenCapture.TryCaptureWindowAsync,
            new ScrollDriver(ScrollSpeeds.NotchesPerStep(settings.ScrollSpeed)),
            settings.ScrollMaxHeight,
            settings.ScrollAutoScroll);
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
    /// Records one display, one region of it, or one window, until something asks it to
    /// stop.
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
    /// <para>
    /// A window recording keeps only the panel that stops it. The region frame would stand
    /// where the window used to be the moment it is moved, and the click ring, the
    /// keystroke pill and the webcam bubble are macshot's own windows over the desktop —
    /// none of them is in the recorded window's tree, so all three would be shown to the
    /// user and be absent from the file. They are left down and the log says so.
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
        // a capture item is one display, and it starts at its own top-left corner. A
        // window is its own item and needs no region at all — what it keeps is decided
        // from the window itself, inside the recorder.
        var followed = request.Window;
        var region = followed is null && request.Region is { } aimed
            ? monitor.VirtualToLocal(aimed)
            : (CaptureRegion?)null;

        if (region is { IsEmpty: true })
        {
            throw new InvalidOperationException("That region is not on the display being recorded.");
        }

        using var cancellation = new CancellationTokenSource();
        // Not built at all when it is not wanted, rather than built and hidden: it would
        // otherwise appear at the end to name the saved file, which is a panel arriving
        // from nowhere over whatever was being recorded. Escape and the notification icon
        // still stop the recording, and the on-stop action still says where it went.
        RecordingHudWindow? hud = null;
        if (!_settings.Current.HideRecordingHud)
        {
            hud = new RecordingHudWindow();
            hud.StopRequested += (_, _) => cancellation.Cancel();
            hud.PauseToggled += (_, held) => _recorder.SetPaused(held);

            // The panel belongs to what is being recorded, so it is placed against the
            // region — or against the whole display, when that is what is being recorded.
            hud.ShowHud(request.Region ?? monitor.Bounds, monitor);
        }

        // And a frame round the same rectangle, which is what still says where the
        // recording is once that panel has been dragged out of the way.
        RecordedRegionWindow? border = null;
        if (followed is null && _settings.Current.ShowRecordedRegionBorder)
        {
            border = new RecordedRegionWindow();
            border.ShowAround(request.Region ?? monitor.Bounds, monitor.Scale);
        }

        // And a ring out of every click, which unlike the frame is meant to be in the
        // file: it is the only thing that tells a viewer a press happened at all.
        ClickHighlightOverlay? clicks = null;
        if (followed is null && _settings.Current.ShowClickHighlight)
        {
            clicks = new ClickHighlightOverlay(monitor.Scale);
            clicks.Start();
        }

        // And what is being typed, at the foot of the same rectangle. Also meant to be in
        // the file: a recording that teaches a shortcut has to show the shortcut.
        KeystrokeOverlay? keystrokes = null;
        if (followed is null && _settings.Current.ShowKeystrokes)
        {
            keystrokes = new KeystrokeOverlay(request.Region ?? monitor.Bounds, monitor.Scale)
            {
                ShowAll = _settings.Current.ShowEveryKeystroke,
            };
            keystrokes.Start();
        }

        // And the camera, in a corner of the same rectangle. The one overlay here that is
        // meant to be in the file rather than kept out of it.
        WebcamWindow? webcam = null;
        if (followed is null && _settings.Current.RecordWebcam)
        {
            var bubble = new WebcamWindow();
            var current = _settings.Current;

            webcam = await bubble.ShowInAsync(
                request.Region ?? monitor.Bounds,
                current.WebcamCorner,
                current.WebcamSize,
                current.WebcamShape,
                monitor.Scale)
                ? bubble
                : null;

            if (webcam is null)
            {
                // No camera, or Windows says no. Closed rather than left showing a black
                // circle over the recording, and the recording goes ahead without it.
                await bubble.StopAsync();
            }
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
                (followed is { } target
                    ? $"recording the window '{target.Title}' ({target.Id:X}), "
                        + "so no region frame, click ring, keystroke pill or webcam — "
                        + "none of them is in that window's own tree"
                    : $"recording {monitor.DeviceName} ({monitor.Bounds.Width}x{monitor.Bounds.Height})")
                    + $" as {format} at {frameRate} fps to {path}"
                    + (region is { } cropped
                        ? $", cropped to {cropped.Width}x{cropped.Height} at {cropped.X},{cropped.Y}"
                        : string.Empty));

            var result = followed is { } window
                ? await _recorder.RecordWindowAsync(window, path, format, cancellation.Token, frameRate, audio)
                : await _recorder.RecordDisplayAsync(
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
            hud?.ShowSaved(Path.GetFileName(result.Path));

            await DeliverRecordingAsync(result.Path);
        }
        catch (Exception)
        {
            // A failure has nothing to report there; the message box reports it.
            hud?.Close();
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
            keystrokes?.Dispose();

            // Awaited nowhere, but the camera is released inside it before the window
            // goes: the light beside the lens has to go out when the bubble does.
            if (webcam is { } bubble)
            {
                _ = bubble.StopAsync();
            }

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
    /// says, because there is nowhere else for it to go: minutes of video do not belong
    /// on the clipboard, and the editor that opens next is an editor of a file on disk.
    /// </remarks>
    /// <summary>
    /// Does whatever the preferences say happens once a recording stops.
    /// </summary>
    /// <remarks>
    /// Best effort throughout: the recording is already on disk and the panel already
    /// says so, and interrupting the user to report that the folder would not open would
    /// be reporting a problem they do not have.
    /// </remarks>
    private async Task DeliverRecordingAsync(string path)
    {
        try
        {
            switch (_settings.Current.RecordingOnStop)
            {
                case RecordingOnStop.ShowInFolder:
                    // Selected rather than merely opened, because a folder of thirty
                    // recordings does not answer "where did the one I just made go".
                    using (Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")))
                    {
                    }

                    break;

                case RecordingOnStop.CopyToClipboard:
                    await CopyRecordingAsync(path);
                    break;

                case RecordingOnStop.OpenEditor:
                    // On the dispatcher, because a window is being made: the recording
                    // stops on whichever thread the encoder finished on.
                    _dispatcher.TryEnqueue(() => ShowVideoEditor(path));
                    break;

                default:
                    break;
            }
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"Could not deliver the recording at '{path}': {exception.Message}");
        }
    }

    /// <summary>
    /// Puts the recording's file on the clipboard.
    /// </summary>
    /// <remarks>
    /// The file rather than its pixels: a video has no single frame to paste, and what
    /// takes a video is something that takes an attachment.
    /// </remarks>
    private static async Task CopyRecordingAsync(string path)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetStorageItems([await StorageFile.GetFileFromPathAsync(path)]);

        Clipboard.SetContent(package);

        // Flushed, so the recording survives macshot being quit — the same reason a
        // copied capture is flushed.
        Clipboard.Flush();
    }

    private string ResolveRecordingPath(RecordingFormat format)
    {
        var settings = _settings.Current;

        // A folder of its own if one is named, and otherwise wherever captures go — so
        // that moving the capture folder moves recordings with it, which is what someone
        // who never set this expects.
        var directory = string.IsNullOrWhiteSpace(settings.RecordingDirectory)
            ? ImageDelivery.ResolveDirectory(settings)
            : settings.RecordingDirectory;

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
    /// <summary>
    /// Opens a picture that was never a capture in the editor — macshot's "Open Image...".
    /// </summary>
    /// <remarks>
    /// The editor is the same one a capture opens, so everything it can do to a
    /// screenshot it can do to a file: annotate it, crop it, pin it, save it somewhere
    /// else. That is the whole feature.
    /// </remarks>
    private async Task OpenImageAsync()
    {
        try
        {
            if (await ClipboardImages.PickAsync(_messageWindow.Handle) is { } frame)
            {
                await ShowEditorAsync(frame);
            }
        }
        catch (Exception exception)
        {
            // A file the picker offered and the decoder then refused: named as a picture,
            // and not one Windows can read. macshot returns silently here; the user chose
            // the file, so they are told why nothing opened. Not through L: macshot has no
            // string for this, and inventing a key would look translated and never be.
            FailureReport.Notice(
                _messageWindow.Handle,
                $"macshot could not open that image: {exception.Message}");
        }
    }

    /// <summary>
    /// Opens a recording in the video editor — macshot's "Open Video...".
    /// </summary>
    /// <remarks>
    /// Any file, not only one macshot made: the editor trims and re-encodes whatever
    /// Windows can read, which is the same offer macshot makes.
    /// </remarks>
    private async Task OpenVideoAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };

        // A desktop app has no CoreWindow for the picker to belong to, so it is given a
        // window handle instead. Without this the call throws rather than opening.
        InitializeWithWindow.Initialize(picker, _messageWindow.Handle);

        foreach (var extension in VideoExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        if (await picker.PickSingleFileAsync() is { } file)
        {
            ShowVideoEditor(file.Path);
        }
    }

    /// <summary>
    /// Opens <paramref name="path"/> in the video editor, wired to send what it exports.
    /// </summary>
    private void ShowVideoEditor(string path)
    {
#if OFFLINE
        VideoEditorWindow.Show(path, _settings);
#else
        // Subscribed through the callback rather than after the call, because a second
        // "Open Video..." on a file already open hands back the window that is there —
        // and subscribing to it twice would send the next export twice.
        VideoEditorWindow.Show(
            path,
            _settings,
            editor => editor.UploadRequested += (_, exported) => Post(() => _uploads.UploadFileAsync(exported)));
#endif
    }

    /// <summary>Opens the picture on the clipboard in the editor.</summary>
    private async Task OpenFromClipboardAsync()
    {
        if (await ClipboardImages.ReadAsync(renderText: false) is not { } frame)
        {
            FailureReport.Notice(
                _messageWindow.Handle,
                $"{L("No Image on Clipboard")}\n\n"
                    + L("Copy an image to the clipboard first, then try again."));
            return;
        }

        await ShowEditorAsync(frame);
    }

    /// <summary>
    /// Pins the picture on the clipboard, or a picture of the text on it.
    /// </summary>
    /// <remarks>
    /// Text counts here and not in the item above it, which is macshot's split too: a pin
    /// is a thing to keep in front of you while you type somewhere else, and a snippet of
    /// copied text is exactly that. The editor's item says image and means it.
    /// </remarks>
    private async Task PinFromClipboardAsync()
    {
        if (await ClipboardImages.ReadAsync(renderText: true) is not { } frame)
        {
            FailureReport.Notice(
                _messageWindow.Handle,
                $"{L("No Image or Text on Clipboard")}\n\n"
                    + L("Copy an image or text to the clipboard first, then try again."));
            return;
        }

        await PinAsync(frame);
    }

    /// <param name="origin">
    /// The history entry this capture was opened from, when it was. Pressing Done writes
    /// back over that entry instead of adding a second one: reopening a capture to move an
    /// arrow is editing it, not taking another.
    /// </param>
    private async Task ShowEditorAsync(
        CapturedFrame frame,
        IReadOnlyList<Annotation>? annotations = null,
        string? origin = null)
    {
        _editor?.Close();

        var editor = new EditorWindow(frame, _settings, annotations);
        editor.PinRequested += (_, pinned) => Post(() => PinAsync(pinned));
#if !OFFLINE
        editor.UploadRequested += (_, taken) => Post(() => _uploads.UploadAsync(taken));
#endif
        editor.AddCaptureRequested += (_, _) => Post(() => AddCaptureAsync(editor));

        // Delivered exactly as a capture is, so the editor needs no opinion about
        // clipboards, folders or history, and what Done means cannot drift between the
        // two paths.
        editor.Finished += (_, finished) => Post(() => DeliverAsync(finished, origin));
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
