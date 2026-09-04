namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// How much of the editor's image history is worth holding on to.
/// </summary>
/// <remarks>
/// <para>
/// Every image operation — crop, flip, rotate, add a capture, remove a background — keeps
/// the pixels it replaced so it can be undone, and those pixels are the whole picture.
/// macOS keeps every one of them (<c>OverlayView.swift:9508</c>) and gets away with it: an
/// <c>NSImage</c>'s backing store is compressible and pageable, so a history nobody is
/// looking at costs nothing that shows. A .NET <c>byte[]</c> of four megapixels is neither
/// — it is thirty-three megabytes on the large object heap, which is not compacted, so a
/// dozen crops of a 4K screenshot is most of a gigabyte the process is holding and will
/// not give back.
/// </para>
/// <para>
/// The budget is in bytes rather than in steps because bytes are what the complaint is
/// about. A 500x400 capture fits six hundred steps inside it, which is the same as being
/// unbounded and so behaves exactly as macOS does; a 4K one gets fifteen, which is far
/// more image operations than one editing session runs. Nothing is ever dropped that
/// would leave the last operation unable to be undone.
/// </para>
/// </remarks>
public static class ImageUndoBudget
{
    /// <summary>
    /// What the whole image history may weigh. Half a gigabyte: enough that the cap is
    /// invisible at any ordinary capture size, small enough that a machine with 8GB in it
    /// is not being asked for a tenth of its memory to remember a crop.
    /// </summary>
    public const long Bytes = 512L * 1024 * 1024;

    /// <summary>
    /// How many of the oldest steps have to go for <paramref name="sizes"/> — oldest
    /// first, as an undo history is held — to fit inside <paramref name="budget"/>.
    /// </summary>
    public static int OldestToDrop(IReadOnlyList<long> sizes, long budget = Bytes)
    {
        ArgumentNullException.ThrowIfNull(sizes);

        var kept = 0L;

        // Newest backwards: the step that would be undone next is the one that has to
        // survive, so it is the one counted first.
        for (var index = sizes.Count - 1; index >= 0; index--)
        {
            kept += sizes[index];

            // The newest is kept whatever it weighs. An image too big for the budget on
            // its own would otherwise be an operation that cannot be undone at all, which
            // is a worse thing to have done than holding the memory.
            if (kept > budget && index < sizes.Count - 1)
            {
                return index + 1;
            }
        }

        return 0;
    }
}
