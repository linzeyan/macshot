using System.Buffers.Binary;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// The size of a PNG, read out of its first few bytes.
/// </summary>
/// <remarks>
/// The history is a directory of PNGs and no manifest, so the only place an entry's
/// dimensions are recorded is the file itself. Decoding one to ask how big it is would
/// mean decoding every capture in the notification area's menu each time it opens; the
/// IHDR chunk is the first thing in the file and answers in 24 bytes.
/// </remarks>
public static class PngHeader
{
    /// <summary>How much of the file has to be in hand before the size is known.</summary>
    public const int Length = 24;

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Reads the pixel dimensions, or says the bytes were not the start of a PNG.
    /// </summary>
    /// <remarks>
    /// The signature and the chunk name are both checked. A file that only happens to be
    /// named <c>.png</c> would otherwise be reported at whatever two integers sit at those
    /// offsets, which reaches the user as a capture claiming to be 1.8 billion pixels wide.
    /// </remarks>
    public static bool TryReadSize(ReadOnlySpan<byte> head, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (head.Length < Length
            || !head[..Signature.Length].SequenceEqual(Signature)
            || !head.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(head.Slice(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(head.Slice(20, 4));
        return width > 0 && height > 0;
    }
}
