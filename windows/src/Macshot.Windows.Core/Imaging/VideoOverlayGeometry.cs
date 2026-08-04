using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// Where a rectangle drawn on the source frame ends up in the exported one.
/// </summary>
/// <remarks>
/// <para>
/// A censor and a caption are both stated as a rectangle normalised against the source's
/// own frame, and both have to land on the thing they were drawn over after the export
/// has cropped for a zoom and scaled to the output size. This is that mapping, and it is
/// the whole of what the two effects share geometrically.
/// </para>
/// <para>
/// macshot does this as <c>censorOutputRect</c>: it transforms the rectangle's corners by
/// the same affine matrix it gave Core Image, then flips y because <c>CIImage</c> counts
/// from the bottom. This port's renderer expresses a zoom as the source rectangle that
/// fills the output (<see cref="VideoZoomSegment.SourceRectAt"/>) rather than as a
/// transform, so the mapping is a division instead of a matrix — the same answer, reached
/// from the end the renderer already has in its hand, and with no y flip because
/// everything here counts from the top.
/// </para>
/// </remarks>
public static class VideoOverlayGeometry
{
    /// <summary>
    /// <paramref name="normalized"/> in pixels of an output frame
    /// <paramref name="outputWidth"/> by <paramref name="outputHeight"/>, given that
    /// <paramref name="crop"/> of the source is what fills it.
    /// </summary>
    /// <param name="normalized">Fractions of the source frame, (0,0) at its top-left.</param>
    /// <param name="crop">
    /// The part of the source that fills the output, in source pixels — the whole frame
    /// when nothing is magnified.
    /// </param>
    public static CaptureRegion OutputRect(
        CaptureRegion normalized,
        CaptureRegion crop,
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight)
    {
        if (crop.Width <= 0 || crop.Height <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
        {
            return default;
        }

        var acrossScale = outputWidth / crop.Width;
        var downScale = outputHeight / crop.Height;

        var left = ((normalized.X * sourceWidth) - crop.X) * acrossScale;
        var top = ((normalized.Y * sourceHeight) - crop.Y) * downScale;

        return new CaptureRegion(
            left,
            top,
            normalized.Width * sourceWidth * acrossScale,
            normalized.Height * sourceHeight * downScale);
    }

    /// <summary>
    /// Where the picture actually sits inside a control that letterboxes it.
    /// </summary>
    /// <remarks>
    /// A <c>MediaPlayerElement</c> stretched uniformly centres the video and leaves bars
    /// on two sides. The censor and caption rectangles are dragged on top of that
    /// control, so without this the rectangle a user draws over a face lands somewhere
    /// else in the file — the error is exactly the width of one bar and is invisible
    /// until the export is played back.
    /// </remarks>
    public static CaptureRegion Letterbox(
        double controlWidth,
        double controlHeight,
        int sourceWidth,
        int sourceHeight)
    {
        if (controlWidth <= 0 || controlHeight <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
        {
            return default;
        }

        var fit = Math.Min(controlWidth / sourceWidth, controlHeight / sourceHeight);
        var width = sourceWidth * fit;
        var height = sourceHeight * fit;

        return new CaptureRegion((controlWidth - width) / 2, (controlHeight - height) / 2, width, height);
    }

    /// <summary>
    /// A rectangle drawn on that letterboxed picture, as fractions of the source frame.
    /// </summary>
    public static CaptureRegion Normalize(CaptureRegion drawn, CaptureRegion letterbox)
    {
        if (letterbox.Width <= 0 || letterbox.Height <= 0)
        {
            return default;
        }

        return new CaptureRegion(
            (drawn.X - letterbox.X) / letterbox.Width,
            (drawn.Y - letterbox.Y) / letterbox.Height,
            drawn.Width / letterbox.Width,
            drawn.Height / letterbox.Height);
    }

    /// <summary>The inverse: a normalised rectangle back onto the letterboxed picture.</summary>
    public static CaptureRegion Denormalize(CaptureRegion normalized, CaptureRegion letterbox) =>
        new(
            letterbox.X + (normalized.X * letterbox.Width),
            letterbox.Y + (normalized.Y * letterbox.Height),
            normalized.Width * letterbox.Width,
            normalized.Height * letterbox.Height);
}
