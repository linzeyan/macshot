using Macshot.Windows.Core.Recognition;
using Macshot.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Macshot.Windows;

/// <summary>
/// Shows what OCR read out of a capture, the counterpart of the macOS
/// <c>OCRResultController</c>.
/// </summary>
/// <remarks>
/// A window rather than a silent copy to the clipboard: OCR gets characters wrong,
/// and pasting a wrong string into a terminal is worse than being shown it first.
/// </remarks>
public sealed partial class TextRecognitionWindow : Window
{
    private readonly SettingsStore _settings;

    /// <summary>
    /// Cancelled when the window closes, so a request still in flight cannot come back
    /// and write into controls that are gone.
    /// </summary>
    private readonly CancellationTokenSource _closing = new();

    public TextRecognitionWindow(string text, SettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        InitializeComponent();
        RecognizedTextBox.Text = text ?? string.Empty;
        StatusText.Text = string.IsNullOrWhiteSpace(text) ? "No text was recognized." : string.Empty;

        Closed += (_, _) => _closing.Cancel();

#if OFFLINE
        // Nothing to show it with: the request is not in this build. Collapsed rather
        // than disabled, because a greyed-out Translate button reads as a feature that
        // is temporarily unavailable rather than one this build does not have.
        TranslateRow.Visibility = Visibility.Collapsed;
#else
        TargetLanguageBox.ItemsSource = TranslationLanguages.All;
        TargetLanguageBox.SelectedIndex =
            TranslationLanguages.IndexOf(_settings.Current.TranslateTargetLanguage);
#endif
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(RecognizedTextBox.Text);

        try
        {
            Clipboard.SetContent(package);

            // macshot is a background tool the user will quit, and without this the
            // text would go with it.
            Clipboard.Flush();
            StatusText.Text = "Copied.";
        }
        catch (Exception exception)
        {
            // Another process can hold the clipboard open. Saying so beats a button
            // that silently does nothing.
            StatusText.Text = $"Could not copy: {exception.Message}";
        }
    }

    /// <summary>
    /// Replaces the text with its translation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In place, rather than in a second box beside it. The window's one job is to
    /// produce a string to copy, and two boxes would ask which of them Copy meant.
    /// The original is a Ctrl+Z away, because the box is an ordinary text box.
    /// </para>
    /// <para>
    /// The body is compiled out of the offline build. The method itself has to remain
    /// for the XAML to bind to, and the row it belongs to is collapsed there, so it
    /// cannot be reached.
    /// </para>
    /// </remarks>
    private async void Translate_Click(object sender, RoutedEventArgs e)
    {
#if !OFFLINE
        // Async void, so nothing above this can catch: everything inside has to be
        // handled here or a network fault ends the process.
        try
        {
            var settings = _settings.Current;
            var target = SelectedLanguageCode();

            TranslateButton.IsEnabled = false;
            StatusText.Text = "Translating...";

            var outcome = await TranslationService.TranslateAsync(
                RecognizedTextBox.Text,
                target,
                settings.TranslateApiKey,
                _closing.Token);

            if (_closing.IsCancellationRequested)
            {
                return;
            }

            if (outcome.Text is { } translated)
            {
                RecognizedTextBox.Text = translated;
                StatusText.Text = $"Translated into {TranslationLanguages.All[TranslationLanguages.IndexOf(target)].Name}.";
            }
            else
            {
                StatusText.Text = outcome.Failure ?? "The translation failed.";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not translate: {exception.Message}";
        }
        finally
        {
            if (!_closing.IsCancellationRequested)
            {
                TranslateButton.IsEnabled = true;
            }
        }
#endif
    }

    /// <summary>
    /// Remembers the language as it is chosen, rather than on translating.
    /// </summary>
    /// <remarks>
    /// Someone who picks a language and then closes the window has still said which
    /// language they want, and being asked again next time is the sort of thing that
    /// makes a setting feel broken. The body is compiled out for the same reason as
    /// the one above.
    /// </remarks>
    private void TargetLanguage_Changed(object sender, SelectionChangedEventArgs e)
    {
#if !OFFLINE
        // Fires while the XAML tree is still being built, before the store is assigned.
        if (_settings is null)
        {
            return;
        }

        var chosen = SelectedLanguageCode();
        if (chosen == _settings.Current.TranslateTargetLanguage)
        {
            return;
        }

        try
        {
            _settings.Save(_settings.Current with { TranslateTargetLanguage = chosen });
        }
        catch (Exception exception)
        {
            // The chosen language still applies to this window; only remembering it
            // failed, and that is not worth taking over the status line for.
            DiagnosticLog.Write($"Could not remember the translation language: {exception.Message}");
        }
#endif
    }

#if !OFFLINE
    private string SelectedLanguageCode() =>
        TargetLanguageBox.SelectedItem is TranslationLanguage language
            ? language.Code
            : TranslationLanguages.DefaultCode;
#endif

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
