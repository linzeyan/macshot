using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Services;

/// <summary>
/// Where the picture behind a framed capture is kept, and how it is turned back into
/// pixels the renderer can sample.
/// </summary>
/// <remarks>
/// <para>
/// A copy of the chosen file rather than a path to it. macshot stores the image data
/// itself, in its defaults (<c>OverlayView+Popovers.swift:199</c>), and the difference
/// shows the first time the original is moved: a remembered path leaves the background
/// silently reverting to a gradient weeks later, with nothing to point at. A copy is the
/// same promise the Mac makes — once chosen, it is macshot's.
/// </para>
/// <para>
/// Beside the settings rather than inside them. It is a megabyte of picture; JSON would
/// have to carry it base64-encoded, which triples it and puts it in every settings export.
/// </para>
/// </remarks>
internal static class BeautifyBackgroundStore
{
    /// <summary>
    /// No extension, because whatever the user picked is copied through byte for byte and
    /// the decoder sniffs the format. Naming it .png would be a claim about a file macshot
    /// never re-encoded.
    /// </summary>
    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "macshot",
        "beautify-background");

    public static bool Exists => File.Exists(Path);

    /// <summary>
    /// The picture as it was last read, for the several places that have to hand it to
    /// the renderer.
    /// </summary>
    /// <remarks>
    /// Held here rather than passed down from whoever opened the window, because the
    /// alternative is that one call site out of five forgets it and that capture silently
    /// comes out on a gradient. Decoding it per repaint is not an option either: it is a
    /// screen-sized PNG.
    /// </remarks>
    public static BeautifyBackdrop? Current { get; private set; }

    /// <summary>
    /// The same picture undecoded, for archiving beside a capture that was framed on it.
    /// </summary>
    /// <remarks>
    /// The file's own bytes rather than the decoded pixels re-encoded: what goes into a
    /// capture's sidecar is what the user chose, and re-encoding a screen-sized picture per
    /// capture would cost more than reading it did. Held beside <see cref="Current"/> so
    /// the two cannot disagree about which picture is in use.
    /// </remarks>
    public static byte[]? CurrentBytes { get; private set; }

    /// <summary>Reads the stored picture into <see cref="Current"/>.</summary>
    public static async Task RefreshAsync()
    {
        var bytes = await ReadAsync();

        CurrentBytes = bytes;
        Current = bytes is null ? null : await DecodeAsync(bytes);
    }

    /// <summary>
    /// One picture's bytes as pixels the renderer can sample, or null when they are not an
    /// image any more.
    /// </summary>
    /// <remarks>
    /// Used for the copy archived with a capture as well as for the current one: a capture
    /// framed on a picture carries that picture's bytes, because the one on disk here is
    /// whichever the user last chose and may no longer be the one the capture was delivered
    /// on.
    /// </remarks>
    public static async Task<BeautifyBackdrop?> DecodeAsync(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            using var memory = new MemoryStream(bytes, writable: false);
            var frame = await ImageLoader.LoadAsync(memory.AsRandomAccessStream());
            return new BeautifyBackdrop(frame.Width, frame.Height, frame.BgraPixels);
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"The beautify background could not be read: {exception.Message}");
            return null;
        }
    }

    /// <summary>Takes a copy of <paramref name="sourcePath"/> as the background.</summary>
    public static void Keep(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(sourcePath, Path, overwrite: true);
    }

    /// <summary>
    /// The stored file, or null when there is none or it can no longer be read.
    /// </summary>
    /// <remarks>
    /// Null rather than a throw for an unreadable file: the caller's answer either way is
    /// to draw a gradient, and a frame that quietly falls back is better than a capture
    /// that cannot be taken because of a background nobody is looking at.
    /// </remarks>
    private static async Task<byte[]?> ReadAsync()
    {
        if (!Exists)
        {
            return null;
        }

        try
        {
            return await File.ReadAllBytesAsync(Path);
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"The beautify background could not be read: {exception.Message}");
            return null;
        }
    }
}
