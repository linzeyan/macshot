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
    /// How wide the swatch's copy is decoded, in pixels. The swatch is drawn at 28 points
    /// and cropped to fill, so this is generous even at 200%.
    /// </summary>
    private const int SwatchExtent = 128;

    /// <summary>
    /// The picture as it was last read, for the several places that have to hand it to
    /// the renderer — and <c>null</c> whenever it is not the background in use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Held here rather than passed down from whoever opened the window, because the
    /// alternative is that one call site out of five forgets it and that capture silently
    /// comes out on a gradient. Decoding it per repaint is not an option either: it is a
    /// screen-sized PNG.
    /// </para>
    /// <para>
    /// Only while it is in use, though. This used to be decoded at startup unconditionally,
    /// which cost 12.9MB of resident memory for the rest of the session on a 2038x1588
    /// picture — measured on a machine whose <c>beautifyStyleIndex</c> was -1, meaning a
    /// gradient, meaning the picture was never going to be drawn at all. A background
    /// chosen once and moved away from is the commonest case there is.
    /// </para>
    /// </remarks>
    public static BeautifyBackdrop? Current { get; private set; }

    /// <summary>
    /// A small copy for the swatch that offers the picture, which has to be paintable even
    /// when the picture is not the background in use.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Current"/> rather than taken from it, because the swatch is
    /// the one thing that outlives the choice. Painting it from the full decode is what
    /// made opening the frame picker cost a second screen: a <c>WriteableBitmap</c> the size
    /// of the picture, for twenty-eight points of ring.
    /// </remarks>
    public static CapturedFrame? Swatch { get; private set; }

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

    /// <summary>Reads the stored picture into <see cref="Swatch"/>, and into
    /// <see cref="Current"/> when it is going to be drawn.</summary>
    /// <param name="inUse">
    /// Whether the picture is the chosen background. False reads the file and the swatch
    /// and stops there, which is every session that has a picture stored and a gradient
    /// selected.
    /// </param>
    public static async Task RefreshAsync(bool inUse)
    {
        var bytes = await ReadAsync();

        CurrentBytes = bytes;
        Swatch = bytes is null ? null : await DecodeAsync(bytes, SwatchExtent);
        Current = bytes is null || !inUse ? null : await DecodeAsync(bytes);
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

    /// <summary>
    /// The same bytes at swatch size, or null when they are not an image any more.
    /// </summary>
    private static async Task<CapturedFrame?> DecodeAsync(byte[] bytes, int within)
    {
        try
        {
            using var memory = new MemoryStream(bytes, writable: false);
            return await ImageLoader.LoadAsync(memory.AsRandomAccessStream(), within);
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
