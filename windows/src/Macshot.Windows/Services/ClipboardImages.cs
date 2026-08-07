using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

using WinRT.Interop;

namespace Macshot.Windows.Services;

/// <summary>
/// Getting a picture from somewhere other than the screen: the clipboard, or a file.
/// </summary>
/// <remarks>
/// Behind macshot's "Open Image...", "Open from Clipboard" and "Pin from Clipboard".
/// Everything downstream of a capture — the editor, the pin, the annotation tools —
/// works on pixels and does not care where they came from, so these three items are the
/// whole of what it takes to point them at something that was never a screenshot.
/// </remarks>
internal static class ClipboardImages
{
    /// <summary>
    /// What "Open Image..." offers. macshot's list, plus the two Windows adds: a picker
    /// with no filter shows every file on the machine, and most of them are not images.
    /// </summary>
    internal static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".heic", ".webp"];

    /// <summary>
    /// Asks for an image file and reads it, or answers null when the dialog was
    /// dismissed.
    /// </summary>
    /// <remarks>
    /// One file rather than macshot's many: this port has one editor window, so a second
    /// file would close the window holding the first. Opening them one at a time is what
    /// the port can honestly do.
    /// </remarks>
    public static async Task<CapturedFrame?> PickAsync(IntPtr owner)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };

        // A desktop app has no CoreWindow for the picker to belong to, so it is given a
        // window handle instead. Without this the call throws rather than opening.
        InitializeWithWindow.Initialize(picker, owner);

        foreach (var extension in ImageExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        if (await picker.PickSingleFileAsync() is not { } file)
        {
            return null;
        }

        return await ImageLoader.LoadAsync(file.Path);
    }

    /// <summary>
    /// The picture on the clipboard, or null when there is not one.
    /// </summary>
    /// <param name="renderText">
    /// Whether copied text counts, drawn as a picture of itself. True for pinning, which
    /// is macshot's "No Image or Text on Clipboard"; false for the editor, whose item
    /// says image.
    /// </param>
    /// <remarks>
    /// A bitmap first, then a copied file, then — if asked — the text. That is the order
    /// of how specific the answer is: something that copied a picture as a picture meant
    /// the picture, and text is what is left when nothing else was offered.
    /// </remarks>
    public static async Task<CapturedFrame?> ReadAsync(bool renderText)
    {
        DataPackageView clipboard;
        try
        {
            clipboard = Clipboard.GetContent();
        }
        catch (Exception exception)
        {
            // The clipboard is a shared, singly-owned resource: another program may hold
            // it open at the moment this asks. Nothing here is worth an error dialog —
            // the caller says "nothing to paste", which is what the user sees anyway.
            DiagnosticLog.Write($"Could not read the clipboard: {exception.Message}");
            return null;
        }

        try
        {
            if (clipboard.Contains(StandardDataFormats.Bitmap))
            {
                var reference = await clipboard.GetBitmapAsync();
                using var stream = await reference.OpenReadAsync();
                return await ImageLoader.LoadAsync(stream);
            }

            if (clipboard.Contains(StandardDataFormats.StorageItems))
            {
                foreach (var item in await clipboard.GetStorageItemsAsync())
                {
                    if (item is StorageFile file && IsImage(file.FileType))
                    {
                        return await ImageLoader.LoadAsync(file.Path);
                    }
                }
            }

            if (renderText && clipboard.Contains(StandardDataFormats.Text))
            {
                // The primary display, because that is where a new pin opens. How wide the
                // text wraps and how tall the picture may be are both fractions of it.
                var primary = MonitorEnumerator.Enumerate().Layout.Primary;

                return ClipboardTextImage.Render(
                    await clipboard.GetTextAsync(),
                    primary.Bounds.Width,
                    primary.Bounds.Height);
            }
        }
        catch (Exception exception)
        {
            // A clipboard entry that says it is a bitmap and then will not decode is the
            // copying program's fault, not something the user can act on.
            DiagnosticLog.Write($"Could not read the image on the clipboard: {exception.Message}");
        }

        return null;
    }

    private static bool IsImage(string extension) =>
        ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
}
