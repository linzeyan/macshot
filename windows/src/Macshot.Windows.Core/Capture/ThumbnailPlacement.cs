namespace Macshot.Windows.Core.Capture;

/// <summary>Which corner of the screen the panels after a capture stack in.</summary>
/// <remarks>macshot's <c>thumbnailCorner</c>, in its order.</remarks>
public enum ThumbnailCorner
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft,
}

/// <summary>
/// Where the panel that appears after a capture goes.
/// </summary>
/// <remarks>
/// Two scales, and they are not the same thing. The display's is how many pixels a
/// layout unit is worth, and everything is measured in it; the user's preview size is a
/// preference about how big the panel should be, and only the panel itself takes it. A
/// margin that grew with the preference would push a large panel off the screen it is
/// meant to be tucked into the corner of.
/// </remarks>
public static class ThumbnailPlacement
{
    public const double Width = 240;
    public const double Height = 160;

    /// <summary>How far the column sits from the corner of the work area.</summary>
    public const double Margin = 16;

    /// <summary>And how far apart two panels in it are.</summary>
    public const double StackGap = 8;

    /// <summary>macshot's slider, half size to double.</summary>
    public const double MinPreviewScale = 0.5;

    public const double MaxPreviewScale = 2.0;

    public static double SanePreviewScale(double scale) =>
        double.IsFinite(scale) ? Math.Clamp(scale, MinPreviewScale, MaxPreviewScale) : 1;

    /// <summary>
    /// The panel's place in the work area, in that display's own pixels.
    /// </summary>
    /// <param name="stackIndex">
    /// Where in the column this one is, counting from the corner. The column always grows
    /// away from the edge it is against — up from a bottom corner, down from a top one —
    /// so the oldest panel is the one nearest the corner whichever corner was chosen.
    /// </param>
    public static (int X, int Y, int Width, int Height) For(
        ThumbnailCorner corner,
        CaptureRegion workArea,
        double previewScale,
        double displayScale,
        int stackIndex)
    {
        var pixels = Math.Max(displayScale, 0.1);
        var preview = SanePreviewScale(previewScale);

        var width = (int)(Width * preview * pixels);
        var height = (int)(Height * preview * pixels);
        var margin = (int)(Margin * pixels);
        var gap = (int)(StackGap * pixels);
        var step = Math.Max(stackIndex, 0) * (height + gap);

        var left = corner is ThumbnailCorner.BottomLeft or ThumbnailCorner.TopLeft;
        var top = corner is ThumbnailCorner.TopLeft or ThumbnailCorner.TopRight;

        var x = left
            ? (int)workArea.X + margin
            : (int)workArea.Right - width - margin;

        var y = top
            ? (int)workArea.Y + margin + step
            : (int)workArea.Bottom - height - margin - step;

        return (x, y, width, height);
    }

    /// <summary>
    /// Which way a flick has to go to throw the panel away: out through the nearest screen
    /// edge, which is the one it is sitting against. -1 is left, 1 is right.
    /// </summary>
    public static int DismissDirection(ThumbnailCorner corner) =>
        corner is ThumbnailCorner.BottomLeft or ThumbnailCorner.TopLeft ? -1 : 1;

    /// <summary>
    /// How far the panel has to be pushed before letting go throws it away, in the same
    /// pixels its frame is in.
    /// </summary>
    /// <remarks>
    /// macshot's <c>dismissCompletionThreshold</c> — a twentieth of the panel's width,
    /// held between 8 and 16. Small on purpose: this is a flick, not a drag, and a
    /// threshold anyone has to aim at is one they will miss and then wonder about.
    /// </remarks>
    public static double DismissThreshold(double width) => Math.Clamp(width * 0.05, 8, 16);

    /// <summary>
    /// How far the push is worth in fading, which is 1.4 times what it is worth in
    /// dismissing — so the panel is not fully faded at the moment it commits, and there is
    /// still something to see being thrown away.
    /// </summary>
    public static double DismissTravel(double width) => DismissThreshold(width) * 1.4;

    /// <summary>How opaque the panel is that far into a flick. macshot fades it to 0.55.</summary>
    public static double DismissOpacity(double pushed, double width) =>
        1 - (Math.Clamp(pushed / DismissTravel(width), 0, 1) * 0.45);
}
