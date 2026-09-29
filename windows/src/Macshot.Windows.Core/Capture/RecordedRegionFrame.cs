namespace Macshot.Windows.Core.Capture;

/// <summary>
/// Where the frame round a recorded region is drawn: four bars laid against the outside
/// of the region, cut to the display it is on.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>SelectionBorderOverlay</c>: the accent at 0.8, 1.5 wide, entirely outside
/// the rectangle so that it is never in the file. Its window is the screen, so whatever of
/// the stroke falls off the screen is not drawn — a whole-display recording has no frame
/// at all, and a region against one edge has three sides. Cutting to the display here is
/// that, and it is also what keeps a frame off the monitor next door.
/// </para>
/// <para>
/// The region is rounded outwards before the bars are laid against it, so that however the
/// crop rounds a fractional edge, the bars are still wholly outside it.
/// </para>
/// </remarks>
public static class RecordedRegionFrame
{
    /// <summary>macshot's stroke — <c>SelectionBorderOverlay.swift:50</c>.</summary>
    public const double StrokeDips = 1.5;

    /// <summary>
    /// The bars, in virtual-screen pixels, for <paramref name="region"/> on
    /// <paramref name="display"/> at <paramref name="scale"/>. Top and bottom run the full
    /// width including the corners, so the four close into a rectangle.
    /// </summary>
    public static IReadOnlyList<(int X, int Y, int Width, int Height)> Edges(
        CaptureRegion region,
        CaptureRegion display,
        double scale)
    {
        var stroke = Math.Max(1, (int)Math.Round(StrokeDips * scale));

        var left = (int)Math.Floor(region.X);
        var top = (int)Math.Floor(region.Y);
        var right = (int)Math.Ceiling(region.Right);
        var bottom = (int)Math.Ceiling(region.Bottom);
        var across = right - left + (stroke * 2);

        CaptureRegion[] bars =
        [
            new(left - stroke, top - stroke, across, stroke),
            new(left - stroke, bottom, across, stroke),
            new(left - stroke, top, stroke, bottom - top),
            new(right, top, stroke, bottom - top),
        ];

        return
        [
            .. bars
                .Select(bar => bar.Intersect(display))
                .Where(bar => !bar.IsEmpty)
                .Select(bar => ((int)bar.X, (int)bar.Y, (int)bar.Width, (int)bar.Height)),
        ];
    }
}
