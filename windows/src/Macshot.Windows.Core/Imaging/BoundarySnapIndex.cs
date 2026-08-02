namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// A line in the picture that a selection edge can be pulled onto.
/// </summary>
/// <param name="Position">Where to put the edge, in frame coordinates.</param>
/// <param name="Strength">
/// How hard the colour changes across it. Not used to choose between lines — the nearer
/// one always wins — but it is what decides whether a line is one at all.
/// </param>
public readonly record struct BoundaryHit(double Position, double Strength);

/// <summary>
/// Where the strong colour boundaries in a capture are, so that dragging a selection edge
/// near a window border, a table rule or a toolbar edge lands exactly on it.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>BoundarySnapIndex</c>. Cropping a screenshot to a piece of interface is
/// aiming at a line that is already drawn, and a hand on a mouse is worth about three
/// pixels — so without this, "crop to that panel" comes out with a sliver of what is
/// beside it, or a row of its own border missing.
/// </para>
/// <para>
/// A boundary <c>b</c> is the seam <em>between</em> pixel <c>b-1</c> and pixel <c>b</c>,
/// which is what an edge of a rectangle is: 0 and the width are the picture's own edges
/// and have nothing on either side of them, so the usable range is 1 to width-1.
/// </para>
/// <para>
/// Built once per capture and off the UI thread, because it reads every pixel. The lookup
/// during a drag is a scan of a few columns, which is what makes it usable on a pointer
/// move.
/// </para>
/// </remarks>
public sealed class BoundarySnapIndex
{
    /// <summary>
    /// How different two neighbouring pixels must be, on average across the edge being
    /// dragged, for the seam between them to count as a line — macshot's
    /// <c>minMeanDiff</c>, on the same 0-441 scale as the distance between two RGB
    /// colours.
    /// </summary>
    private const int MinimumMeanDifference = 28;

    /// <summary>
    /// How much of the dragged edge that difference has to run along, so that one bright
    /// pixel in the middle of a photograph is not read as a border.
    /// </summary>
    private const double MinimumSupport = 0.55;

    /// <summary>
    /// The most pixels worth indexing. A capture larger than this is a wall of displays,
    /// where the arrays would cost more than the feature is worth; snapping is simply off
    /// there rather than the capture being slow to open.
    /// </summary>
    private const long MaximumPixels = 40_000_000;

    /// <summary>
    /// How much colour changes across each vertical seam, one byte per pixel row, indexed
    /// <c>[y * (Width + 1) + boundary]</c>.
    /// </summary>
    /// <remarks>
    /// A byte where macshot keeps a float, because here the whole virtual desktop can be
    /// indexed at once and four bytes a pixel twice over is a quarter of a gigabyte on a
    /// pair of 4K displays. Differences above 255 are clipped, which cannot change an
    /// answer: a seam needs 55% of its length individually over 28 before its mean is even
    /// consulted, so no line qualifies on the strength of a few enormous values alone.
    /// </remarks>
    private readonly byte[] _vertical;

    /// <summary>The same across horizontal seams, indexed <c>[boundary * Width + x]</c>.</summary>
    private readonly byte[] _horizontal;

    private BoundarySnapIndex(
        int width,
        int height,
        int originX,
        int originY,
        byte[] vertical,
        byte[] horizontal)
    {
        Width = width;
        Height = height;
        OriginX = originX;
        OriginY = originY;
        _vertical = vertical;
        _horizontal = horizontal;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Where the capture's first pixel sits in frame coordinates.</summary>
    /// <remarks>
    /// One pixel to one frame unit and nothing else to undo — the port's frame space is
    /// desktop pixels, where macshot has to map through the rectangle the screenshot was
    /// drawn into.
    /// </remarks>
    public int OriginX { get; }

    public int OriginY { get; }

    /// <summary>
    /// Reads the boundaries out of a BGRA capture, or answers null when there is nothing
    /// to read.
    /// </summary>
    public static BoundarySnapIndex? Build(
        ReadOnlySpan<byte> bgra,
        int width,
        int height,
        int originX,
        int originY)
    {
        if (width < 2 || height < 2 || (long)width * height > MaximumPixels)
        {
            return null;
        }

        if (bgra.Length < (long)width * height * 4)
        {
            return null;
        }

        var vertical = new byte[height * (width + 1)];
        var horizontal = new byte[height * width];
        var stride = width * 4;

        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            var verticalBase = y * (width + 1);

            for (var x = 1; x < width; x++)
            {
                vertical[verticalBase + x] = Difference(bgra, row + ((x - 1) * 4), row + (x * 4));
            }
        }

        for (var y = 1; y < height; y++)
        {
            var above = (y - 1) * stride;
            var here = y * stride;
            var horizontalBase = y * width;

            for (var x = 0; x < width; x++)
            {
                horizontal[horizontalBase + x] = Difference(bgra, above + (x * 4), here + (x * 4));
            }
        }

        return new BoundarySnapIndex(width, height, originX, originY, vertical, horizontal);
    }

