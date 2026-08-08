namespace Macshot.Windows.Core.Capture;

/// <summary>
/// The shape a region being dragged out is held to: a ratio that was locked before the
/// drag began, or the square Shift asks for while it is under way.
/// </summary>
/// <remarks>
/// Separate from <see cref="SelectionHandles"/>, which shapes a drag of one of the eight
/// grips around a region that already exists. This is the first drag, which has no grips
/// and only one moving corner.
/// </remarks>
public static class MarqueeShaping
{
    /// <summary>
    /// Where the corner under the pointer belongs once the drag has been given a shape.
    /// <paramref name="anchor"/> is where the press landed and never moves; the answer is
    /// <paramref name="moving"/> itself when nothing is being held.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A locked ratio wins over Shift. It was chosen deliberately, in a menu, and is meant
    /// to outlast the capture; a key held during one drag should not silently replace it.
    /// macOS resolves the two in the same order (<c>OverlayView.swift:6711-6723</c>).
    /// </para>
    /// <para>
    /// The square is taken from the shorter side rather than the longer one, which is what
    /// keeps the rectangle's corner under the pointer: taken from the longer, the corner
    /// would run ahead of the pointer along the short axis and the region would cover
    /// pixels the user never dragged over.
    /// </para>
    /// </remarks>
    public static CapturePoint Corner(
        CapturePoint anchor,
        CapturePoint moving,
        double? aspect,
        bool square)
    {
        var width = Math.Abs(moving.X - anchor.X);
        var height = Math.Abs(moving.Y - anchor.Y);

        if (aspect is { } ratio && ratio > 0)
        {
            // Whichever axis has been dragged furthest decides the size, so the region
            // follows the pointer rather than being pinned by the axis that happens to
            // be shorter.
            if (width / Math.Max(height, 1) > ratio)
            {
                width = height * ratio;
            }
            else
            {
                height = width / ratio;
            }
        }
        else if (square)
        {
            width = height = Math.Min(width, height);
        }
        else
        {
            return moving;
        }

        return new CapturePoint(
            moving.X > anchor.X ? anchor.X + width : anchor.X - width,
            moving.Y > anchor.Y ? anchor.Y + height : anchor.Y - height);
    }

    /// <summary>
    /// The region an exact-size preset produces: a box of that many pixels centred on
    /// <paramref name="centre"/> and kept inside <paramref name="bounds"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other kind of first drag, and not really a drag at all — the size was chosen in
    /// a menu, so the press only says where. The anchor plays no part, which is why this
    /// takes a point rather than the two <see cref="Corner"/> works between.
    /// </para>
    /// <para>
    /// A preset larger than the display is shrunk to fit rather than clipped or refused: a
    /// 1080 × 1920 box on a 1080p screen would otherwise have edges the user could neither
    /// see nor drag. It stops being that size, which is the lesser of the two lies — the
    /// size box says what it came to. macshot scales it the same way
    /// (<c>OverlayView.swift:6745-6749</c>).
    /// </para>
    /// <para>
    /// Pixels throughout, unlike the macOS original: <c>fixedPreSelectionRect</c> divides
    /// the preset by the backing scale because AppKit's selection is in points, and this
    /// port's regions are already in the display's own pixels.
    /// </para>
    /// </remarks>
    public static CaptureRegion FixedRegion(
        CapturePoint centre,
        double width,
        double height,
        CaptureRegion bounds)
    {
        var boxWidth = Math.Max(1, width);
        var boxHeight = Math.Max(1, height);

        if (boxWidth > bounds.Width || boxHeight > bounds.Height)
        {
            var fit = Math.Min(bounds.Width / boxWidth, bounds.Height / boxHeight);
            boxWidth *= fit;
            boxHeight *= fit;
        }

        return new CaptureRegion(
            Math.Clamp(
                centre.X - (boxWidth / 2),
                bounds.X,
                Math.Max(bounds.X, bounds.Right - boxWidth)),
            Math.Clamp(
                centre.Y - (boxHeight / 2),
                bounds.Y,
                Math.Max(bounds.Y, bounds.Bottom - boxHeight)),
            boxWidth,
            boxHeight);
    }
}
