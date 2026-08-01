namespace Macshot.Windows.Core.Imaging;

/// <summary>What one frame did to the stitched image.</summary>
public enum ScrollStitchOutcome
{
    /// <summary>The first frame, which becomes the image the rest are matched against.</summary>
    Seeded,

    /// <summary>Matched where it already sits: the view has not scrolled since the last frame.</summary>
    Unchanged,

    /// <summary>Matched below its previous position, and the newly revealed rows were appended.</summary>
    Advanced,

    /// <summary>No confident match. The frame is dropped rather than guessed at.</summary>
    Rejected,
}

/// <summary>
/// Builds one tall image out of successive frames of a scrolling view, by finding
/// where each frame overlaps what has been captured so far.
/// </summary>
/// <remarks>
/// <para>
/// The match is sum of absolute differences over a band of rows taken from the top
/// of the incoming frame, searched against the tail of the stitched image. SAD is
/// what the macOS product uses, and it is enough here because the two images are
/// the same pixels shifted, not two photographs of one scene: there is no lighting
/// or scale to be robust against, so anything fancier buys nothing.
/// </para>
/// <para>
/// This is portable Core work on purpose. The part that needs Windows is grabbing
/// the frames and driving the scroll; the stitching is arithmetic, and keeping it
/// here is what lets it be tested against synthetic pages instead of by scrolling a
/// real window and looking at the result.
/// </para>
/// </remarks>
public sealed class ScrollStitcher
{
    /// <summary>
    /// How many rows are matched. Tall enough to be unique on ordinary content,
    /// short enough that a frame arriving mid-scroll still has an unscrolled band at
    /// its top.
    /// </summary>
    public const int BandHeight = 24;

    /// <summary>
    /// Every fourth pixel across is sampled. A full-width comparison costs four
    /// times as much for a match that lands in the same place: neighbouring pixels
    /// on a scrolling page are not independent evidence.
    /// </summary>
    private const int SampleStride = 4;

    /// <summary>
    /// Mean absolute channel difference, out of 255, below which a match is believed.
    /// Frames of a still page differ only by compression and cursor noise, so the
    /// real matches sit near zero and the wrong ones sit far above this.
    /// </summary>
    private const double MatchThreshold = 6;

    /// <summary>
    /// A band flatter than this is refused rather than matched. A blank strip matches
    /// everywhere equally well, and the "best" offset it produces is noise — which is
    /// how a stitched capture ends up with a page of content missing.
    /// </summary>
    private const double MinimumBandContrast = 3;

    private readonly List<byte[]> _rows = [];

    public ScrollStitcher(int width, int frameHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameHeight, BandHeight);

