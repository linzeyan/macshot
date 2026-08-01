using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Core.Input;
using Macshot.Windows.Core.Localization;
using Macshot.Windows.Core.Recognition;
using Macshot.Windows.Core.Upload;

namespace Macshot.Windows.Core.Output;

/// <summary>
/// Everything the user can change about how a finished capture is delivered.
/// </summary>
/// <remarks>
/// This is the Windows counterpart of the macOS <c>UserDefaults</c> keys, gathered
/// into one value instead of scattered loose keys, so the delivery path takes a
/// single argument and the whole thing round-trips through one JSON file. It lives
/// in Core with no persistence of its own: reading and writing the file is the
/// app's job, validating the values is this type's.
/// </remarks>
public sealed record CaptureSettings
{
    public const int MinQuality = 1;
    public const int MaxQuality = 100;
    public const int MinThumbnailSeconds = 1;
    public const int MaxThumbnailSeconds = 60;
    public const double MinStrokeWidth = 1;
    public const double MaxStrokeWidth = 64;

    /// <summary>
    /// The most a rectangle's corners can be rounded. Past this the shape reads as a
    /// pill rather than a box, and the rasterizer clamps it to half the shorter side
    /// anyway, so a larger number would only look like it did nothing.
    /// </summary>
    public const double MaxCornerRadius = 64;
    public const int MinDelaySeconds = 1;
    public const int MaxDelaySeconds = 60;
    public const int MaxHistorySize = 200;

    /// <summary>
    /// Below this a remembered selection is not worth restoring — it is the residue
    /// of a click, not a region someone chose.
    /// </summary>
    public const double MinRememberedSelection = 8;

    public static CaptureSettings Default { get; } = new();

    public CaptureImageFormat Format { get; init; } = CaptureImageFormat.Png;

    /// <summary>Encoder quality for lossy formats, 1–100. Ignored for PNG.</summary>
    public int Quality { get; init; } = 90;

    /// <summary>
    /// Where captures are written. Null means "wherever the app decides", which is
    /// Pictures\Macshot; storing null rather than that resolved path keeps the file
    /// portable between machines whose Pictures folder is redirected differently.
    /// </summary>
    public string? SaveDirectory { get; init; }

    public string FilenameTemplate { get; init; } = Output.FilenameTemplate.Default;

    /// <summary>
    /// What a recording is named. Separate from <see cref="FilenameTemplate"/> because
    /// macshot keeps it separate — one template for both leaves a folder where the
    /// videos and the screenshots cannot be told apart by name, which is what this
    /// port did until it had a second template to use.
    /// </summary>
    public string RecordingFilenameTemplate { get; init; } = Output.FilenameTemplate.DefaultRecording;

    public bool CopyToClipboard { get; init; } = true;

    /// <summary>Writes the capture to <see cref="SaveDirectory"/> without asking.</summary>
    public bool AutoSave { get; init; } = true;

    public bool ShowThumbnail { get; init; } = true;

    /// <summary>
    /// What a screen recording is written as. MP4 by default because it is what a
    /// recording should be; GIF costs size and colour, and is chosen for where the
    /// file has to go rather than for what it is.
    /// </summary>
    public RecordingFormat RecordingFormat { get; init; } = RecordingFormat.Mp4;

    public int ThumbnailSeconds { get; init; } = 6;

    /// <summary>
    /// Which corner the panels after a capture stack in. macshot's
    /// <c>thumbnailCorner</c>, and bottom-right by default as macshot's is — which is
    /// also where Windows puts its own notices, so it is the corner a user has already
    /// arranged their windows around.
    /// </summary>
    public ThumbnailCorner ThumbnailCorner { get; init; } = ThumbnailCorner.BottomRight;

    /// <summary>
    /// How big those panels are, as a multiple of macshot's 240 × 160. macshot's
    /// <c>thumbnailScale</c>, from half size to double.
    /// </summary>
    public double ThumbnailScale { get; init; } = 1;

    /// <summary>
    /// How many frames a second a screen recording is taken at.
    /// </summary>
    /// <remarks>
    /// macshot's <c>recordingFPS</c>, whose menu offers 15, 24, 30, 60 and 120 —
    /// <c>SettingsWindowController.swift:1551</c>. Anything in the plan's range is
    /// accepted here rather than only those five, because this file is edited by hand
    /// and refusing 45 would be refusing it for no reason.
    /// </remarks>
    public int RecordingFrameRate { get; init; } = RecordingPlan.DefaultFrameRate;

    /// <summary>
    /// How many frames a second a recording written as a GIF is taken at.
    /// </summary>
    /// <remarks>
    /// Its own setting rather than <see cref="RecordingFrameRate"/>, because the two
    /// are answers to different questions: one is how smooth the recording should be,
    /// the other is how large a GIF may get before the destination that only takes GIFs
    /// refuses it.
    /// </remarks>
    public int GifFrameRate { get; init; } = GifRecordingPlan.DefaultFrameRate;

    /// <summary>
    /// Whether a recording carries what the machine is playing.
    /// </summary>
    /// <remarks>
    /// Off by default, as macshot's <c>recordSystemAudio</c> is: recording sound nobody
    /// asked for is a surprise in a file that gets shared.
    /// </remarks>
    public bool RecordSystemAudio { get; init; }

    /// <summary>
    /// Whether a frame is drawn round the part of the screen a recording is taking.
    /// </summary>
    /// <remarks>
    /// macshot's <c>showSelectionBorder</c>, and on for the same reason: once the
    /// recording panel has been dragged clear of the region, the frame is the only thing
    /// left saying what is being recorded. It is laid outside the recorded rectangle, so
    /// turning it on cannot put a purple line in the file.
    /// </remarks>
    public bool ShowRecordedRegionBorder { get; init; } = true;

