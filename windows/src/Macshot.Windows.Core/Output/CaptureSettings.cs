using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Core.Input;
using Macshot.Windows.Core.Localization;
using Macshot.Windows.Core.Recognition;

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

    private IReadOnlyList<string> SaneHiddenTools()
    {
        var known = HiddenTools
            .Where(name => Enum.TryParse<AnnotationTool>(name, ignoreCase: true, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return (this with { HiddenTools = known }).EnabledTools().Count > 0 ? known : [];
    }

    private static AnnotationColor Color(string hex, AnnotationColor fallback) =>
        Annotations.AnnotationColor.TryParseHex(hex, out var parsed) ? parsed : fallback;

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
            CensorMode: CensorMode);
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
            Quality = Math.Clamp(Quality, MinQuality, MaxQuality),
            SaveDirectory = string.IsNullOrWhiteSpace(SaveDirectory) ? null : SaveDirectory.Trim(),
            FilenameTemplate = string.IsNullOrWhiteSpace(FilenameTemplate)
                ? Output.FilenameTemplate.Default
                : FilenameTemplate.Trim(),
            RecordingFilenameTemplate = string.IsNullOrWhiteSpace(RecordingFilenameTemplate)
                ? Output.FilenameTemplate.DefaultRecording
                : RecordingFilenameTemplate.Trim(),
            ThumbnailSeconds = Math.Clamp(ThumbnailSeconds, MinThumbnailSeconds, MaxThumbnailSeconds),
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
