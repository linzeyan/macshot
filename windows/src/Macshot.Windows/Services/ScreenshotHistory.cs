using System.Globalization;
using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Services;

/// <summary>One past capture: where it is, and when it was taken.</summary>
/// <param name="RawPath">
/// The same capture before any mark was drawn on it, when the marks were archived
/// beside it. Null for a capture with none, where the image itself is already the
/// unannotated one.
/// </param>
/// <param name="NotesPath">The marks, as <see cref="AnnotationFile"/> writes them.</param>
public sealed record HistoryEntry(
    string Path,
    DateTimeOffset TakenAt,
    string? RawPath = null,
    string? NotesPath = null)
{
    /// <summary>What the tray menu calls it.</summary>
    public string Label => TakenAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);

    /// <summary>
    /// Whether reopening this gives back the marks as marks rather than as pixels.
    /// </summary>
    public bool IsEditable => RawPath is not null && NotesPath is not null;
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
/// A capture with marks on it is archived three times over: the finished image, the
/// pixels before any mark, and the marks themselves. That is what makes a capture
/// reopened from here <em>editable</em> — the arrow can be moved or taken off, rather
/// than only looked at, which is what someone reopens a capture to do. The two extra
/// files share the entry's name and carry it on, so the listing is still the index: a
/// name with anything between the timestamp and the extension is a companion of the
/// entry it names rather than an entry of its own.
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

    /// <summary>The pixels an entry had before anything was drawn on them.</summary>
    private const string RawSuffix = ".raw.png";

    /// <summary>The marks that were drawn on them.</summary>
    private const string NotesSuffix = ".notes.json";

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
    /// <param name="editable">
    /// The pixels the marks were drawn on and the marks themselves, when the finished
    /// image can be rebuilt from the two. Null when it cannot — a framed capture, say,
    /// where the background is not one of the marks — and then only the finished image
    /// is archived, which is what this did for every capture until now.
    /// </param>
    public static async Task<string?> RecordAsync(
        CapturedFrame frame,
        CaptureSettings settings,
        EditableCapture? editable = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.EffectiveHistorySize <= 0)
        {
            return null;
        }

        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            var stem = DateTimeOffset.Now.ToString(NameFormat, CultureInfo.InvariantCulture);
            var path = Path.Combine(Directory, stem + Extension);
            var bytes = await ImageDelivery.EncodeAsync(frame, CaptureImageFormat.Png, CaptureSettings.MaxQuality);
            await File.WriteAllBytesAsync(path, bytes);

            // Only when there is something to separate. A capture nobody drew on is
            // already its own unannotated copy, and archiving it twice would double what
            // the history costs for the commonest capture there is.
            if (editable is { Annotations.Count: > 0 })
            {
                var raw = await ImageDelivery.EncodeAsync(
                    editable.Raw,
                    CaptureImageFormat.Png,
                    CaptureSettings.MaxQuality);

                await File.WriteAllBytesAsync(Path.Combine(Directory, stem + RawSuffix), raw);
                await File.WriteAllTextAsync(
                    Path.Combine(Directory, stem + NotesSuffix),
                    AnnotationFile.Write(editable.Annotations));
            }

            Prune(settings.EffectiveHistorySize);
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
            Erase(path);
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

            return [.. Ordered().Take(count).Select(Describe)];
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
            .Where(IsEntry)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal);
    }

    /// <summary>
    /// Whether a file is a capture rather than one of a capture's companions.
    /// </summary>
    /// <remarks>
    /// The test is that the whole name before the extension is a timestamp this wrote.
    /// It takes the raw copies out of the listing — <c>….raw.png</c> keeps a stem the
    /// format cannot parse — and it also keeps anything else that has found its way into
    /// the folder from being offered as a past capture.
    /// </remarks>
    private static bool IsEntry(string path) => DateTimeOffset.TryParseExact(
        Path.GetFileNameWithoutExtension(path),
        NameFormat,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeLocal,
        out _);

    /// <summary>One entry, with whatever was archived beside it.</summary>
    private static HistoryEntry Describe(string path)
    {
        var stem = Path.Combine(Directory, Path.GetFileNameWithoutExtension(path));
        var raw = stem + RawSuffix;
        var notes = stem + NotesSuffix;

        // Both or neither. One without the other cannot reopen anything: the marks with
        // no clean pixels to put them back on would draw them twice.
        return File.Exists(raw) && File.Exists(notes)
            ? new HistoryEntry(path, TakenAt(path), raw, notes)
            : new HistoryEntry(path, TakenAt(path));
    }

    private static void Prune(int keep)
    {
        foreach (var path in Ordered().Skip(keep))
        {
            try
            {
                Erase(path);
            }
            catch (Exception)
            {
                // One file that will not go — open in a viewer, most likely — is no
                // reason to abandon the rest of the prune.
            }
        }
    }

    /// <summary>
    /// Takes one capture and everything archived with it off the disk.
    /// </summary>
    /// <remarks>
    /// The companions go with the capture rather than being left to be swept up later.
    /// A raw copy outliving the image it belongs to is invisible — nothing lists it —
    /// and it is the larger of the two files.
    /// </remarks>
    private static void Erase(string path)
    {
        var stem = Path.Combine(
            Path.GetDirectoryName(path) ?? Directory,
            Path.GetFileNameWithoutExtension(path));

        File.Delete(path);
        File.Delete(stem + RawSuffix);
        File.Delete(stem + NotesSuffix);
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
