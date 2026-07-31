using System.Globalization;
using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Services;

/// <summary>One past capture: where it is, and when it was taken.</summary>
public sealed record HistoryEntry(string Path, DateTimeOffset TakenAt)
{
    /// <summary>What the tray menu calls it.</summary>
    public string Label => TakenAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
}

/// <summary>
/// Keeps the last few captures on disk, so one dismissed too quickly can still be
/// reached.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately separate from the save directory. A capture is kept here whether or
/// not the user asked for it to be saved, and putting unasked-for files among the
/// ones they chose to keep would make their own folder untrustworthy — so history
/// lives under the app's own data, where deleting the lot costs nothing.
/// </para>
/// <para>
/// The directory listing is the index. A manifest would be a second thing to keep in
/// step with the files, and it would be the half that goes wrong: deleting a file by
/// hand would leave the history pointing at nothing. Sorting by name, which is the
/// timestamp, is all the ordering needed.
/// </para>
/// <para>
/// Every operation is best-effort. History is a convenience, and one that can fail a
/// capture — a locked file, a full disk — is worse than no history at all.
/// </para>
/// </remarks>
public static class ScreenshotHistory
{
    /// <summary>
    /// Stamped to the millisecond, because two captures inside the same second are
    /// ordinary and the name is also the sort key.
    /// </summary>
    private const string NameFormat = "yyyyMMdd-HHmmss-fff";

    private const string Extension = ".png";

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "macshot",
        "history");

    /// <summary>
    /// Writes a capture into the history and prunes whatever falls off the end.
    /// </summary>
    /// <remarks>
    /// Always PNG, whatever the save format is. History exists to get the pixels back,
    /// and keeping the lossy copy of something the user may not have saved anywhere
    /// else would make the archive worse than the thing it archives.
    /// </remarks>
    /// <returns>
    /// Where the copy was written, or null when history is off or the write failed. The
    /// thumbnail carries it so that its Delete can take the capture back out again.
    /// </returns>
    public static async Task<string?> RecordAsync(CapturedFrame frame, CaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.HistorySize <= 0)
        {
            return null;
        }

        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            var name = DateTimeOffset.Now.ToString(NameFormat, CultureInfo.InvariantCulture) + Extension;
            var path = Path.Combine(Directory, name);
            var bytes = await ImageDelivery.EncodeAsync(frame, CaptureImageFormat.Png, CaptureSettings.MaxQuality);
            await File.WriteAllBytesAsync(path, bytes);

            Prune(settings.HistorySize);
            return path;
        }
        catch (Exception exception)
        {
            // Logged rather than shown. The capture itself has already been delivered
            // by the time this runs, so interrupting the user to say the copy of it
            // failed would report a problem they do not have.
            DiagnosticLog.Write($"Could not add the capture to history: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Takes one capture back out of the history, for the thumbnail's Delete.
    /// </summary>
    /// <remarks>
    /// Silent on failure, as recording is: the file may already have been pruned off the
    /// end, and a capture the user asked to be rid of that is gone anyway is not an error
    /// worth a dialog.
    /// </remarks>
    public static void Forget(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"Could not remove the capture from history: {exception.Message}");
        }
    }

    /// <summary>The most recent captures, newest first.</summary>
    public static IReadOnlyList<HistoryEntry> Recent(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        try
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return [];
            }

            return [.. Ordered().Take(count).Select(path => new HistoryEntry(path, TakenAt(path)))];
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"Could not read the capture history: {exception.Message}");
            return [];
        }
    }

    /// <summary>Forgets everything, which is what a user asking for that expects.</summary>
    public static void Clear()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"Could not clear the capture history: {exception.Message}");
        }
    }

    /// <summary>
    /// Newest first. Ordered by name rather than by write time, because the name is
    /// the moment the capture was taken and the write time is the moment the encoder
    /// finished — which two captures in flight at once can put in the wrong order.
    /// </summary>
    private static IEnumerable<string> Ordered()
    {
        return System.IO.Directory
            .EnumerateFiles(Directory, "*" + Extension)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal);
    }

    private static void Prune(int keep)
    {
        foreach (var path in Ordered().Skip(keep))
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                // One file that will not go — open in a viewer, most likely — is no
                // reason to abandon the rest of the prune.
            }
        }
    }

    /// <summary>
    /// When the capture was taken, read back out of its name, falling back to the
    /// file's own timestamp for anything this did not write.
    /// </summary>
    private static DateTimeOffset TakenAt(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return DateTimeOffset.TryParseExact(
            name,
            NameFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var stamp)
            ? stamp
            : File.GetLastWriteTime(path);
    }
}
