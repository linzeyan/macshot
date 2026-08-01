using System.Diagnostics;
using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Input;
using Macshot.Windows.Core.Localization;
using Macshot.Windows.Core.Output;
using Macshot.Windows.Services;
using Macshot.Windows.Toolbar;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;
using static Macshot.Windows.Services.Localization;

namespace Macshot.Windows;

/// <summary>
/// The settings window, the counterpart of the macOS
/// <c>PreferencesWindowController</c>.
/// </summary>
/// <remarks>
/// <para>
/// A change takes effect as it is made, which is what the macOS window does — it has no
/// Save button either. The alternative is a window whose contents mean nothing until a
/// button is found, and whose button means nothing once it has been pressed.
/// </para>
/// <para>
/// The controls are wired by hand rather than bound, because <see cref="CaptureSettings"/>
/// is an immutable record with no change notification and adding one purely for this
/// window would put UI concerns into Core.
/// </para>
/// </remarks>
public sealed partial class PreferencesWindow : Window
{
    /// <summary>The macOS settings window's content size, which this one is.</summary>
    private const double WidthDips = 620;

    private const double HeightDips = 520;

    /// <summary>
    /// How long a change waits before it is written.
    /// </summary>
    /// <remarks>
    /// Dragging the quality slider is one gesture and hundreds of notifications. Each
    /// write re-reads the file into the running app and hands the global shortcuts back to
    /// Windows to take again, so writing every notification would spend a drag doing that
    /// — and a shortcut that momentarily belongs to nobody is one a keypress can miss.
    /// Short enough that letting go of a control and closing the window keeps the change.
    /// </remarks>
    private static readonly TimeSpan WriteDelay = TimeSpan.FromMilliseconds(250);

    private readonly SettingsStore _settings;

    /// <summary>Collects a burst of changes into one write. See <see cref="WriteDelay"/>.</summary>
    private readonly DispatcherTimer _write = new() { Interval = WriteDelay };

    /// <summary>
    /// True while the controls are being filled in from the stored settings, so the
    /// notifications that causes are not mistaken for the user changing something.
    /// </summary>
    private bool _loading;

    /// <summary>Whether a change has been made that is not on disk yet.</summary>
    private bool _pending;

    /// <summary>One tick box per tool, in the order the toolbar keeps them.</summary>
    private readonly Dictionary<AnnotationTool, CheckBox> _toolToggles = [];

    private readonly ColorChoice _toolbarBackground = new("Background");
    private readonly ColorChoice _toolbarAccent = new("Accent");
    private readonly ColorChoice _toolbarIcon = new("Icons");

    public PreferencesWindow(SettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        InitializeComponent();
        // Every string in the XAML is already the English text macshot keys by,
        // so the page is translated in place rather than written twice.
        this.Localize();

        // The markup selects the first item, which happens while the pages it switches
        // between are still being built — so the handler that would have shown it declined
        // to, and the first page has to be shown from here instead.
        ShowPage(Tabs.SelectedItem as ListViewItem);
        BuildToolsPage();
        Load(_settings.Current);
        PlaceOnScreen();

        _write.Tick += (_, _) => Persist();

        CaptureAreaHotkeyBox.BindingChanged += Setting_Changed;
        CaptureAllScreensHotkeyBox.BindingChanged += Setting_Changed;
        RecordScreenHotkeyBox.BindingChanged += Setting_Changed;

        // A change still waiting out its delay when the window goes is a change the user
        // made and watched take effect on screen.
        Closed += (_, _) => Persist();
    }

    /// <summary>
    /// Builds the parts of the Tools page that come from the toolbar rather than from the
    /// markup, so a tool added later appears here without this page being edited.
    /// </summary>
    private void BuildToolsPage()
    {
        foreach (var tool in ToolbarActions.ToolOrder)
        {
            // 13 to match the markup's rows rather than WinUI's 14: these sit in the same
            // column as the tick boxes the markup declares.
            var toggle = new CheckBox { Content = ToolbarActions.Tooltip(tool), MinWidth = 0, FontSize = 13 };
            toggle.Checked += Setting_Changed;
            toggle.Unchecked += Setting_Changed;
            _toolToggles[tool] = toggle;
            ToolToggles.Children.Add(toggle);
        }

        foreach (var choice in new[] { _toolbarBackground, _toolbarAccent, _toolbarIcon })
        {
            choice.Changed += Setting_Changed;
            ToolbarColorRow.Children.Add(choice);
        }
    }