        Width = width;
        FrameHeight = frameHeight;
    }

    public int Width { get; }

    /// <summary>The height of each incoming frame, which every frame has to match.</summary>
    public int FrameHeight { get; }

    /// <summary>Rows captured so far.</summary>
    public int Height => _rows.Count;

    /// <summary>
    /// Matches <paramref name="framePixels"/> against what has been stitched and
    /// appends whatever it reveals.
    /// </summary>
    public ScrollStitchOutcome Add(ReadOnlySpan<byte> framePixels)
    {
        var stride = checked(Width * 4);
        if (framePixels.Length != checked(stride * FrameHeight))
        {
            throw new ArgumentException("The frame does not match the stitcher's dimensions.", nameof(framePixels));
        }

        if (_rows.Count == 0)
        {
            AppendRows(framePixels, 0, FrameHeight);
            return ScrollStitchOutcome.Seeded;
        }

        if (Contrast(framePixels) < MinimumBandContrast)
        {
            return ScrollStitchOutcome.Rejected;
        }

        if (FindBestMatch(framePixels) is not { } match)
        {
            return ScrollStitchOutcome.Rejected;
        }

        // The frame's first row sits at stitched row `match`, so everything past the
        // end of what is already stitched is new.
        var advance = FrameHeight - (Height - match);
        if (advance <= 0)
        {
            return ScrollStitchOutcome.Unchanged;
        }

        AppendRows(framePixels, FrameHeight - advance, FrameHeight);
        return ScrollStitchOutcome.Advanced;
    }

    /// <summary>The stitched image, top-down BGRA, <see cref="Height"/> rows tall.</summary>
    public byte[] ToImage()
    {
        var stride = Width * 4;
        var image = new byte[checked(stride * _rows.Count)];
        for (var row = 0; row < _rows.Count; row++)
        {
            _rows[row].CopyTo(image, row * stride);
        }

        return image;
    }

    /// <summary>
    /// The stitched image so far, shrunk to <paramref name="targetWidth"/> across.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the panel that shows a scroll capture as it lengthens. Composed straight into
    /// the small buffer rather than by scaling the output of <see cref="ToImage"/>: a
    /// page eight thousand rows tall is a thirty-megabyte copy, and this runs every few
    /// frames while the capture is in flight.
    /// </para>
    /// <para>
    /// Nearest neighbour, which is what a 200-wide thumbnail of a screenshot wants — the
    /// panel is read for "has it drifted or stitched the same rows twice", and averaging
    /// is exactly what would hide a one-row seam.
    /// </para>
    /// </remarks>
    /// <returns>Top-down BGRA, <paramref name="targetWidth"/> across, and how tall it is.</returns>
    public (byte[] Pixels, int Width, int Height) ToPreview(int targetWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);

        if (_rows.Count == 0)
        {
            return ([], 0, 0);
        }

        var width = Math.Min(targetWidth, Width);
        var height = Math.Max(1, (int)Math.Round((double)_rows.Count * width / Width));
        var preview = new byte[checked(width * height * 4)];

        for (var row = 0; row < height; row++)
        {
            var source = _rows[Math.Min(_rows.Count - 1, row * _rows.Count / height)];
            var target = row * width * 4;
            for (var column = 0; column < width; column++)
            {
                var from = Math.Min(Width - 1, column * Width / width) * 4;
                preview[target + (column * 4)] = source[from];
                preview[target + (column * 4) + 1] = source[from + 1];
                preview[target + (column * 4) + 2] = source[from + 2];
                preview[target + (column * 4) + 3] = source[from + 3];
            }
        }

        return (preview, width, height);
    }

    /// <summary>
    /// The stitched row the frame's top band matches, or null when nothing matches
    /// well enough.
    /// </summary>
    /// <remarks>
    /// Candidates are walked from the smallest advance upwards and only a strictly
    /// better score displaces the incumbent, so a tie resolves to the smallest
    /// advance. On repeating content — a table of identical rows — that duplicates a
    /// few rows, which is visible in the result and can be undone; resolving the
    /// other way silently drops content nobody knows is missing.
    /// </remarks>
    private int? FindBestMatch(ReadOnlySpan<byte> framePixels)
    {
        // The view cannot have scrolled more than one frame between samples without
        // leaving nothing to match against, and that bounds the search however tall
        // the stitched image has grown.
        var highest = Height - BandHeight;
        var lowest = Math.Max(0, Height - FrameHeight);
        if (highest < lowest)
        {
            return null;
        }

        var bestScore = double.MaxValue;
        var bestRow = -1;
        for (var candidate = highest; candidate >= lowest; candidate--)
        {
            var score = ScoreAt(framePixels, candidate);
            if (score < bestScore)
            {
                bestScore = score;
                bestRow = candidate;
            }
        }

        return bestScore <= MatchThreshold ? bestRow : null;
    }

    /// <summary>
    /// Mean absolute channel difference between the frame's top band and the stitched
    /// rows starting at <paramref name="stitchedRow"/>.
    /// </summary>
    private double ScoreAt(ReadOnlySpan<byte> framePixels, int stitchedRow)
    {
        var stride = Width * 4;
        var total = 0L;
        var samples = 0;

        for (var row = 0; row < BandHeight; row++)
        {
            var frameRow = framePixels.Slice(row * stride, stride);
            var reference = _rows[stitchedRow + row];
            for (var x = 0; x < Width; x += SampleStride)
            {
                var offset = x * 4;

                // Alpha is skipped: the capture backend leaves it undefined, so
                // including it would be comparing noise.
                total += Math.Abs(frameRow[offset] - reference[offset]);
                total += Math.Abs(frameRow[offset + 1] - reference[offset + 1]);
                total += Math.Abs(frameRow[offset + 2] - reference[offset + 2]);
                samples += 3;
            }
        }

        return samples == 0 ? double.MaxValue : (double)total / samples;
    }

    /// <summary>
    /// How much the frame's top band varies around its own mean, over the same
    /// samples the match uses.
    /// </summary>
    private double Contrast(ReadOnlySpan<byte> framePixels)
    {
        var stride = Width * 4;
        var total = 0L;
        var samples = 0;

        for (var row = 0; row < BandHeight; row++)
        {
            var frameRow = framePixels.Slice(row * stride, stride);
            for (var x = 0; x < Width; x += SampleStride)
            {
                var offset = x * 4;
                total += frameRow[offset] + frameRow[offset + 1] + frameRow[offset + 2];
                samples += 3;
            }
        }

        if (samples == 0)
        {
            return 0;
        }

        var mean = (double)total / samples;
        var deviation = 0d;
        for (var row = 0; row < BandHeight; row++)
        {
            var frameRow = framePixels.Slice(row * stride, stride);
            for (var x = 0; x < Width; x += SampleStride)
            {
                var offset = x * 4;
                deviation += Math.Abs(frameRow[offset] - mean);
                deviation += Math.Abs(frameRow[offset + 1] - mean);
                deviation += Math.Abs(frameRow[offset + 2] - mean);
            }
        }

        return deviation / samples;
    }

    private void AppendRows(ReadOnlySpan<byte> framePixels, int fromRow, int toRow)
    {
        var stride = Width * 4;
        for (var row = fromRow; row < toRow; row++)
        {
            _rows.Add(framePixels.Slice(row * stride, stride).ToArray());
        }
    }
}
