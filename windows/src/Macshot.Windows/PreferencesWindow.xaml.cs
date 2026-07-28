using Macshot.Windows.Core.Output;
using Macshot.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
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
    private readonly SettingsStore _settings;

    public PreferencesWindow(SettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        InitializeComponent();
        Load(_settings.Current);
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
        UpdateQualityVisibility();
        UpdateTemplatePreview();
    }

    private CaptureSettings Collect()
    {
        return new CaptureSettings
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
        }.Normalized();
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = Collect();
        try
        {
            _settings.Save(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
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