    /// <summary>
    /// Whether a ring blooms out of every click while a recording is running.
    /// </summary>
    /// <remarks>
    /// macshot's <c>recordMouseHighlight</c>, and off by default as macshot's is. Unlike
    /// the region frame this one is <em>inside</em> the recording — that is the whole
    /// point, since a viewer otherwise sees the pointer move and something happen with
    /// nothing in between — so it cannot be on without having been asked for. macOS also
    /// gates it on the Input Monitoring permission; Windows asks for nothing to install a
    /// low-level mouse hook, so there is no permission gate on this side.
    /// </remarks>
    public bool ShowClickHighlight { get; init; }

    /// <summary>
    /// Whether a pill at the foot of the recorded region says what is being typed.
    /// </summary>
    /// <remarks>
    /// macshot's <c>recordKeystroke</c>, off by default as macshot's is, and in the
    /// recording for the same reason the click ring is: a viewer watching a shortcut being
    /// used sees the result and never the shortcut. macOS gates this on Input Monitoring;
    /// a low-level keyboard hook on Windows asks for nothing, so there is no gate here.
    /// </remarks>
    public bool ShowKeystrokes { get; init; }

    /// <summary>
    /// Whether every keystroke shows, or only the ones that make a shortcut.
    /// </summary>
    /// <remarks>
    /// macshot's <c>keystrokeShowAll</c>, and off by default there too — which is the
    /// safe way round. A recording of somebody answering an email should not be a
    /// transcript of the email, and it becomes one the moment this is on and forgotten.
    /// </remarks>
    public bool ShowEveryKeystroke { get; init; }

    /// <summary>
    /// Whether the camera appears in a corner of the recording. macshot's
    /// <c>recordWebcam</c>, off by default as macshot's is.
    /// </summary>
    /// <remarks>
    /// Off by default and asked for each time on the strip, because this is the one
    /// recording overlay that puts the person at the keyboard into the file. A camera
    /// that came on because it was left on last month is the wrong kind of surprise.
    /// </remarks>
    public bool RecordWebcam { get; init; }

    /// <summary>Which corner of the recorded region the camera sits in.</summary>
    public WebcamCorner WebcamCorner { get; init; } = WebcamCorner.BottomRight;

    /// <summary>How big the camera bubble is. macshot's <c>webcamSize</c>.</summary>
    public WebcamSize WebcamSize { get; init; } = WebcamSize.Medium;

    /// <summary>Whether the camera is a circle or a rounded rectangle.</summary>
    public WebcamShape WebcamShape { get; init; } = WebcamShape.Circle;

    /// <summary>
    /// Where recordings go, or empty to put them wherever captures go. macshot's
    /// <c>recordingSavePath</c>.
    /// </summary>
    /// <remarks>
    /// Empty rather than defaulted to the capture folder, so that someone who changes
    /// where captures go moves their recordings with them without having to change this
    /// too. Only a folder named here overrides that.
    /// </remarks>
    public string RecordingDirectory { get; init; } = string.Empty;

    /// <summary>What happens the moment a recording stops. macshot's <c>recordingOnStop</c>.</summary>
    public RecordingOnStop RecordingOnStop { get; init; } = RecordingOnStop.OpenEditor;

    /// <summary>
    /// Whether the recording panel is left off the screen. macshot's
    /// <c>hideRecordingHUD</c>.
    /// </summary>
    /// <remarks>
    /// For recording something the panel would be sitting on top of. Escape and the
    /// notification icon both still stop the recording, which is what makes hiding it
    /// safe — a recording that cannot be stopped is the one thing worse than a panel in
    /// the way.
    /// </remarks>
    public bool HideRecordingHud { get; init; }

