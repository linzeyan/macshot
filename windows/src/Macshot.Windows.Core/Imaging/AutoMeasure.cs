namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// How far a run of one colour reaches from a point, along one axis.
/// </summary>
/// <remarks>
/// <para>
/// What the ruler answers with while 1 or 2 is held. Measuring a gap in a screenshot by
/// hand means finding both of its edges with the pointer, and the edges are exactly what
/// is hard to hit: a two-pixel error at each end is a reading four pixels wrong, and there
/// is nothing on screen to say so. The picture already knows where the gap ends — it ends
/// where the colour changes — so the ruler reads it off instead of asking the hand to.
/// </para>
/// <para>
/// macshot's own scan (<c>OverlayView.swift:3943-4053</c>), including its threshold and
/// its stopping rule. Here rather than in the app layer because it is arithmetic over a
/// pixel buffer with no screen in it, which makes it the one part of the feature that can
/// be held to a test.
/// </para>
/// </remarks>
public static class AutoMeasure
{
    /// <summary>
    /// How different a pixel has to be to count as the end of the run: the sum of the
    /// three channel differences, out of 765.
    /// </summary>
    /// <remarks>
    /// macshot's 30, and it is deliberately small. The run being measured is a gap, a
    /// margin, a bar — something one colour — so anything that is visibly a different
    /// colour should stop it, while the dither and the sub-pixel fringing that a
    /// screenshot is full of should not. Raising it makes the ruler run through the edge
    /// it was meant to find.
    /// </remarks>
    public const int DefaultThreshold = 30;

    /// <summary>
    /// The first and last pixel of the run through <paramref name="x"/>,
    /// <paramref name="y"/> along one axis, both inclusive.
    /// </summary>
    /// <param name="bgra">The frame's pixels, four bytes each, blue first.</param>
    /// <param name="width">How many pixels a row of <paramref name="bgra"/> holds.</param>
    /// <param name="height">How many rows it holds.</param>
    /// <param name="x">The column the point is in.</param>
    /// <param name="y">The row the point is in.</param>
    /// <param name="vertical">
    /// True to scan up and down the column — what holding 1 asks for — and false to scan
    /// left and right along the row.
    /// </param>
    /// <param name="threshold">
    /// What counts as a different colour. <see cref="DefaultThreshold"/> unless a caller
    /// has a reason.
    /// </param>
    /// <returns>
    /// The run, or null when the point is outside the frame. A point on a colour that
    /// matches nothing beside it returns a run of one pixel rather than null: that is a
    /// true reading of a one-pixel line, and refusing to answer would be indistinguishable
    /// from the key not working.
    /// </returns>
    public static (int Start, int End)? Run(
        ReadOnlySpan<byte> bgra,
        int width,
        int height,
        int x,
        int y,
        bool vertical,
        int threshold = DefaultThreshold)
    {
        if (width <= 0 || height <= 0 || x < 0 || x >= width || y < 0 || y >= height)
        {
            return null;
        }

        if (bgra.Length < checked(width * height * 4))
        {
            throw new ArgumentException(
                "The pixel buffer is smaller than the frame it is said to hold.",
                nameof(bgra));
        }

        // The colour under the pointer is the one the run is made of, so both directions
        // are compared against it rather than against the pixel before them. Comparing
        // with the neighbour would let a gradient walk the whole width of the capture one
        // imperceptible step at a time.
        var reference = At(bgra, width, x, y);

        var limit = vertical ? height : width;
        var along = vertical ? y : x;

        var start = along;
        for (var scan = along - 1; scan >= 0; scan--)
        {
            if (Differs(At(bgra, width, vertical ? x : scan, vertical ? scan : y), reference, threshold))
            {
                break;
            }

            start = scan;
        }

        var end = along;
        for (var scan = along + 1; scan < limit; scan++)
        {
            if (Differs(At(bgra, width, vertical ? x : scan, vertical ? scan : y), reference, threshold))
            {
                break;
            }

            end = scan;
        }

        return (start, end);
    }

    private static (byte Blue, byte Green, byte Red) At(ReadOnlySpan<byte> bgra, int width, int x, int y)
    {
        var offset = ((y * width) + x) * 4;
        return (bgra[offset], bgra[offset + 1], bgra[offset + 2]);
    }

    /// <summary>
    /// Whether two pixels are different enough to end a run: the sum of the three channel
    /// distances, which is macshot's own measure.
    /// </summary>
    /// <remarks>
    /// Summed rather than taken as a distance, and unweighted rather than by luminance,
    /// because both would change where the ruler stops on the same screenshot the two
    /// products are pointed at. A reading is a number the user compares between them.
    /// </remarks>
    private static bool Differs(
        (byte Blue, byte Green, byte Red) pixel,
        (byte Blue, byte Green, byte Red) reference,
        int threshold) =>
        Math.Abs(pixel.Blue - reference.Blue)
        + Math.Abs(pixel.Green - reference.Green)
        + Math.Abs(pixel.Red - reference.Red) > threshold;
}