    /// <summary>
    /// Takes a change the moment it is made, after <see cref="WriteDelay"/>.
    /// </summary>
    /// <remarks>
    /// One handler for every control wired in code, whatever its notification signature:
    /// this window writes all of its pages at once, so which control changed makes no
    /// difference to what happens next. Both parameters are the widest and most nullable
    /// they can be, which is what lets one method stand in for every event's delegate.
    /// </remarks>
    private void Setting_Changed(object? sender, object? args) => Apply();

    private void Setting_Toggled(object sender, RoutedEventArgs e) => Apply();

    private void Setting_NumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => Apply();

    private void Setting_SliderChanged(object sender, RangeBaseValueChangedEventArgs e) => Apply();

    private void Setting_SelectionChanged(object sender, SelectionChangedEventArgs e) => Apply();

    /// <summary>
    /// Takes what a text box holds when the focus leaves it, rather than as it is typed.
    /// </summary>
    /// <remarks>
    /// A filename template is briefly nonsense on the way to being right — <c>{yyy</c> is
    /// three keystrokes into <c>{yyyy}</c> — and storing each of those would put a capture
    /// taken mid-edit under a name nobody chose. Leaving the field is the user saying they
    /// are done with it.
    /// </remarks>
    private void Setting_LostFocus(object sender, RoutedEventArgs e) => Apply();

    /// <summary>Notes that something changed, and starts the wait before it is written.</summary>
    private void Apply()
    {
        if (_loading)
        {
            return;
        }

        _pending = true;
        _write.Stop();
        _write.Start();
    }

    /// <summary>Writes every page, if anything is waiting to be written.</summary>
    private void Persist()
    {
        _write.Stop();

        if (!_pending)
        {
            return;
        }

        // The recorder cannot produce an unusable shortcut, but a hand-edited settings
        // file can, and this window shows what the file held. Refused rather than
        // repaired: normalizing would quietly put the default back, and a shortcut
        // silently reverting to Ctrl+Shift+X reads as macshot ignoring what was set.
        var unreadable = new[]
        {
            CaptureAreaHotkeyBox.Binding,
            CaptureAllScreensHotkeyBox.Binding,
            RecordScreenHotkeyBox.Binding,
        }.Where(text => !HotkeyBinding.TryParse(text, out _)).ToArray();

        if (unreadable.Length > 0)
        {
            StatusText.Text = $"Not a shortcut: {string.Join(", ", unreadable)}. Click it and press the keys — nothing is being kept until then.";
            return;
        }

        try
        {
            _settings.Save(Collect());
        }
        // Everything, not only the file system failures. This runs from a timer tick with
        // nobody above it to catch anything that escapes, and a preference that cannot be
        // stored is never a reason to take the app down.
        catch (Exception exception)
        {
            StatusText.Text = $"Could not save preferences: {exception.Message}";
            return;
        }

        _pending = false;
        StatusText.Text = string.Empty;
    }

    /// <summary>
    /// Shows the chosen page. All six exist at once and one is visible: a Frame would
    /// rebuild the page on every click, and a change on any page writes every page.
    /// </summary>
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // The first item is selected in the markup, so this can fire while the tree is
        // still being built and before the pages it switches between exist.
        if (GeneralPage is null)
        {
            return;
        }

