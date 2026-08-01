using Windows.ApplicationModel.DataTransfer;

namespace Macshot.Windows.Services;

/// <summary>
/// Putting recognized text on the clipboard, which is what reading a region was for.
/// </summary>
/// <remarks>
/// One copy of this, because two places need it and they must not disagree about the
/// flush: macshot is a background tool the user will quit, and text left unflushed goes
/// with it.
/// </remarks>
internal static class ClipboardText
{
    /// <summary>
    /// Copies it. Throws what the clipboard throws — another process may be holding it
    /// open, and the caller is the one that knows whether there is a window to say so in.
    /// </summary>
    public static void Copy(string text)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }
}