    /// <summary>
    /// The nearest upright line to <paramref name="x"/>, judged along the part of it the
    /// edge being dragged actually spans.
    /// </summary>
    /// <remarks>
    /// Along the span rather than the whole height, so that dragging the left edge of a
    /// selection over a table snaps to the rule that runs beside <em>it</em> and not to
    /// one somewhere else on the screen that happens to be nearer the pointer.
    /// </remarks>
    public BoundaryHit? NearestVertical(double x, double top, double bottom, double radius)
    {
        var from = PixelY(Math.Min(top, bottom));
        var to = PixelY(Math.Max(top, bottom)) - 1;
        return Nearest(
            PixelX(x),
            radius,
            Width,
            Math.Max(0, from),
            Math.Min(Height - 1, to),
            (boundary, along) => _vertical[(along * (Width + 1)) + boundary],
            boundary => OriginX + boundary);
    }

    /// <summary>The nearest level line to <paramref name="y"/>, judged the same way.</summary>
    public BoundaryHit? NearestHorizontal(double y, double left, double right, double radius)
    {
        var from = PixelX(Math.Min(left, right));
        var to = PixelX(Math.Max(left, right)) - 1;
        return Nearest(
            PixelY(y),
            radius,
            Height,
            Math.Max(0, from),
            Math.Min(Width - 1, to),
            (boundary, along) => _horizontal[(boundary * Width) + along],
            boundary => OriginY + boundary);
    }

    private static byte Difference(ReadOnlySpan<byte> bgra, int a, int b)
    {
        double blue = bgra[a] - bgra[b];
        double green = bgra[a + 1] - bgra[b + 1];
        double red = bgra[a + 2] - bgra[b + 2];
        var distance = Math.Sqrt((red * red) + (green * green) + (blue * blue));
        return (byte)Math.Min(255, distance);
    }

    /// <summary>
    /// The nearest qualifying seam within <paramref name="radius"/> of
    /// <paramref name="center"/>, scored across <paramref name="from"/>..<paramref name="to"/>.
    /// </summary>
    private static BoundaryHit? Nearest(
        int center,
        double radius,
        int size,
        int from,
        int to,
        Func<int, int, byte> strength,
        Func<int, double> position)
    {
        if (to < from)
        {
            return null;
        }

        var reach = Math.Max(1, (int)Math.Round(radius));
        var low = Math.Max(1, center - reach);
        var high = Math.Min(size - 1, center + reach);
        var span = to - from + 1;

        BoundaryHit? best = null;
        var nearest = int.MaxValue;

        for (var boundary = low; boundary <= high; boundary++)
        {
            var total = 0;
            var support = 0;

            for (var along = from; along <= to; along++)
            {
                var difference = strength(boundary, along);
                total += difference;

                if (difference >= MinimumMeanDifference)
                {
                    support++;
                }
            }

            var mean = (double)total / span;
            if (mean < MinimumMeanDifference || (double)support / span < MinimumSupport)
            {
                continue;
            }

            // Nearest rather than strongest: the user is aiming at the line under the
            // pointer, and a stronger one two pixels further off is a different line.
            var distance = Math.Abs(boundary - center);
            if (distance < nearest)
            {
                nearest = distance;
                best = new BoundaryHit(position(boundary), mean);
            }
        }

        return best;
    }

    private int PixelX(double x) => Math.Clamp((int)Math.Round(x - OriginX), 0, Width);

    private int PixelY(double y) => Math.Clamp((int)Math.Round(y - OriginY), 0, Height);
}
