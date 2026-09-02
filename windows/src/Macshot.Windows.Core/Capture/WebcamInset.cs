namespace Macshot.Windows.Core.Capture;

/// <summary>Which corner of the recorded region the camera sits in.</summary>
/// <remarks>
/// Deliberately not <see cref="Output.ThumbnailCorner"/>, though the four values are the
/// same: macshot keeps two enums for the same reason, and they are two settings that can
/// disagree. Sharing one type would make a change to where thumbnails stack look like a
/// change to where the camera goes.
/// </remarks>
public enum WebcamCorner
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft,
}

public enum WebcamShape
{
    Circle,
    RoundedRect,
}

/// <summary>
/// Where the camera bubble goes inside the recorded region, and what shape it is cut to.
/// </summary>
/// <remarks>
/// macshot's <c>WebcamOverlay.configure</c>. Inside the region rather than beside it,
/// unlike every other thing macshot puts on screen while recording: the HUD and the
/// region border are placed to stay <em>out</em> of the file, and this one is placed to
/// be in it.
/// </remarks>
public static class WebcamInset
{
    /// <summary>How far the bubble is held off the region's edges.</summary>
    public const double Padding = 12;

    /// <summary>The ring round it, so it reads as an inset rather than a hole.</summary>
    public const double BorderWidth = 2;

    /// <summary>
    /// The ends of the size slider, and where it starts, in points.
    /// </summary>
    /// <remarks>
    /// macshot's <c>WebcamSize.minPoints</c>, <c>maxPoints</c> and <c>defaultPoints</c>
    /// (<c>WebcamOverlay.swift:13-16</c>). A slider rather than the four named steps this
    /// used to have, because the steps were sized for one display: 220 points is a large
    /// bubble on a laptop and a dot on a 4K panel, and "roughly how much of the frame" is
    /// a question only the person looking at the frame can answer.
    /// </remarks>
    public const double MinimumSide = 80;

    public const double MaximumSide = 480;

    public const double DefaultSide = 120;

    /// <summary>The chosen size, held to the range the slider offers, in points.</summary>
    /// <remarks>
    /// Rounded first, as macshot's <c>WebcamSize.save(points:)</c> does: the bubble is a
    /// window, and a window is placed in whole pixels whatever the slider was dragged to.
    /// </remarks>
    public static double Clamp(double side) =>
        double.IsFinite(side) ? Math.Clamp(Math.Round(side), MinimumSide, MaximumSide) : DefaultSide;

    /// <summary>
    /// What the bubble's corners are cut to: half the side is a circle, and macshot's
    /// rounded rectangle is a fifth of it.
    /// </summary>
    /// <remarks>
    /// Takes the side actually drawn rather than the one chosen, so the cut follows a
    /// bubble that <see cref="For"/> had to shrink. A circle cut at half of the size that
    /// was asked for is not a circle.
    /// </remarks>
    public static double CornerRadiusFor(double side, WebcamShape shape) =>
        shape is WebcamShape.Circle ? side / 2 : side / 5;

    /// <summary>
    /// The side the bubble is drawn at inside <paramref name="region"/>, in pixels: the
    /// size that was asked for, or as much of it as leaves the padding standing.
    /// </summary>
    /// <remarks>
    /// macshot clamps the same way (<c>WebcamOverlay.swift:89-95</c>), and needs to now
    /// that the size is a slider: 480 points over a 200-pixel region is not a camera in
    /// the corner of a recording, it is a camera instead of one.
    /// </remarks>
    public static double FittedSide(CaptureRegion region, double side, double scale) =>
        Math.Min(
            Clamp(side) * scale,
            Math.Max(1, Math.Min(region.Width, region.Height) - Padding * scale * 2));

    /// <summary>
    /// The bubble's place inside <paramref name="region"/>, in pixels.
    /// </summary>
    /// <remarks>
    /// The region is in screen pixels and so is the answer, so the padding is the only
    /// thing that takes the display's scale — a 12-point inset is 12 points on any
    /// display, while the region's own edges are already where they are.
    /// </remarks>
    public static (int X, int Y, int Width, int Height) For(
        CaptureRegion region,
        WebcamCorner corner,
        double size,
        double scale)
    {
        var side = FittedSide(region, size, scale);
        var pad = Padding * scale;

        var left = corner is WebcamCorner.BottomLeft or WebcamCorner.TopLeft;
        var top = corner is WebcamCorner.TopLeft or WebcamCorner.TopRight;

        var x = left ? region.X + pad : region.X + region.Width - side - pad;
        var y = top ? region.Y + pad : region.Y + region.Height - side - pad;

        return ((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(side), (int)Math.Round(side));
    }
}
