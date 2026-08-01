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

/// <summary>How big the camera bubble is.</summary>
/// <remarks>
/// Four steps rather than a slider, because this is picked once and lives inside someone
/// else's recording: the question is "roughly how much of the frame", not "how many
/// pixels".
/// </remarks>
public enum WebcamSize
{
    Small,
    Medium,
    Large,
    ExtraLarge,
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

    public static double SideFor(WebcamSize size) => size switch
    {
        WebcamSize.Small => 80,
        WebcamSize.Large => 160,
        WebcamSize.ExtraLarge => 220,
        _ => 120,
    };

    /// <summary>
    /// What the bubble's corners are cut to: half the side is a circle, and macshot's
    /// rounded rectangle is a fifth of it.
    /// </summary>
    public static double CornerRadiusFor(WebcamSize size, WebcamShape shape) =>
        shape is WebcamShape.Circle ? SideFor(size) / 2 : SideFor(size) / 5;

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
        WebcamSize size,
        double scale)
    {
        var side = SideFor(size) * scale;
        var pad = Padding * scale;

        var left = corner is WebcamCorner.BottomLeft or WebcamCorner.TopLeft;
        var top = corner is WebcamCorner.TopLeft or WebcamCorner.TopRight;

        var x = left ? region.X + pad : region.X + region.Width - side - pad;
        var y = top ? region.Y + pad : region.Y + region.Height - side - pad;

        return ((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(side), (int)Math.Round(side));
    }
}
