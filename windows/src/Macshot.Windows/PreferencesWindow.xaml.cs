using System.Diagnostics;
using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Input;
using Macshot.Windows.Core.Output;
using Macshot.Windows.Services;
using Macshot.Windows.Toolbar;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace Macshot.Windows;

/// <summary>
/// The settings window, the counterpart of the macOS
/// <c>PreferencesWindowController</c>.
/// </summary>
/// <remarks>
/// Values are read into the controls once and written back only on Save, so a
/// half-typed template never reaches the delivery path. The controls are wired by
/// hand rather than bound, because <see cref="CaptureSettings"/> is an immutable
/// record with no change notification and adding one purely for this window would
/// put UI concerns into Core.
/// </remarks>
public sealed partial class PreferencesWindow : Window
{
    /// <summary>
    /// Chosen so the longest tab needs no scrolling on a 1080p display, which is the
    /// smallest screen worth designing this for.
    /// </summary>
    private const double WidthDips = 640;

    private const double HeightDips = 700;

    private readonly SettingsStore _settings;

    /// <summary>One tick box per tool, in the order the toolbar keeps them.</summary>
    private readonly Dictionary<AnnotationTool, CheckBox> _toolToggles = [];

    private readonly ColorChoice _toolbarBackground = new("Background");
    private readonly ColorChoice _toolbarAccent = new("Accent");
    private readonly ColorChoice _toolbarIcon = new("Icons");

    public PreferencesWindow(SettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        InitializeComponent();
        BuildToolsPage();
        Load(_settings.Current);
        PlaceOnScreen();
    }

    /// <summary>
    /// Builds the parts of the Tools page that come from the toolbar rather than from the
    /// markup, so a tool added later appears here without this page being edited.
    /// </summary>
    private void BuildToolsPage()
    {
        foreach (var tool in ToolbarActions.ToolOrder)
        {
            var toggle = new CheckBox { Content = ToolbarActions.Tooltip(tool) };
            _toolToggles[tool] = toggle;
            ToolToggles.Children.Add(toggle);
        }

        ToolbarColorRow.Children.Add(_toolbarBackground);
        ToolbarColorRow.Children.Add(_toolbarAccent);
        ToolbarColorRow.Children.Add(_toolbarIcon);
    }

    /// <summary>
    /// Shows the chosen page. All six exist at once and one is visible: a Frame would
    /// rebuild the page on every click, and Save reads every control on every page.
    /// </summary>
    private void Sections_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // The first item is selected in the markup, so this can fire while the tree is
        // still being built and before the pages it switches between exist.
        if (GeneralPage is null)
        {
            return;
        }

        var chosen = (args.SelectedItem as NavigationViewItem)?.Tag as string;

        foreach (var (tag, page) in Pages())
        {
            page.Visibility = tag == chosen ? Visibility.Visible : Visibility.Collapsed;
        }
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
        FormatBox.ItemsSource = Enum.GetValues<CaptureImageFormat>().Select(format => format.ToString()).ToList();
        FormatBox.SelectedIndex = (int)settings.Format;
        QualitySlider.Value = settings.Quality;
        RecordingFormatBox.ItemsSource = Enum.GetValues<RecordingFormat>().Select(format => format.ToString()).ToList();
        RecordingFormatBox.SelectedIndex = (int)settings.RecordingFormat;
        DirectoryBox.Text = settings.SaveDirectory ?? string.Empty;
        TemplateBox.Text = settings.FilenameTemplate;
        ClipboardSwitch.IsOn = settings.CopyToClipboard;
        AutoSaveSwitch.IsOn = settings.AutoSave;
        ThumbnailSwitch.IsOn = settings.ShowThumbnail;
        ThumbnailSecondsBox.Value = settings.ThumbnailSeconds;
        DelaySecondsBox.Value = settings.DelaySeconds;
        HistorySizeBox.Value = settings.HistorySize;
        RememberSelectionSwitch.IsOn = settings.RememberLastSelection;
        SmoothPencilSwitch.IsOn = settings.SmoothPencilStrokes;
        VerboseLoggingSwitch.IsOn = settings.VerboseLogging;

#if OFFLINE
        // No translator in this build, so nothing to give a key to.
        TranslationSection.Visibility = Visibility.Collapsed;
#else
        TranslateKeyBox.Password = settings.TranslateApiKey;
#endif

        CaptureAreaHotkeyBox.Binding = settings.CaptureAreaHotkey;
        CaptureAllScreensHotkeyBox.Binding = settings.CaptureAllScreensHotkey;
        RecordScreenHotkeyBox.Binding = settings.RecordScreenHotkey;

        var shown = settings.EnabledTools();
        foreach (var (tool, toggle) in _toolToggles)
        {
            toggle.IsChecked = shown.Contains(tool);
        }

        ShowToolbarColors(settings.ToToolbarColors());

        VersionText.Text = $"Version {typeof(PreferencesWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";
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

    private void ResetToolbarColors_Click(object sender, RoutedEventArgs e) =>
        ShowToolbarColors(ToolbarColors.Default);

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
            CopyToClipboard = ClipboardSwitch.IsOn,
            AutoSave = AutoSaveSwitch.IsOn,
            ShowThumbnail = ThumbnailSwitch.IsOn,

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
            RememberLastSelection = RememberSelectionSwitch.IsOn,
            SmoothPencilStrokes = SmoothPencilSwitch.IsOn,
            VerboseLogging = VerboseLoggingSwitch.IsOn,
#if !OFFLINE
            TranslateApiKey = TranslateKeyBox.Password,
#endif
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
    }

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
        }
    }

    private void ResetDirectory_Click(object sender, RoutedEventArgs e) => DirectoryBox.Text = string.Empty;

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
        try
        {
            Directory.CreateDirectory(DiagnosticLog.Directory);

            using var opened = Process.Start(new ProcessStartInfo("explorer.exe")
            {
                // Quoted: the path runs through the user's profile name, which can
                // contain a space.
                Arguments = File.Exists(DiagnosticLog.Path)
                    ? $"/select,\"{DiagnosticLog.Path}\""
                    : $"\"{DiagnosticLog.Directory}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not open the folder: {exception.Message}";
        }
    }

    /// <summary>
    /// Deletes the kept copies immediately, without waiting for Save.
    /// </summary>
    /// <remarks>
    /// Immediate because it is an action rather than a setting: someone clearing
    /// history has just captured something they want gone, and leaving it on disk
    /// until an unrelated Save button is pressed would be the wrong answer to that.
    /// </remarks>
    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        ScreenshotHistory.Clear();
        StatusText.Text = "History cleared.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
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
            StatusText.Text = $"Not a shortcut: {string.Join(", ", unreadable)}. Click it and press the keys.";
            return;
        }

        var settings = Collect();
        try
        {
            _settings.Save(settings);
        }
        // Everything, not only the file system failures. This runs from a click
        // handler, so anything that escapes has nobody above it to catch it, and a
        // preference that cannot be stored is never a reason to take the app down.
        catch (Exception exception)
        {
            StatusText.Text = $"Could not save preferences: {exception.Message}";
            return;
        }

        // Normalization may have changed what was typed, so the controls are reloaded
        // from what was actually stored rather than from what was entered.
        Load(_settings.Current);
        StatusText.Text = $"Saved to {_settings.Path}";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
