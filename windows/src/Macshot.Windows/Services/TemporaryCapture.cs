using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Output;

using Windows.Storage;

namespace Macshot.Windows.Services;

/// <summary>
/// A copy of a capture on disk, for handing to another program.
/// </summary>
/// <remarks>
/// macshot's <c>makeCurrentImageFileURL</c>
/// (<c>FloatingThumbnailController.swift:372</c>): sharing and Open With both need a
/// file, and the capture in hand may never have been written anywhere — history can be
/// off, and the panel is up before anything is saved.
/// </remarks>
internal static class TemporaryCapture
{
    /// <summary>
    /// Writes the capture where another program can read it, always as a PNG.
    /// </summary>
    /// <remarks>
    /// PNG whatever the save format is: this is a paste target rather than an archive,
    /// so it should never be handed the lossy copy. It goes to the temporary directory
    /// rather than the user's pictures — it is a copy handed to another program, not a
    /// capture the user asked to keep, and it must not land where the saved ones live.
    /// </remarks>
    public static async Task<StorageFile> WriteAsync(CapturedFrame frame, CaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.Combine(Path.GetTempPath(), "macshot");
        Directory.CreateDirectory(directory);

        // The user's own naming, because the name travels with the image: an attachment
        // called "tmp4F2A.png" is one the recipient cannot place.
        var name = FilenameTemplate.ResolveUnique(
            settings.FilenameTemplate,
            DateTimeOffset.Now,
            CaptureImageFormat.Png.FileExtension(),
            candidate => File.Exists(Path.Combine(directory, candidate)));

        var path = Path.Combine(directory, name);
        await File.WriteAllBytesAsync(
            path,
            (await ImageDelivery.EncodeAsync(frame, CaptureImageFormat.Png, CaptureSettings.MaxQuality)).Bytes);

        return await StorageFile.GetFileFromPathAsync(path);
    }
}
