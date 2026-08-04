using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Output;
using Microsoft.UI.Xaml;
using WinRT.Interop;

using Windows.Storage;
using Windows.Storage.Pickers;

namespace Macshot.Windows.Services;

/// <summary>
/// Saving a capture somewhere other than the folder the preferences name.
/// </summary>
/// <remarks>
/// <para>
/// Behind the right-click on the Save button, which is where macshot puts it. The plain
/// press stays what it was — one press for the usual answer — because a capture tool
/// that opens a dialog every time is a capture tool nobody uses for the twentieth
/// screenshot of the morning.
/// </para>
/// <para>
/// The format follows the extension the user picks rather than the preference, so this
/// is also the way to get one JPEG out of a session set to PNG. Anything else would be
/// a dialog offering a choice it then ignored.
/// </para>
/// </remarks>
internal static class SavePrompt
{
    /// <summary>
    /// Saves the way the preferences say to: into the folder they name, or wherever the
    /// user picks. Answers the path, or null when the dialog was dismissed — which means
    /// the capture is still in hand, not thrown away.
    /// </summary>
    /// <remarks>
    /// Every Save in the app comes through here, so macshot's <c>saveAction</c> cannot be
    /// honoured by some of the buttons and ignored by the rest. The Save As beside them
    /// calls <see cref="WriteAsync(Window, CapturedFrame, CaptureSettings, string?)"/>
    /// directly, because that one asks whatever the setting says.
    /// </remarks>
    public static Task<string?> SaveAsync(
        Window owner,
        CapturedFrame frame,
        CaptureSettings settings,
        string? windowTitle = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return SaveAsync(WindowNative.GetWindowHandle(owner), frame, settings, windowTitle);
    }

    /// <param name="owner">
    /// The window the dialog belongs to. A capture delivered straight from the hotkey has
    /// none — the overlay is dismissed before delivery — so the controller passes its
    /// message window, which is a real handle even though nothing is drawn in it.
    /// </param>
    /// <inheritdoc cref="SaveAsync(Window, CapturedFrame, CaptureSettings, string?)"/>
    public static async Task<string?> SaveAsync(
        IntPtr owner,
        CapturedFrame frame,
        CaptureSettings settings,
        string? windowTitle = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(settings);

        return settings.SaveAction is SaveAction.AskWhereToSave
            ? await WriteAsync(owner, frame, settings, windowTitle)
            : await ImageDelivery.SaveAsync(frame, settings, windowTitle);
    }

    /// <summary>
    /// Asks where to put it and writes it there. Answers the path, or null if the
    /// dialog was dismissed — which means the capture is still in hand, not thrown away.
    /// </summary>
    public static Task<string?> WriteAsync(
        Window owner,
        CapturedFrame frame,
        CaptureSettings settings,
        string? windowTitle = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return WriteAsync(WindowNative.GetWindowHandle(owner), frame, settings, windowTitle);
    }

    /// <inheritdoc cref="WriteAsync(Window, CapturedFrame, CaptureSettings, string?)"/>
    public static async Task<string?> WriteAsync(
        IntPtr owner,
        CapturedFrame frame,
        CaptureSettings settings,
        string? windowTitle = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(settings);

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(SuggestedName(settings, windowTitle)),
        };

        // A desktop app has no CoreWindow for the picker to belong to, so it is given the
        // window it was opened from. Without this the call throws rather than opening.
        InitializeWithWindow.Initialize(picker, owner);

        // The preference's format first, so the top entry is the one the session is
        // already set to.
        foreach (var format in Formats(ImageEncoders.Resolve(settings.Format)))
        {
            picker.FileTypeChoices.Add(Describe(format), [format.FileExtension()]);
        }

        if (await picker.PickSaveFileAsync() is not { } file)
        {
            return null;
        }

        var chosen = FormatOf(file.FileType) ?? ImageEncoders.Resolve(settings.Format);

        // Through ForSaving like the folder path, or the resolution setting would hold
        // for the captures saved without being asked about and not for the ones the user
        // named by hand.
        var encoded = await ImageDelivery.EncodeAsync(
            ImageDelivery.ForSaving(frame, settings),
            chosen,
            settings.Quality);
        await FileIO.WriteBytesAsync(file, encoded.Bytes);

        // The picker has already created the file under the extension it was offered, so
        // an encoder that fell back leaves a JPEG called .heic — a file every viewer
        // opens by its name and then fails on. Renaming is the only way to keep the name
        // and the bytes telling the same story.
        if (encoded.Format != chosen)
        {
            await file.RenameAsync(
                Path.GetFileNameWithoutExtension(file.Name) + encoded.Format.FileExtension(),
                NameCollisionOption.GenerateUniqueName);
        }

        return file.Path;
    }

    /// <summary>
    /// Only what this machine can write, preference first — a dialog offering a format
    /// the encoder does not have would be a choice that renamed itself on the way out.
    /// </summary>
    private static IEnumerable<CaptureImageFormat> Formats(CaptureImageFormat first) =>
        ImageEncoders.Available.OrderByDescending(format => format == first);

    private static string Describe(CaptureImageFormat format) => $"{format.DisplayName()} image";

    private static CaptureImageFormat? FormatOf(string extension)
    {
        foreach (var format in Enum.GetValues<CaptureImageFormat>())
        {
            if (string.Equals(format.FileExtension(), extension, StringComparison.OrdinalIgnoreCase))
            {
                return format;
            }
        }

        return null;
    }

    /// <summary>
    /// The name the folder would have given it. Uniqueness is the dialog's business from
    /// here, so nothing on disk is checked.
    /// </summary>
    private static string SuggestedName(CaptureSettings settings, string? windowTitle) =>
        FilenameTemplate.ResolveUnique(
            settings.FilenameTemplate,
            DateTimeOffset.Now,
            settings.Format.FileExtension(),
            _ => false,
            new FilenameContext(windowTitle));
}
