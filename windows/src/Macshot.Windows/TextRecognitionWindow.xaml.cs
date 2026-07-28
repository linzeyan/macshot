using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace Macshot.Windows;

/// <summary>
/// Shows what OCR read out of a capture, the counterpart of the macOS
/// <c>OCRResultController</c>.
/// </summary>
/// <remarks>
/// A window rather than a silent copy to the clipboard: OCR gets characters wrong,
/// and pasting a wrong string into a terminal is worse than being shown it first.
/// Translation, which the macOS window also offers, is not part of this milestone.
/// </remarks>
public sealed partial class TextRecognitionWindow : Window
{
    public TextRecognitionWindow(string text)
    {
        InitializeComponent();
        RecognizedTextBox.Text = text ?? string.Empty;
        StatusText.Text = string.IsNullOrWhiteSpace(text) ? "No text was recognized." : string.Empty;
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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