    /// <summary>
    /// The single keys the overlay and the editor answer to, by
    /// <see cref="ToolShortcut.Id"/>. macshot's <c>overlayToolShortcuts</c>.
    /// </summary>
    /// <remarks>
    /// Only what the user has changed. A shortcut missing from here keeps macshot's
    /// default, and one present but empty was taken off on purpose — which is why this
    /// cannot be a full table filled in from the defaults.
    /// </remarks>
    public IReadOnlyDictionary<string, string> ToolShortcuts { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Whether a button's tooltip also says which key does the same thing. macshot's
    /// <c>showToolShortcutsInTooltips</c>.
    /// </summary>
    /// <remarks>
    /// On by default, unlike macshot's: a shortcut nobody can discover is a shortcut
    /// nobody uses, and the tooltip is the only place the overlay could say so.
    /// </remarks>
    public bool ShowShortcutsInTooltips { get; init; } = true;

    /// <summary>
    /// Whether a shape picked in the size box outlives the capture it was picked on.
    /// macshot's <c>keepAspectRatio</c>.
    /// </summary>
    /// <remarks>
    /// Off by default, and the difference it makes is the difference between choosing
    /// 16 : 9 for this screenshot and working in 16 : 9. A shape that silently held over
    /// into every later capture would be a drag that refuses to be the size it is dragged.
    /// </remarks>
    public bool KeepAspectRatio { get; init; }

    /// <summary>
    /// The shape being held over, as width ÷ height, or 0 for none.
    /// macshot's <c>keepAspectRatioValue</c>.
    /// </summary>
    public double KeepAspectRatioValue { get; init; }

    /// <summary>
    /// Whether the size box reads in layout points rather than device pixels. macshot's
    /// <c>resolutionUnitIsPoints</c>.
    /// </summary>
    public bool ResolutionUnitIsPoints { get; init; }

    /// <summary>
    /// Whether taking a capture the usual way also opens it in the editor.
    /// macshot's <c>quickCaptureOpenEditor</c>.
    /// </summary>
    /// <remarks>
    /// Alongside whatever else is being done with it rather than instead: someone who
    /// wants every capture annotated still wants it copied, and an editor that swallowed
    /// the copy would make the setting cost something.
    /// </remarks>
    public bool QuickCaptureOpenEditor { get; init; }

    /// <summary>
    /// Whether macshot starts with Windows. macshot's <c>launchAtLogin</c>.
    /// </summary>
    /// <remarks>
    /// Stored here as well as in the registry so the setting travels with an exported
    /// settings file, and so the checkbox has something to show before the registry has
    /// been read. The registry is still the authority — the app writes one from the
    /// other on save.
    /// </remarks>
    public bool LaunchAtLogin { get; init; }

    /// <summary>
    /// Whether the notification-area icon is hidden. macshot's <c>hideMenuBarIcon</c>.
    /// </summary>
    /// <remarks>
    /// The shortcuts still work with the icon gone, which is the point: someone who
    /// captures by hotkey has no use for an icon sitting in the tray. Read once at
    /// startup, as macshot's is, because an icon that came and went as the checkbox was
    /// clicked would leave the tray reordering itself under the user's pointer.
    /// </remarks>
    public bool HideTrayIcon { get; init; }

    /// <summary>Which light or dark macshot's own windows are drawn in.</summary>
    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>
    /// Whether a recording carries the microphone. macshot's <c>recordMicAudio</c>,
    /// off by default for the same reason.
    /// </summary>
    public bool RecordMicAudio { get; init; }

    /// <summary>
    /// The drawing style the toolbar was last left on, as <c>#AARRGGBB</c>. This is
    /// remembered rather than configured — nobody opens a settings window to pick
    /// the colour of the next arrow — which is why it has no preferences UI. It is
    /// the counterpart of the macOS <c>currentStrokeWidth</c> family of defaults.
    /// </summary>
    public string AnnotationColor { get; init; } = AnnotationStyle.Default.Color.ToHex();

    public double AnnotationStrokeWidth { get; init; } = AnnotationStyle.Default.StrokeWidth;

    public LineStyle AnnotationLineStyle { get; init; } = LineStyle.Solid;

    /// <summary>Which ends the arrow tool draws.</summary>
    public ArrowStyle AnnotationArrowStyle { get; init; } = ArrowStyle.Filled;

    /// <summary>How far the rectangle tool rounds its corners, in frame pixels.</summary>
    public double AnnotationCornerRadius { get; init; }

    /// <summary>
    /// Whether new arrows point back the way they are drawn. macshot's
    /// <c>arrowReversed</c>, which it remembers between captures as it does every other
    /// tool setting.
    /// </summary>
    public bool AnnotationArrowReversed { get; init; }

    /// <summary>
    /// The halo laid under a mark, as hex, or empty for none. macshot's
    /// <c>annotationOutlineEnabled</c> and <c>savedOutlineColor</c> in one: a colour that
    /// cannot be read means off, so the two cannot disagree.
    /// </summary>
    public string AnnotationOutline { get; init; } = string.Empty;

    /// <summary>How big the text tool sets a label, in frame pixels.</summary>
    /// <remarks>
    /// Remembered apart from the stroke width because it is set apart from it: the two
    /// shared one number until the text tool grew its own controls, which meant sizing a
    /// label also resized the next arrow.
    /// </remarks>
    public double AnnotationFontSize { get; init; } = AnnotationStyle.DefaultFontSize;

    /// <summary>The face the text tool sets a label in, or empty for the system font.</summary>
    public string AnnotationFontFamily { get; init; } = string.Empty;

    /// <summary>Whether the text tool sets a label bold.</summary>
    public bool AnnotationBold { get; init; }

    /// <summary>The pill behind a label as <c>#AARRGGBB</c>, or empty for none.</summary>
    public string AnnotationTextBackground { get; init; } = string.Empty;

    /// <summary>The line around that pill as <c>#AARRGGBB</c>, or empty for none.</summary>
    public string AnnotationTextOutline { get; init; } = string.Empty;

    /// <summary>
    /// How much a freehand stroke is rounded off once it is finished. Smoothed by
    /// default: a path sampled from a mouse is a staircase, and nobody draws one on
    /// purpose.
    /// </summary>
    public PencilSmoothing PencilSmoothing { get; init; } = Annotations.PencilSmoothing.Smooth;

    /// <summary>
    /// How the censor tool covers what it is dragged over. Remembered like the drawing
    /// colour is, because it is the same kind of choice: the last one made is nearly
    /// always the next one wanted.
    /// </summary>
    public CensorMode CensorMode { get; init; } = Annotations.CensorMode.Pixelate;

    /// <summary>
    /// Offers the previous selection again on the next capture. Off by default: a
    /// selection that reappears where the last one was is a surprise until you know
    /// the setting exists.
    /// </summary>
    public bool RememberLastSelection { get; init; }

    /// <summary>
    /// Whether the overlay offers the window under the pointer as a region to take.
    /// </summary>
    /// <remarks>
    /// On by default, and toggled with Tab from the overlay rather than from the settings
    /// window — it is a thing you turn off in the middle of a capture, when the window
    /// being offered is not the region you want and the highlight is in the way. Kept here
    /// so that the answer survives the capture it was given during. With it off, a click
    /// that never became a drag takes the whole screen instead of a window.
    /// </remarks>
    public bool WindowSnapEnabled { get; init; } = true;

    /// <summary>
    /// Takes the instruction pill off the overlay, leaving what the overlay has to
    /// <em>report</em> — a sampled colour, a ruler's reading, a failure.
    /// </summary>
    /// <remarks>
    /// For someone who has read the instructions once and is now recording their screen
    /// or taking a picture of it, where a standing sentence in the middle of the display
    /// is in every capture they make.
    /// </remarks>
    public bool HideCaptureInstructions { get; init; }

    /// <summary>
    /// The region the last capture was taken from, in the frame space of
    /// <see cref="LastSelectionDisplay"/>. Null until one has been taken.
    /// </summary>
    public CaptureRegion? LastSelection { get; init; }

    /// <summary>
    /// Which display <see cref="LastSelection"/> was drawn on, by device name.
    /// </summary>
    /// <remarks>
    /// Stored alongside the region because a rectangle alone means nothing once the
    /// displays have been rearranged: restoring it on a monitor that is no longer
    /// there, or in a different place, would put the selection somewhere the user
    /// never drew it. The overlay offers it back only on the display it came from.
    /// </remarks>
    public string? LastSelectionDisplay { get; init; }

    /// <summary>
    /// How long the delayed capture counts down for, so whatever is being captured
    /// can be opened first.
    /// </summary>
    /// <remarks>
    /// This is the length of the wait, not whether there is one. A delay in front of
    /// every capture would make the shortcut useless for the ordinary case, so the
    /// delay is asked for by name from the notification-area menu and the shortcut
    /// stays immediate — which is also why there is no "off" value.
    /// </remarks>
    public int DelaySeconds { get; init; } = 5;

    /// <summary>How many past captures are kept. Zero turns history off entirely.</summary>
    public int HistorySize { get; init; } = 20;

    /// <summary>
    /// Keeps every capture, whatever <see cref="HistorySize"/> says.
    /// </summary>
    /// <remarks>
    /// macshot's <c>historyUnlimited</c>, which overrides its own count the same way —
    /// <c>ScreenshotHistory.swift:40–45</c>. Off by default: an archive that grows
    /// without end is a choice, and one nobody should be given by accident.
    /// </remarks>
    public bool HistoryUnlimited { get; init; }

    /// <summary>
    /// How many past captures to keep, once the unlimited switch has had its say.
    /// </summary>
    /// <remarks>
    /// Unlimited wins over a count of zero. Someone who has asked to keep everything
    /// has not asked for history to be off, and reading it the other way would turn the
    /// switch into one that silently deletes.
    /// </remarks>
    public int EffectiveHistorySize => HistoryUnlimited ? int.MaxValue : HistorySize;

    /// <summary>
    /// The three global shortcuts, written the way <see cref="HotkeyBinding"/> reads
    /// them.
    /// </summary>
    /// <remarks>
    /// Stored as text rather than as a modifier mask and a key code, because the
    /// settings file is meant to be hand-editable and <c>Ctrl+Shift+X</c> says what it
    /// means where <c>{"modifiers":6,"key":88}</c> does not. Normalizing rewrites
    /// whatever is in the file through the parser, so a shortcut that cannot be
    /// registered becomes the default here rather than at the point of registering.
    /// </remarks>
    public string CaptureAreaHotkey { get; init; } = HotkeyBinding.CaptureArea.ToString();

    public string CaptureAllScreensHotkey { get; init; } = HotkeyBinding.CaptureAllScreens.ToString();

    public string RecordScreenHotkey { get; init; } = HotkeyBinding.RecordScreen.ToString();

    public HotkeyBinding CaptureAreaBinding =>
        HotkeyBinding.ParseOrDefault(CaptureAreaHotkey, HotkeyBinding.CaptureArea);

    public HotkeyBinding CaptureAllScreensBinding =>
        HotkeyBinding.ParseOrDefault(CaptureAllScreensHotkey, HotkeyBinding.CaptureAllScreens);

    public HotkeyBinding RecordScreenBinding =>
        HotkeyBinding.ParseOrDefault(RecordScreenHotkey, HotkeyBinding.RecordScreen);

    /// <summary>
    /// Writes a step-by-step trace next to this file, for diagnosing a fault that
    /// cannot be reproduced on the machine the code was written on.
    /// </summary>
    /// <remarks>
    /// Off by default. Everything worth tracing sits on a path that runs at
    /// pointer-move rates, and a log nobody asked for is a file growing on their disk.
    /// </remarks>
    public bool VerboseLogging { get; init; }

    /// <summary>
    /// Whether macshot looks for a new version on its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On by default, which is macshot's default for Sparkle's
    /// <c>SUEnableAutomaticChecks</c>: a screenshot tool that never mentions an update
    /// is one that stays on a version with a bug in it.
    /// </para>
    /// <para>
    /// ⚠️ **Nothing reads this yet.** The port is not distributed, so there is no feed
    /// for an updater to check. The setting exists now so that the preferences window
    /// does not have to be rearranged when the updater arrives, and so a user who turns
    /// it off before then stays turned off.
    /// </para>
    /// </remarks>
    public bool AutomaticUpdateChecks { get; init; } = true;

    /// <summary>
    /// Whether those checks include beta releases.
    /// </summary>
    /// <remarks>
    /// macshot's <c>betaUpdatesEnabled</c>, which selects Sparkle's <c>beta</c> channel.
    /// Off by default: a beta is something a user opts into.
    /// </remarks>
    public bool BetaUpdates { get; init; }

    /// <summary>
    /// What language macshot itself is shown in, or <c>"system"</c> to follow Windows.
    /// </summary>
    /// <remarks>
    /// macshot's <c>appLanguage</c>, with macshot's default. A code this build has no
    /// strings for resolves to English rather than being refused, so a settings file
    /// from a newer version cannot leave the interface blank.
    /// </remarks>
    public string Language { get; init; } = AppLanguages.System;

    /// <summary>What recognized text is translated into, as an ISO-639-1 code.</summary>
    public string TranslateTargetLanguage { get; init; } = TranslationLanguages.DefaultCode;

    /// <summary>
    /// The tools taken off the toolbar, by name. Empty means all of them are there.
    /// </summary>
    /// <remarks>
    /// Stored as what is hidden rather than what is shown, so a version that adds a tool
    /// offers it to everyone instead of hiding it from every existing user — a list of
    /// what is wanted, written before the tool existed, cannot contain it.
    /// </remarks>
    public IReadOnlyList<string> HiddenTools { get; init; } = [];

    /// <summary>
    /// The buttons after the tools, and on the action strip, that the user has taken off —
    /// by <see cref="ToolbarCustomAction.Id"/>. macshot's <c>enabledActions</c>, inverted.
    /// </summary>
    /// <remarks>
    /// Stored as what is hidden for the same reason <see cref="HiddenTools"/> is: macshot
    /// keeps the list the other way round and has to notice each new action and append it
    /// to everyone's stored list, which is work this side does not have to do.
    /// </remarks>
    public IReadOnlyList<string> HiddenActions { get; init; } = [];

    /// <summary>How many colours the picker keeps for the user's own. macshot's seven.</summary>
    public const int CustomColorSlots = 7;

    /// <summary>
    /// The user's own colours, as <c>#AARRGGBB</c>, in the order the picker shows them.
    /// </summary>
    /// <remarks>
    /// macshot's saveable slots, which are how a brand colour or a colour sampled off a
    /// screenshot survives past the capture it was picked on. Fewer than
    /// <see cref="CustomColorSlots"/> entries means the rest are empty; a longer list is
    /// trimmed on read, so a hand-edited file cannot grow the picker.
    /// </remarks>
    public IReadOnlyList<string> CustomColors { get; init; } = [];

    /// <summary>The toolbar's own colours, as <c>#AARRGGBB</c>.</summary>
    /// <remarks>
    /// The toolbar sits over a screenshot rather than in a window, so it cannot follow the
    /// system theme without disappearing into half the captures anyone takes. These are
    /// how someone who wants it to look like something else says so.
    /// </remarks>
    public string ToolbarBackgroundColor { get; init; } = ToolbarColors.DefaultBackground.ToHex();

    public string ToolbarAccentColor { get; init; } = ToolbarColors.DefaultAccent.ToHex();

    public string ToolbarIconColor { get; init; } = ToolbarColors.DefaultIcon.ToHex();

    /// <summary>The three toolbar colours, with anything unreadable back at its default.</summary>
    public ToolbarColors ToToolbarColors() => new(
        Color(ToolbarBackgroundColor, ToolbarColors.DefaultBackground),
        Color(ToolbarAccentColor, ToolbarColors.DefaultAccent),
        Color(ToolbarIconColor, ToolbarColors.DefaultIcon));

    /// <summary>The tools to put on the toolbar, in the order the toolbar keeps them.</summary>
    public IReadOnlyCollection<AnnotationTool> EnabledTools()
    {
        var hidden = HiddenTools
            .Select(name => Enum.TryParse<AnnotationTool>(name, ignoreCase: true, out var tool) ? tool : (AnnotationTool?)null)
            .Where(tool => tool is not null)
            .Select(tool => tool!.Value)
            .ToHashSet();

        return [.. ToolbarActions.ToolOrder.Where(tool => !hidden.Contains(tool))];
    }

    /// <summary>
    /// The hidden buttons, keeping only names this build has a button for.
    /// </summary>
    /// <remarks>
    /// Unlike the tools, there is no floor here: every one of these can be hidden at once
    /// and what is left is still a working toolbar, because Cancel, Copy and Save cannot
    /// be hidden at all.
    /// </remarks>
    private IReadOnlyList<string> SaneHiddenActions() =>
    [
        .. HiddenActions
            .Where(id => ToolbarCustomActions.Find(id) is not null)
            .Distinct(StringComparer.Ordinal),
    ];

    private IReadOnlyList<string> SaneHiddenTools()
    {
        var known = HiddenTools
            .Where(name => Enum.TryParse<AnnotationTool>(name, ignoreCase: true, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return (this with { HiddenTools = known }).EnabledTools().Count > 0 ? known : [];
    }

    /// <summary>
    /// The chosen shortcuts, keeping only the ones this build can honour.
    /// </summary>
    /// <remarks>
    /// An entry naming nothing is dropped, but an entry naming something with an empty
    /// key is kept: that is a user who took a default shortcut off, and dropping it would
    /// hand the key straight back to them.
    /// </remarks>
    private IReadOnlyDictionary<string, string> SaneToolShortcuts()
    {
        if (ToolShortcuts.Count == 0)
        {
            return ToolShortcuts;
        }

        var sane = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var shortcut in Annotations.ToolShortcuts.All)
        {
            if (ToolShortcuts.TryGetValue(shortcut.Id, out var key))
            {
                sane[shortcut.Id] = Annotations.ToolShortcuts.Normalize(key);
            }
        }

        return sane;
    }

    private static AnnotationColor Color(string hex, AnnotationColor fallback) =>
        Annotations.AnnotationColor.TryParseHex(hex, out var parsed) ? parsed : fallback;

    /// <summary>
    /// The saved colours, at the picker's own length: an unreadable one becomes an empty
    /// slot rather than being dropped, so the colours after it stay in their squares.
    /// </summary>
    private IReadOnlyList<string> SaneCustomColors()
    {
        if (CustomColors.Count == 0)
        {
            return [];
        }

        var slots = new string[Math.Min(CustomColors.Count, CustomColorSlots)];
        for (var index = 0; index < slots.Length; index++)
        {
            slots[index] = Annotations.AnnotationColor.TryParseHex(CustomColors[index], out var parsed)
                ? parsed.ToHex()
                : string.Empty;
        }

        return slots;
    }

    /// <summary>Which of <see cref="BeautifyRenderer.Styles"/> the Beautify action uses.</summary>
    public int BeautifyStyleIndex { get; init; }

    public double BeautifyPadding { get; init; } = BeautifyOptions.Default.Padding;

    public double BeautifyCornerRadius { get; init; } = BeautifyOptions.Default.CornerRadius;

    public double BeautifyShadowRadius { get; init; } = BeautifyOptions.Default.ShadowRadius;

    public BeautifyOptions ToBeautifyOptions() => new BeautifyOptions(
        BeautifyStyleIndex,
        BeautifyPadding,
        BeautifyCornerRadius,
        BeautifyShadowRadius).Normalized();

    /// <summary>
    /// Where the Upload button sends a capture. macshot's <c>uploadProvider</c>.
    /// </summary>
    /// <remarks>
    /// Present in the offline build's settings record as well as the normal one. The
    /// value is inert there — nothing reads it, because the code that would is compiled
    /// out — and keeping the property means a settings file written by the normal build
    /// still round-trips through the offline one instead of losing a setting on the way
    /// through.
    /// </remarks>
    public Upload.UploadProvider UploadProvider { get; init; } = Upload.UploadProvider.Imgbb;

    /// <summary>
    /// Whether the Upload button asks first. macshot's <c>uploadConfirmEnabled</c>, and
    /// off by default there as here — the button is already a deliberate act.
    /// </summary>
    public bool UploadConfirm { get; init; }

    /// <summary>
    /// The user's own imgbb key, or empty for the shared one — macshot's <c>imgbbAPIKey</c>.
    /// </summary>
    public string ImgbbApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Everything that has been uploaded to imgbb, newest last, each with the link that
    /// takes it down again. macshot's <c>imgbbUploads</c>.
    /// </summary>
    public IReadOnlyList<UploadHistoryEntry> ImgbbUploads { get; init; } = [];

    /// <summary>
    /// The address of the signed-in Google account, shown in the settings window so the
    /// user can tell which one they are uploading into. macshot's <c>gdriveUserEmail</c>.
    /// The tokens are not here: they live in their own file, as macshot's do.
    /// </summary>
    public string GoogleDriveAccount { get; init; } = string.Empty;

    public string S3Endpoint { get; init; } = string.Empty;

    public string S3Region { get; init; } = "auto";

    public string S3Bucket { get; init; } = string.Empty;

    public string S3AccessKeyId { get; init; } = string.Empty;

    public string S3SecretAccessKey { get; init; } = string.Empty;

    public string S3PublicUrlBase { get; init; } = string.Empty;

    public string S3PathPrefix { get; init; } = string.Empty;

    /// <summary>The S3 settings as the signer wants them.</summary>
    /// <remarks>
    /// Gathered here rather than passed as seven arguments, and gathered from this record
    /// rather than stored as one, because every other setting in the file is a scalar and
    /// a nested object would be the only thing a hand-editor had to indent.
    /// </remarks>
    public S3Settings ToS3Settings() => new(
        S3Endpoint.Trim(),
        string.IsNullOrWhiteSpace(S3Region) ? "auto" : S3Region.Trim(),
        S3Bucket.Trim(),
        S3AccessKeyId.Trim(),
        S3SecretAccessKey.Trim(),
        S3PublicUrlBase.Trim(),
        S3PathPrefix.Trim());

    /// <summary>
    /// The remembered selection, but only when it is worth offering: the setting is
    /// on, it was taken on the display being asked about, and it still fits there.
    /// </summary>
    /// <remarks>
    /// Fitting is checked rather than clamped. A selection that has to be squashed to
    /// fit a display it no longer matches is not the one the user drew, and offering
    /// a wrong rectangle is worse than offering none.
    /// </remarks>
    public CaptureRegion? RememberedSelectionFor(string displayDeviceName, int width, int height)
    {
        if (!RememberLastSelection
            || LastSelection is not { } selection
            || !string.Equals(LastSelectionDisplay, displayDeviceName, StringComparison.Ordinal))
        {
            return null;
        }

        var fits = selection.X >= 0
            && selection.Y >= 0
            && selection.Right <= width
            && selection.Bottom <= height
            && selection.Width >= MinRememberedSelection
            && selection.Height >= MinRememberedSelection;

        return fits ? selection : null;
    }

    /// <summary>
    /// Records the region a capture was taken from, or forgets it when it is too
    /// small to have been chosen deliberately.
    /// </summary>
    public CaptureSettings WithLastSelection(CaptureRegion selection, string displayDeviceName)
    {
        if (selection.Width < MinRememberedSelection || selection.Height < MinRememberedSelection)
        {
            return this;
        }

        return this with { LastSelection = selection, LastSelectionDisplay = displayDeviceName };
    }

    public AnnotationStyle ToAnnotationStyle()
    {
        var color = Annotations.AnnotationColor.TryParseHex(AnnotationColor, out var parsed)
            ? parsed
            : AnnotationStyle.Default.Color;
        return new AnnotationStyle(
            color,
            Math.Clamp(AnnotationStrokeWidth, MinStrokeWidth, MaxStrokeWidth),
            AnnotationLineStyle,
            ArrowStyle: AnnotationArrowStyle,
            CornerRadius: Math.Clamp(AnnotationCornerRadius, 0, MaxCornerRadius),
            CensorMode: CensorMode)
        {
            FontSize = Math.Clamp(
                AnnotationFontSize,
                AnnotationStyle.MinFontSize,
                AnnotationStyle.MaxFontSize),
            FontFamily = AnnotationFontFamily,
            Bold = AnnotationBold,
            ArrowReversed = AnnotationArrowReversed,
            Outline = Annotations.AnnotationColor.TryParseHex(AnnotationOutline, out var halo)
                ? halo
                : null,
            TextBackground = Annotations.AnnotationColor.TryParseHex(AnnotationTextBackground, out var fill)
                ? fill
                : null,
            TextOutline = Annotations.AnnotationColor.TryParseHex(AnnotationTextOutline, out var edge)
                ? edge
                : null,
        };
    }

    public CaptureSettings WithAnnotationStyle(AnnotationStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        return this with
        {
            AnnotationColor = style.Color.ToHex(),
            AnnotationStrokeWidth = style.StrokeWidth,
            AnnotationLineStyle = style.LineStyle,
            AnnotationArrowStyle = style.ArrowStyle,
            AnnotationCornerRadius = style.CornerRadius,
            CensorMode = style.CensorMode,
            AnnotationFontSize = style.FontSize,
            AnnotationFontFamily = style.FontFamily,
            AnnotationBold = style.Bold,
            AnnotationArrowReversed = style.ArrowReversed,
            AnnotationOutline = style.Outline?.ToHex() ?? string.Empty,
            AnnotationTextBackground = style.TextBackground?.ToHex() ?? string.Empty,
            AnnotationTextOutline = style.TextOutline?.ToHex() ?? string.Empty,
        };
    }

    /// <summary>
    /// Clamps every field into range. The settings file is user-editable and can
    /// also be stale after an upgrade, so nothing downstream may assume it is sane;
    /// this is the one place that repairs it.
    /// </summary>
    public CaptureSettings Normalized()
    {
        return this with
        {
            Format = Enum.IsDefined(Format) ? Format : CaptureImageFormat.Png,
            RecordingFormat = Enum.IsDefined(RecordingFormat) ? RecordingFormat : RecordingFormat.Mp4,
            UploadProvider = Enum.IsDefined(UploadProvider) ? UploadProvider : Upload.UploadProvider.Imgbb,

            // Trimmed rather than validated. Every one of these is pasted from a console
            // that likes to add a newline, and a trailing space in an access key is a 403
            // that reads as wrong credentials.
            ImgbbApiKey = ImgbbApiKey.Trim(),
            S3Endpoint = S3Endpoint.Trim(),
            S3Region = string.IsNullOrWhiteSpace(S3Region) ? "auto" : S3Region.Trim(),
            S3Bucket = S3Bucket.Trim(),
            S3AccessKeyId = S3AccessKeyId.Trim(),
            S3SecretAccessKey = S3SecretAccessKey.Trim(),
            S3PublicUrlBase = S3PublicUrlBase.Trim(),
            S3PathPrefix = S3PathPrefix.Trim(),
            Quality = Math.Clamp(Quality, MinQuality, MaxQuality),
            SaveDirectory = string.IsNullOrWhiteSpace(SaveDirectory) ? null : SaveDirectory.Trim(),
            FilenameTemplate = string.IsNullOrWhiteSpace(FilenameTemplate)
                ? Output.FilenameTemplate.Default
                : FilenameTemplate.Trim(),
            RecordingFilenameTemplate = string.IsNullOrWhiteSpace(RecordingFilenameTemplate)
                ? Output.FilenameTemplate.DefaultRecording
                : RecordingFilenameTemplate.Trim(),
            ThumbnailSeconds = Math.Clamp(ThumbnailSeconds, MinThumbnailSeconds, MaxThumbnailSeconds),
            ThumbnailScale = ThumbnailPlacement.SanePreviewScale(ThumbnailScale),
            RecordingFrameRate = Math.Clamp(
                RecordingFrameRate,
                RecordingPlan.MinFrameRate,
                RecordingPlan.MaxFrameRate),
            GifFrameRate = Math.Clamp(
                GifFrameRate,
                GifRecordingPlan.MinFrameRate,
                GifRecordingPlan.MaxFrameRate),

            // Round-tripped through the parser so an unreadable colour becomes the
            // default here, rather than silently at every point that draws.
            AnnotationColor = (Annotations.AnnotationColor.TryParseHex(AnnotationColor, out var color)
                ? color
                : AnnotationStyle.Default.Color).ToHex(),
            AnnotationStrokeWidth = double.IsFinite(AnnotationStrokeWidth)
                ? Math.Clamp(AnnotationStrokeWidth, MinStrokeWidth, MaxStrokeWidth)
                : AnnotationStyle.Default.StrokeWidth,
            AnnotationFontSize = double.IsFinite(AnnotationFontSize)
                ? Math.Clamp(AnnotationFontSize, AnnotationStyle.MinFontSize, AnnotationStyle.MaxFontSize)
                : AnnotationStyle.DefaultFontSize,
            AnnotationFontFamily = AnnotationFontFamily.Trim(),

            // Empty means "no pill" and "no line round it", so an unreadable colour is
            // turned off rather than defaulted — a label that silently grew a background
            // nobody asked for is worse than one that lost the one they did.
            AnnotationTextBackground = Annotations.AnnotationColor.TryParseHex(AnnotationTextBackground, out var pill)
                ? pill.ToHex()
                : string.Empty,
            AnnotationTextOutline = Annotations.AnnotationColor.TryParseHex(AnnotationTextOutline, out var rim)
                ? rim.ToHex()
                : string.Empty,
            AnnotationLineStyle = Enum.IsDefined(AnnotationLineStyle) ? AnnotationLineStyle : LineStyle.Solid,
            AnnotationArrowStyle = Enum.IsDefined(AnnotationArrowStyle) ? AnnotationArrowStyle : ArrowStyle.Filled,
            AnnotationCornerRadius = double.IsFinite(AnnotationCornerRadius)
                ? Math.Clamp(AnnotationCornerRadius, 0, MaxCornerRadius)
                : 0,
            PencilSmoothing = Enum.IsDefined(PencilSmoothing)
                ? PencilSmoothing
                : Annotations.PencilSmoothing.Smooth,
            CensorMode = Enum.IsDefined(CensorMode) ? CensorMode : Annotations.CensorMode.Pixelate,
            DelaySeconds = Math.Clamp(DelaySeconds, MinDelaySeconds, MaxDelaySeconds),
            HistorySize = Math.Clamp(HistorySize, 0, MaxHistorySize),

            // Round-tripped through the parser, so a shortcut the file cannot express
            // becomes the default here rather than leaving macshot with no way to
            // capture at all.
            // Normalized rather than passed through: a code the table does not hold
            // would be sent to the service and refused, so the capture would come back
            // with an error where a translation was asked for.
            // Not resolved here, only tidied: "system" has to survive into the file, or
            // a user who follows Windows would be pinned to whatever they had the day
            // they last saved.
            Language = string.IsNullOrWhiteSpace(Language) ? AppLanguages.System : Language.Trim(),

            TranslateTargetLanguage = TranslationLanguages.Normalize(TranslateTargetLanguage),

            CaptureAreaHotkey = CaptureAreaBinding.ToString(),
            CaptureAllScreensHotkey = CaptureAllScreensBinding.ToString(),
            RecordScreenHotkey = RecordScreenBinding.ToString(),

            // A selection with no display to belong to cannot be placed, and a display
            // with no selection has nothing to place, so neither survives alone.
            LastSelection = string.IsNullOrWhiteSpace(LastSelectionDisplay) ? null : Sane(LastSelection),
            LastSelectionDisplay = LastSelection is null || string.IsNullOrWhiteSpace(LastSelectionDisplay)
                ? null
                : LastSelectionDisplay.Trim(),
            // Names the enum does not know are dropped rather than kept: they can only be
            // a typo or a tool that no longer exists, and either way they hide nothing.
            // A file that hides every tool is treated as hiding none — a toolbar with no
            // tools on it is not a preference, it is a broken window.
            HiddenTools = SaneHiddenTools(),
            HiddenActions = SaneHiddenActions(),

            // A held shape that is not a positive number is not a shape. Kept separate
            // from the switch so that turning keep-ratio off and on again does not lose
            // what was being held.
            KeepAspectRatioValue = double.IsFinite(KeepAspectRatioValue) && KeepAspectRatioValue > 0
                ? KeepAspectRatioValue
                : 0,

            // Only shortcuts this build has, on keys it can actually match. A binding for
            // a tool that no longer exists is unreachable, and one holding more than a
            // single character could never fire — both would sit in the settings window
            // looking assigned.
            ToolShortcuts = SaneToolShortcuts(),

            // Trimmed to the slots the picker has and to the entries it can draw. An
            // empty string is a slot nobody has filled yet and is kept as one, because
            // dropping it would shuffle every later colour into a different square.
            CustomColors = SaneCustomColors(),

            // Round-tripped through the parser so an unreadable colour becomes the default
            // here rather than silently wherever the toolbar is drawn.
            ToolbarBackgroundColor = Color(ToolbarBackgroundColor, ToolbarColors.DefaultBackground).ToHex(),
            ToolbarAccentColor = Color(ToolbarAccentColor, ToolbarColors.DefaultAccent).ToHex(),
            ToolbarIconColor = Color(ToolbarIconColor, ToolbarColors.DefaultIcon).ToHex(),
            BeautifyStyleIndex = Math.Clamp(BeautifyStyleIndex, 0, Math.Max(0, BeautifyRenderer.Styles.Count - 1)),
            BeautifyPadding = Clamp(BeautifyPadding, BeautifyOptions.Default.Padding, 0, 0.5),
            BeautifyCornerRadius = Clamp(BeautifyCornerRadius, BeautifyOptions.Default.CornerRadius, 0, 0.5),
            BeautifyShadowRadius = Clamp(BeautifyShadowRadius, BeautifyOptions.Default.ShadowRadius, 0, 0.25),
        };
    }

    /// <summary>
    /// A remembered selection, or null when the file holds one that cannot be drawn:
    /// negative, infinite, or with no area.
    /// </summary>
    private static CaptureRegion? Sane(CaptureRegion? selection)
    {
        if (selection is not { } region)
        {
            return null;
        }

        var finite = double.IsFinite(region.X)
            && double.IsFinite(region.Y)
            && double.IsFinite(region.Width)
            && double.IsFinite(region.Height);

        return finite && !region.IsEmpty ? region : null;
    }

    /// <summary>
    /// Clamps a fraction, falling back to <paramref name="fallback"/> for the values
    /// clamping cannot repair — a NaN is not a number that is merely out of range.
    /// </summary>
    private static double Clamp(double value, double fallback, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}
