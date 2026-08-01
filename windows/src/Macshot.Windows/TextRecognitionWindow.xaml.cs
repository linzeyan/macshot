using Macshot.Windows.Core.Recognition;
using Macshot.Windows.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;
using static Macshot.Windows.Services.Localization;

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
    /// <summary>
    /// Only the build that can translate has any use for the settings here. Kept behind
    /// the same condition as the code that reads it, because a field assigned and never
    /// read is a warning, and warnings are errors in this project.
    /// </summary>
#if !OFFLINE
    private readonly SettingsStore _settings;
#endif

    /// <summary>
    /// Cancelled when the window closes, so a request still in flight cannot come back
    /// and write into controls that are gone.
    /// </summary>
    private readonly CancellationTokenSource _closing = new();

    /// <summary>What the capture decoded to, in the order it was found.</summary>
    private readonly IReadOnlyList<QrCode> _qrCodes;

    /// <summary>macshot's window, in its size.</summary>
    private const double WidthDips = 720;

    private const double HeightDips = 460;

    /// <summary>
    /// Below this the preview and the words are both too narrow to read, so the window
    /// will not go there.
    /// </summary>
    private const double MinimumWidthDips = 480;

    private const double MinimumHeightDips = 300;

    /// <param name="source">
    /// The capture the text was read out of, shown down the left. Null leaves the pane
    /// out, as macshot does when it has no image to put there.
    /// </param>
    /// <param name="qrCodes">
    /// What the same capture decoded to, listed under the words. macshot reads text and
    /// QR codes in one pass and shows them in one window (<c>VisionOCR.swift:48</c>),
    /// because a screenshot of a page with a code on it is one thing the user captured,
    /// not two.
    /// </param>
    public TextRecognitionWindow(
        string text,
        SettingsStore settings,
        CapturedFrame? source = null,
        IReadOnlyList<QrCode>? qrCodes = null)
    {
        // Checked in both builds even though only one keeps it: the caller passing null
        // is a defect either way, and it should not be a defect that only shows up in
        // one variant.
        ArgumentNullException.ThrowIfNull(settings);
#if !OFFLINE
        _settings = settings;
#endif

        InitializeComponent();
        // Every string in the XAML is already the English text macshot keys by,
        // so the page is translated in place rather than written twice.
        this.Localize();
        _qrCodes = qrCodes ?? [];
        RecognizedTextBox.Text = text ?? string.Empty;
        StatusText.Text = string.IsNullOrWhiteSpace(text) && _qrCodes.Count == 0
            ? L("No text was recognized.")
            : string.Empty;
        ShowCount(text ?? string.Empty);
        ShowQrCodes();

        var appWindow = this.GetAppWindow();
        appWindow.UseAppIcon();
        Resize(appWindow);
        Closed += (_, _) => _closing.Cancel();

        if (source is null)
        {
            // Collapsed rather than left empty: a 240-wide black column with nothing in
            // it reads as a preview that failed to load.
            PreviewPane.Visibility = Visibility.Collapsed;
            PreviewSeparator.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Not awaited: the words are the point of the window and are already in it,
            // and a picture that arrives a frame later is not worth delaying them for.
            _ = ShowPreviewAsync(source);
        }

#if OFFLINE
        // Nothing to show it with: the request is not in this build. Collapsed rather
        // than disabled, because a greyed-out Translate button reads as a feature that
        // is temporarily unavailable rather than one this build does not have.
        TranslateLabel.Visibility = Visibility.Collapsed;
        TargetLanguageBox.Visibility = Visibility.Collapsed;
        TranslateButton.Visibility = Visibility.Collapsed;
#else
        TargetLanguageBox.ItemsSource = TranslationLanguages.All;
        TargetLanguageBox.SelectedIndex =
            TranslationLanguages.IndexOf(_settings.Current.TranslateTargetLanguage);
#endif
    }

    /// <summary>
    /// How much was read, which is the quickest way to tell "OCR found nothing" from
    /// "OCR found a page and it scrolled off the top".
    /// </summary>
    private void ShowCount(string text)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        CountText.Text = $"{text.Length} chars · {words} words";
    }

    private async Task ShowPreviewAsync(CapturedFrame source)
    {
        try
        {
            var bitmap = new SoftwareBitmapSource();
            await bitmap.SetBitmapAsync(source.ToSoftwareBitmap());
            PreviewImage.Source = bitmap;
        }
        catch (Exception exception)
        {
            // The text is the window's job; the picture beside it is context. Losing it
            // is worth a line in the log and nothing else.
            DiagnosticLog.Write($"Could not show the recognized capture: {exception.Message}");
            PreviewPane.Visibility = Visibility.Collapsed;
            PreviewSeparator.Visibility = Visibility.Collapsed;
        }
    }

    private static void Resize(AppWindow appWindow)
    {
        var monitor = MonitorEnumerator.Enumerate().Layout.Primary;

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)MinimumWidthDips;
            presenter.PreferredMinimumHeight = (int)MinimumHeightDips;
        }

        appWindow.Resize(new SizeInt32(
            (int)(WidthDips * monitor.Scale),
            (int)(HeightDips * monitor.Scale)));
    }

    /// <summary>
    /// Lists the decoded codes, each with the two things worth doing to one.
    /// </summary>
    /// <remarks>
    /// Built here rather than bound through a template because macshot builds the same
    /// section a row at a time, and a row is a label and two buttons — a template and a
    /// converter for the Open button's visibility would be more machinery than the
    /// thing it draws.
    /// </remarks>
    private void ShowQrCodes()
    {
        // macshot's titles: the window says what it holds, and it does not always hold
        // the same thing.
        Title = L(_qrCodes.Count == 0 ? "Text Recognition" : "Text & QR Recognition");

        if (_qrCodes.Count == 0)
        {
            return;
        }

        QrSection.Visibility = Visibility.Visible;
        QrHeading.Text = L(_qrCodes.Count == 1 ? "QR Code" : "QR Codes");

        foreach (var code in _qrCodes)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var value = new TextBlock
            {
                Text = code.Value,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,

                // One line with an ellipsis: a payload can be a page of text, and the
                // row is a handle for copying it rather than a place to read it.
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            };
            ToolTipService.SetToolTip(value, code.Value);
            row.Children.Add(value);

            var copy = new Button { Content = L("Copy"), FontSize = 12, Padding = new Thickness(10, 2, 10, 2) };
            copy.Click += (_, _) => CopyText(code.Value, L("QR code copied."));
            Grid.SetColumn(copy, 1);
            row.Children.Add(copy);

            // No Open button at all when the payload is not a web address, rather than a
            // disabled one: a Wi-Fi or vCard code is not a broken link.
            if (code.Url is { } url)
            {
                var open = new Button { Content = L("Open"), FontSize = 12, Padding = new Thickness(10, 2, 10, 2) };
                open.Click += async (_, _) => await Launcher.LaunchUriAsync(url);
                Grid.SetColumn(open, 2);
                row.Children.Add(open);
            }

            QrRows.Children.Add(row);
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        // macshot's `copyText`: with nothing recognized, Copy takes the payloads rather
        // than copying an empty string over whatever the user already had.
        var text = string.IsNullOrWhiteSpace(RecognizedTextBox.Text) && _qrCodes.Count > 0
            ? string.Join(Environment.NewLine, _qrCodes.Select(code => code.Value))
            : RecognizedTextBox.Text;

        CopyText(text, L("Copied."));
    }

    private void CopyText(string text, string done)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);

        try
        {
            Clipboard.SetContent(package);

            // macshot is a background tool the user will quit, and without this the
            // text would go with it.
            Clipboard.Flush();
            StatusText.Text = done;
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
            StatusText.Text = L("Translating...");

            // The language and the length, never the text: a trace file is something a
            // user attaches to a bug report, and the text is theirs. The length is
            // enough to tell an empty box from a refused request.
            DiagnosticLog.Verbose($"translating {RecognizedTextBox.Text.Length} characters into {target}");

            var outcome = await TranslationService.TranslateAsync(
                RecognizedTextBox.Text,
                target,
                _closing.Token);

            DiagnosticLog.Verbose(
                outcome.Succeeded ? "translation returned" : $"translation failed: {outcome.Failure}");

            if (_closing.IsCancellationRequested)
            {
                return;
            }

            if (outcome.Text is { } translated)
            {
                RecognizedTextBox.Text = translated;
                ShowCount(translated);
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
