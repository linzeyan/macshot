using System.Runtime.InteropServices;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Recording;

// Imported rather than qualified for the same reason as in ScreenRecorder: inside
// namespace Macshot.Windows the name "Windows" binds to Macshot.Windows.
using Windows.Storage;

namespace Macshot.Windows.Services;

/// <summary>
/// Puts a merged recording where the user is looking for it.
/// </summary>
/// <remarks>
/// The mixing is <see cref="AudioMixdown"/>'s, in a library a test can load. What is left
/// here is the part that is about files rather than about sound: naming the merge after the
/// recording, putting it in the recording's place when it can, and leaving nothing behind
/// that would be taken for a recording when it cannot.
/// </remarks>
internal static class AudioMerger
{
    /// <summary>
    /// Merges <paramref name="tracks"/> into <paramref name="recordingPath"/> and answers
    /// where the recording now is.
    /// </summary>
    /// <remarks>
    /// The original path when the merged file could take its place, and the merged file's
    /// own path when it could not — which is macshot's answer to the same problem. What
    /// must not happen is that a recording the user just made goes missing because the
    /// file it was to be replaced by was still open somewhere.
    /// </remarks>
    public static async Task<string> MergeAsync(
        string recordingPath,
        RecordedAudioTracks tracks,
        AudioMergeAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var directory = Path.GetDirectoryName(recordingPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("A recording needs a full path to merge.", nameof(recordingPath));
        }

        var source = await StorageFile.GetFileFromPathAsync(recordingPath);
        var folder = await StorageFolder.GetFolderFromPathAsync(directory);

        // Beside the recording and named as macshot names it, so a merge that cannot
        // replace the original still leaves a file where the user is looking.
        var written = await folder.CreateFileAsync(
            Path.GetFileNameWithoutExtension(recordingPath) + "_merged" + Path.GetExtension(recordingPath),
            CreationCollisionOption.ReplaceExisting);

        var keep = false;

        try
        {
            await AudioMixdown.WriteAsync(
                source, written, tracks.MicrophonePath, tracks.SystemPath, answer);

            // Whichever of the two paths is taken below, the merged file is now the
            // recording and must not be swept up by the cleanup.
            keep = true;

            try
            {
                await written.MoveAndReplaceAsync(source);
                return recordingPath;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException)
            {
                // Something still has the recording open — a player the user started while
                // the panel was up, most likely. The merge is real and finished, so it is
                // delivered under its own name rather than thrown away. macshot answers the
                // same failure the same way, in AudioMergeController.swift:203-209.
                DiagnosticLog.Write($"The merged recording could not replace '{recordingPath}': {error.Message}");
                return written.Path;
            }
        }
        finally
        {
            if (!keep)
            {
                // A merge that failed halfway leaves a file beside the recording that is
                // neither the recording nor a merge of it. Named after the recording, it
                // would be taken for one.
                try
                {
                    File.Delete(written.Path);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    DiagnosticLog.Write($"Could not delete '{written.Path}': {error.Message}");
                }
            }
        }
    }
}
