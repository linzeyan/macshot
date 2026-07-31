namespace Macshot.Windows.Core.Capture;

/// <summary>
/// Where a pinned capture opens, and what size it becomes as the wheel goes over it.
/// </summary>
/// <remarks>
/// <para>
/// A pin exists to keep a piece of screen visible while working somewhere else, so it
/// opens in the middle of the display rather than over the pixels it was cut from, where
/// it would sit invisibly on top of the thing it is a copy of. It opens no larger than
/// four fifths of the work area: a full-screen capture pinned at 1:1 is a second desktop.
/// </para>
/// <para>
/// Everything here is physical pixels. A scale of 1 is the opening size, which for
/// anything small enough to escape the cap is the capture reproduced pixel for pixel.
/// </para>
/// </remarks>
public static class PinPlacement
{
    /// <summary>How small the pin may be scaled, as a fraction of its opening size.</summary>
    public const double MinScale = 0.1;

    /// <summary>How large it may be scaled.</summary>
    public const double MaxScale = 5.0;

    /// <summary>How much of the work area a pin may cover when it opens.</summary>
    public const double OpeningFraction = 0.8;

    /// <summary>
    /// A change smaller than this is not applied. A wheel notch that would move the
    /// window by less than a pixel still costs a resize, and a stack of them reads as
    /// the window trembling under the pointer.
    /// </summary>
    private const double ScaleEpsilon = 0.001;

    /// <summary>
    /// Where a capture of <paramref name="width"/> × <paramref name="height"/> pixels
    /// opens on the display whose usable area is <paramref name="workArea"/>.
    /// </summary>
    public static CaptureRegion Opening(double width, double height, CaptureRegion workArea)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        // Never enlarged, only shrunk: a 16 × 16 capture pinned at half the screen is a
        // blurry rectangle, and the wheel is there for anyone who wants it bigger.
        var scale = Math.Min(
            1,
            Math.Min(
                workArea.Width * OpeningFraction / width,
                workArea.Height * OpeningFraction / height));

        var opened = new CaptureRegion(0, 0, Math.Round(width * scale), Math.Round(height * scale));

        return opened with
        {
            X = Math.Round(workArea.X + ((workArea.Width - opened.Width) / 2)),
            Y = Math.Round(workArea.Y + ((workArea.Height - opened.Height) / 2)),
        };
    }

    /// <summary>
    /// The window scaled by <paramref name="factor"/> about <paramref name="cursor"/>,
    /// so the pixel under the pointer is still under it afterwards. Returns
    /// <paramref name="current"/> unchanged when the limits leave nothing to do.
    /// </summary>
    /// <param name="current">Where the window is now, in virtual-screen pixels.</param>
    /// <param name="opening">What <see cref="Opening"/> gave, which is scale 1.</param>
    /// <param name="factor">What to multiply the current scale by.</param>
    /// <param name="cursor">The pointer, in virtual-screen pixels.</param>
    public static CaptureRegion Zoomed(
        CaptureRegion current,
        CaptureRegion opening,
        double factor,
        CapturePoint cursor)
    {
        if (opening.IsEmpty || current.IsEmpty)
        {
            return current;
        }

        var scale = current.Width / opening.Width;
        var wanted = Math.Clamp(scale * factor, MinScale, MaxScale);
        if (Math.Abs(wanted - scale) < ScaleEpsilon)
        {
            return current;
        }

        var width = Math.Round(opening.Width * wanted);
        var height = Math.Round(opening.Height * wanted);

        // The fixed point is the pointer brought back onto the window, because a wheel
        // event can arrive from just off it — a pointer capture held through a drag, or a
        // display boundary crossed mid-gesture. Anchoring to the raw point would put the
        // window's edge under a pointer that is nowhere near it.
        var anchorX = Math.Clamp(cursor.X, current.X, current.Right);
        var anchorY = Math.Clamp(cursor.Y, current.Y, current.Bottom);
        var acrossX = (anchorX - current.X) / current.Width;
        var acrossY = (anchorY - current.Y) / current.Height;

        return new CaptureRegion(
            Math.Round(anchorX - (acrossX * width)),
            Math.Round(anchorY - (acrossY * height)),
            width,
            height);
    }

    /// <summary>
    /// Back to scale 1 about the window's own centre, which is what the pointer was
    /// last looking at even though the click landed on the corner.
    /// </summary>
    public static CaptureRegion Restored(CaptureRegion current, CaptureRegion opening)
    {
        return opening with
        {
            X = Math.Round(current.X + ((current.Width - opening.Width) / 2)),
            Y = Math.Round(current.Y + ((current.Height - opening.Height) / 2)),
        };
    }

    /// <summary>The reading for the label, in whole percent.</summary>
    public static int Percent(CaptureRegion current, CaptureRegion opening) =>
        opening.IsEmpty ? 100 : (int)Math.Round(current.Width / opening.Width * 100);
}