        ShowPage(Tabs.SelectedItem as ListViewItem);
    }

    private void ShowPage(ListViewItem? item)
    {
        var chosen = item?.Tag as string;

        foreach (var (tag, page) in Pages())
        {
            page.Visibility = tag == chosen ? Visibility.Visible : Visibility.Collapsed;
        }

        // The title says which page, as the macOS window's does. Six pages of settings
        // named only "Settings" is a window whose title bar stops meaning anything the
        // moment the user is looking for one of them in a screenshot or a taskbar. Taken
        // from the tag rather than from the tab, whose content is an icon and a caption.
        Title = chosen is null
            ? $"{BuildVariant.DisplayName} Settings"
            : $"{BuildVariant.DisplayName} Settings — {char.ToUpperInvariant(chosen[0])}{chosen[1..]}";
    }

    private IEnumerable<(string Tag, FrameworkElement Page)> Pages() =>
    [
        ("general", GeneralPage),
        ("capture", CapturePage),
        ("shortcuts", ShortcutsPage),
        ("tools", ToolsPage),
        ("recording", RecordingPage),
        ("about", AboutPage),
    ];

    /// <summary>
    /// Opens at a size the content fits in, in the middle of the primary display.
    /// </summary>
    /// <remarks>
    /// WinUI's default is a small cascaded window, so the first thing anyone does with
    /// macshot's preferences would be to drag it bigger before a single setting can be
    /// read. Centred rather than cascaded because macshot has no other window for this
    /// one to cascade from — it would appear near the top-left corner for no reason.
    /// </remarks>
    private void PlaceOnScreen()
    {
        var monitor = MonitorEnumerator.Enumerate().Layout.Primary;
        var width = (int)(WidthDips * monitor.Scale);
        var height = (int)(HeightDips * monitor.Scale);

        var appWindow = this.GetAppWindow();
        appWindow.UseAppIcon();
        appWindow.MoveAndResize(new RectInt32(
            (int)(monitor.WorkArea.X + ((monitor.WorkArea.Width - width) / 2)),
            (int)(monitor.WorkArea.Y + ((monitor.WorkArea.Height - height) / 2)),
            width,
            height));
    }

    private void Load(CaptureSettings settings)
    {
        // Filling a control notifies exactly as a user changing it does, and every one of
        // these would otherwise write back what was just read.
        _loading = true;
        try
        {
            Fill(settings);
        }
        finally
        {
            _loading = false;
        }
    }

    private void Fill(CaptureSettings settings)
    {
        FormatBox.ItemsSource = Enum.GetValues<CaptureImageFormat>().Select(format => format.ToString()).ToList();
        FormatBox.SelectedIndex = (int)settings.Format;
        QualitySlider.Value = settings.Quality;
        RecordingFormatBox.ItemsSource = Enum.GetValues<RecordingFormat>().Select(format => format.ToString()).ToList();
        RecordingFormatBox.SelectedIndex = (int)settings.RecordingFormat;
        DirectoryBox.Text = settings.SaveDirectory ?? string.Empty;
        TemplateBox.Text = settings.FilenameTemplate;
        ClipboardCheck.IsChecked = settings.CopyToClipboard;
        AutoSaveCheck.IsChecked = settings.AutoSave;
        ThumbnailCheck.IsChecked = settings.ShowThumbnail;
        ThumbnailSecondsBox.Value = settings.ThumbnailSeconds;
        DelaySecondsBox.Value = settings.DelaySeconds;
        HistorySizeBox.Value = settings.HistorySize;
        RememberSelectionCheck.IsChecked = settings.RememberLastSelection;
        HideInstructionsCheck.IsChecked = settings.HideCaptureInstructions;
        SmoothPencilCheck.IsChecked = settings.SmoothPencilStrokes;
        VerboseLoggingCheck.IsChecked = settings.VerboseLogging;
        AutomaticUpdatesCheck.IsChecked = settings.AutomaticUpdateChecks;
        BetaUpdatesCheck.IsChecked = settings.BetaUpdates;

        // Each language named in itself, macshot's list in macshot's order: a reader
        // looking for their own language scans endonyms, not English names.
        LanguageBox.ItemsSource = AppLanguages.All.Select(language => language.Name).ToList();
        var chosen = AppLanguages.All
            .Select((language, index) => (language, index))
            .FirstOrDefault(entry => string.Equals(
                entry.language.Code,
                settings.Language,
                StringComparison.OrdinalIgnoreCase));
        LanguageBox.SelectedIndex = chosen.language.Code is null ? 0 : chosen.index;

        CaptureAreaHotkeyBox.Binding = settings.CaptureAreaHotkey;
        CaptureAllScreensHotkeyBox.Binding = settings.CaptureAllScreensHotkey;
        RecordScreenHotkeyBox.Binding = settings.RecordScreenHotkey;

        var shown = settings.EnabledTools();
        foreach (var (tool, toggle) in _toolToggles)
        {
            toggle.IsChecked = shown.Contains(tool);
        }

        ShowToolbarColors(settings.ToToolbarColors());

        VersionText.Text = $"Version {Version}";
        SettingsPathText.Text = _settings.Path;

        UpdateQualityVisibility();
        UpdateTemplatePreview();
    }

    private void ShowToolbarColors(ToolbarColors colors)
    {
        _toolbarBackground.Color = ToUiColor(colors.Background);
        _toolbarAccent.Color = ToUiColor(colors.Accent);
        _toolbarIcon.Color = ToUiColor(colors.Icon);
    }

    private void ResetToolbarColors_Click(object sender, RoutedEventArgs e)
    {
        ShowToolbarColors(ToolbarColors.Default);
        Apply();
    }

    private static Color ToUiColor(AnnotationColor color) =>
        Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

    private static AnnotationColor ToAnnotationColor(Color color) =>
        new(color.R, color.G, color.B, color.A);

    private CaptureSettings Collect()
    {
        // Built from what is stored rather than from nothing, because this window does
        // not show every setting: the annotation colour, width and line style are
        // chosen on the overlay's own toolbar, and starting from a blank record would
        // hand them back their defaults every time any preference was saved.
        return (_settings.Current with
        {
            Format = SelectedFormat(),
            Quality = (int)QualitySlider.Value,
            RecordingFormat = RecordingFormatBox.SelectedIndex >= 0
                ? (RecordingFormat)RecordingFormatBox.SelectedIndex
                : RecordingFormat.Mp4,
            SaveDirectory = DirectoryBox.Text,
            FilenameTemplate = TemplateBox.Text,
            CopyToClipboard = ClipboardCheck.IsChecked == true,
            AutoSave = AutoSaveCheck.IsChecked == true,
            ShowThumbnail = ThumbnailCheck.IsChecked == true,

            // NaN is what an emptied NumberBox reports, and casting that would give a
            // nonsense interval rather than an obviously wrong one.
            ThumbnailSeconds = double.IsNaN(ThumbnailSecondsBox.Value)
                ? CaptureSettings.Default.ThumbnailSeconds
                : (int)ThumbnailSecondsBox.Value,
            DelaySeconds = double.IsNaN(DelaySecondsBox.Value)
                ? CaptureSettings.Default.DelaySeconds
                : (int)DelaySecondsBox.Value,
            HistorySize = double.IsNaN(HistorySizeBox.Value)
                ? CaptureSettings.Default.HistorySize
                : (int)HistorySizeBox.Value,
            RememberLastSelection = RememberSelectionCheck.IsChecked == true,
            HideCaptureInstructions = HideInstructionsCheck.IsChecked == true,
            SmoothPencilStrokes = SmoothPencilCheck.IsChecked == true,
            VerboseLogging = VerboseLoggingCheck.IsChecked == true,
            AutomaticUpdateChecks = AutomaticUpdatesCheck.IsChecked == true,
            BetaUpdates = BetaUpdatesCheck.IsChecked == true,
            Language = LanguageBox.SelectedIndex >= 0 && LanguageBox.SelectedIndex < AppLanguages.All.Count
                ? AppLanguages.All[LanguageBox.SelectedIndex].Code
                : AppLanguages.System,
            CaptureAreaHotkey = CaptureAreaHotkeyBox.Binding,
            CaptureAllScreensHotkey = CaptureAllScreensHotkeyBox.Binding,
            RecordScreenHotkey = RecordScreenHotkeyBox.Binding,

            // Stored as what is hidden rather than what is ticked, so a tool added in a
            // later version arrives switched on instead of hidden from everyone who has
            // ever saved this page.
            HiddenTools = [.. _toolToggles
                .Where(entry => entry.Value.IsChecked != true)
                .Select(entry => entry.Key.ToString())],
            ToolbarBackgroundColor = ToAnnotationColor(_toolbarBackground.Color).ToHex(),
            ToolbarAccentColor = ToAnnotationColor(_toolbarAccent.Color).ToHex(),
            ToolbarIconColor = ToAnnotationColor(_toolbarIcon.Color).ToHex(),
        }).Normalized();
    }

    private CaptureImageFormat SelectedFormat() =>
        FormatBox.SelectedIndex >= 0 ? (CaptureImageFormat)FormatBox.SelectedIndex : CaptureImageFormat.Png;

    private void Format_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateQualityVisibility();
        UpdateTemplatePreview();
        Apply();
    }

    /// <summary>
    /// Keeps the preview in step as the template is typed. What is typed is not stored
    /// until the focus leaves the box — see <see cref="Setting_LostFocus"/>.
    /// </summary>
    private void Template_TextChanged(object sender, TextChangedEventArgs e) => UpdateTemplatePreview();

    /// <summary>Quality has no meaning for a lossless format, so it is not offered for one.</summary>
    private void UpdateQualityVisibility()
    {
        // Called from SelectionChanged, which fires while the XAML tree is still
        // being built and before the panel exists.
        if (QualityPanel is null)
        {
            return;
        }

        QualityPanel.Visibility = SelectedFormat().IsLossy() ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Shows what the template resolves to right now. A template is the one setting
    /// whose effect is invisible until a file has already been written under the
    /// wrong name.
    /// </summary>
    private void UpdateTemplatePreview()
    {
        if (TemplatePreview is null || TemplateBox is null)
        {
            return;
        }

        TemplatePreview.Text = FilenameTemplate.Resolve(TemplateBox.Text, DateTimeOffset.Now)
            + SelectedFormat().FileExtension();
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");

        // An unpackaged app has no implicit window for the picker to parent itself
        // to, so it has to be told which one to use or the call fails outright.
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            DirectoryBox.Text = folder.Path;
            Apply();
        }
    }

    /// <summary>
    /// Writes the portable half of the preferences to a file the user chooses.
    /// </summary>
    /// <remarks>
    /// Pending edits are written first. The export reads the store, not the controls, so
    /// exporting immediately after typing in a box would otherwise miss what was typed —
    /// the window writes 250 ms after a change, and a user is faster than that.
    /// </remarks>
    private async void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        Persist();

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,

            // macshot's name for the same file, dated so a folder of them sorts.
            SuggestedFileName = $"macshot-settings-{DateTimeOffset.Now:yyyy-MM-dd}",
        };
        picker.FileTypeChoices.Add("macshot settings", [".json"]);

        // An unpackaged app has no implicit window for the picker to parent itself
        // to, so it has to be told which one to use or the call fails outright.
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            var export = SettingsPortability.Export(_settings.Current, Version, DateTimeOffset.Now);
            await File.WriteAllTextAsync(file.Path, export.Json);
            StatusText.Text = $"Exported {export.KeyCount} settings.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not export the settings: {exception.Message}";
        }
    }

    /// <summary>
    /// Replaces the preferences with the ones in a file, after asking.
    /// </summary>
    /// <remarks>
    /// The confirmation is macshot's, and it is worth keeping: an import replaces every
    /// portable setting at once, including the ones on tabs the user is not looking at,
    /// and there is no undo for it.
    /// </remarks>
    private async void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(file.Path);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not read that file: {exception.Message}";
            return;
        }

        // Read before asking, so a file that is not a settings file is refused without
        // a warning dialog about replacing anything.
        var imported = SettingsPortability.Import(json, _settings.Current);
        if (imported.Settings is not { } restored)
        {
            StatusText.Text = imported.Failure ?? "That file could not be imported.";
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = L("Replace your current settings?"),
            Content = "Importing replaces your preferences with the ones in this file. "
                + "Your save folder, the last selection, and screenshot history are kept. "
                + "This cannot be undone.",
            PrimaryButtonText = L("Import"),
            CloseButtonText = L("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        // Saved before the controls are refilled: Load suppresses the write-back, so
        // nothing here would reach the file otherwise.
        _settings.Save(restored);
        Load(restored);

        StatusText.Text = imported.SkippedKeys.Count == 0
            ? $"Imported {imported.AppliedCount} settings."
            : $"Imported {imported.AppliedCount} settings; {imported.SkippedKeys.Count} were not this version's to take.";
    }

    /// <summary>Opens the folder holding the settings file, with the file selected.</summary>
    private void ShowSettingsFile_Click(object sender, RoutedEventArgs e) => Reveal(_settings.Path);

    private void ResetDirectory_Click(object sender, RoutedEventArgs e)
    {
        DirectoryBox.Text = string.Empty;
        Apply();
    }

    /// <summary>
    /// Opens the folder holding the log and this settings file.
    /// </summary>
    /// <remarks>
    /// The folder rather than the log itself, because the two things worth collecting
    /// live side by side and because <c>%LOCALAPPDATA%</c> is not a path anyone types
    /// from memory. Selecting the log inside it saves the second step.
    /// </remarks>
    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(DiagnosticLog.Directory);
        Reveal(DiagnosticLog.Path);
    }

    /// <summary>
    /// Opens Explorer on <paramref name="path"/>'s folder with the file selected, or on
    /// the folder alone when the file is not there yet.
    /// </summary>
    private void Reveal(string path)
    {
        try
        {
            using var opened = Process.Start(new ProcessStartInfo("explorer.exe")
            {
                // Quoted: the path runs through the user's profile name, which can
                // contain a space.
                Arguments = File.Exists(path)
                    ? $"/select,\"{path}\""
                    : $"\"{Path.GetDirectoryName(path)}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not open the folder: {exception.Message}";
        }
    }

    /// <summary>What this build calls itself, shown on the About page and stamped into an export.</summary>
    private static string Version =>
        typeof(PreferencesWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    /// <summary>
    /// Deletes the kept copies now.
    /// </summary>
    /// <remarks>
    /// An action rather than a setting, so it says what it did: someone clearing history
    /// has just captured something they want gone, and every other control on this page
    /// reports nothing because taking effect is all there is to report.
    /// </remarks>
    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        ScreenshotHistory.Clear();
        StatusText.Text = L("History cleared.");
    }
}
