namespace Macshot.Windows.Core.Capture;

/// <summary>Which of the two numbers the user changed.</summary>
public enum SizedDimension
{
    /// <summary>Both, so a locked ratio has nothing to work out.</summary>
    Both,

    Width,

    Height,
}

/// <summary>
/// Resizing the selection to a size that was typed or picked rather than dragged.
/// </summary>
/// <remarks>
/// Centre-anchored throughout. The region under the pointer is the thing the user is
/// looking at, and a resize that pinned a corner would slide it out from under them; one
/// that grows and shrinks around its middle stays where they put it.
/// </remarks>
public static class SelectionSizing
{
    /// <summary>
    /// The selection at exactly <paramref name="width"/> × <paramref name="height"/> where
    /// the screen has room, and at the largest version of that shape where it does not.
    /// </summary>
    /// <remarks>
    /// Scaled down rather than truncated when it will not fit: a preset called 16 : 9 that
    /// came back as something else would be the preset not doing what it says. Nothing is
    /// asked of an impossible size — a zero or a negative leaves the region alone.
    /// </remarks>
    public static CaptureRegion Resize(
        CaptureRegion selection,
        double width,
        double height,
        CaptureRegion bounds)
    {
        if (width <= 0 || height <= 0 || bounds.IsEmpty)
        {
            return selection;
        }

        if (width > bounds.Width || height > bounds.Height)
        {
            var shrink = Math.Min(bounds.Width / width, bounds.Height / height);
            width *= shrink;
            height *= shrink;
        }

        var centerX = selection.Width > 0 ? selection.X + (selection.Width / 2) : bounds.X + (bounds.Width / 2);
        var centerY = selection.Height > 0 ? selection.Y + (selection.Height / 2) : bounds.Y + (bounds.Height / 2);

        return new CaptureRegion(
            Math.Clamp(centerX - (width / 2), bounds.X, bounds.Right - width),
            Math.Clamp(centerY - (height / 2), bounds.Y, bounds.Bottom - height),
            width,
            height);
    }

    /// <summary>
    /// The same, with a ratio held: changing one number works the other out.
    /// </summary>
    /// <remarks>
    /// A locked ratio that only applied when both numbers were retyped would be a lock
    /// that does nothing, since typing a width is the whole reason to have locked the
    /// shape first.
    /// </remarks>
    public static CaptureRegion Resize(
        CaptureRegion selection,
        double width,
        double height,
        CaptureRegion bounds,
        double? aspect,
        SizedDimension edited)
    {
        if (aspect is > 0)
        {
            switch (edited)
            {
                case SizedDimension.Width:
                    height = Math.Max(1, Math.Round(width / aspect.Value));
                    break;

                case SizedDimension.Height:
                    width = Math.Max(1, Math.Round(height * aspect.Value));
                    break;

                default:
                    break;
            }
        }

        return Resize(selection, width, height, bounds);
    }

    /// <summary>
    /// A region a grip has just dragged out, held to <paramref name="aspect"/>.
    /// </summary>
    /// <remarks>
    /// A locked shape that only applied to typed numbers would come apart the first time
    /// anyone touched a grip, which is how the region is adjusted the rest of the time.
    /// The edge or corner being dragged drives one dimension and the other follows: the
    /// side handles drive the axis they move along, and a corner is driven by whichever
    /// axis was dragged furthest, so the region follows the pointer rather than fighting
    /// it. Whatever is opposite the grip stays put, which is what a resize means.
    /// </remarks>
    public static CaptureRegion ConstrainToAspect(
        CaptureRegion dragged,
        double aspect,
        SelectionHandle handle,
        CaptureRegion bounds,
        double minimum = 1)
    {
        if (aspect <= 0 || handle == SelectionHandle.None || dragged.IsEmpty)
        {
            return dragged;
        }

        var width = dragged.Width;
        var height = dragged.Height;

        switch (handle)
        {
            case SelectionHandle.Top or SelectionHandle.Bottom:
                width = height * aspect;
                break;

            case SelectionHandle.Left or SelectionHandle.Right:
                height = width / aspect;
                break;

            default:
                if (width / aspect >= height)
                {
                    height = width / aspect;
                }
                else
                {
                    width = height * aspect;
                }

                break;
        }

        // Both together, or holding the shape would be the first thing given up at the
        // limits — which is where a ratio lock is most obviously either working or not.
        if (width < minimum || height < minimum)
        {
            var grow = Math.Max(minimum / width, minimum / height);
            width *= grow;
            height *= grow;
        }

        if (width > bounds.Width || height > bounds.Height)
        {
            var shrink = Math.Min(bounds.Width / width, bounds.Height / height);
            width *= shrink;
            height *= shrink;
        }

        var left = handle switch
        {
            SelectionHandle.TopLeft or SelectionHandle.Left or SelectionHandle.BottomLeft => dragged.Right - width,
            SelectionHandle.Top or SelectionHandle.Bottom => dragged.X + ((dragged.Width - width) / 2),
            _ => dragged.X,
        };

        var top = handle switch
        {
            SelectionHandle.TopLeft or SelectionHandle.Top or SelectionHandle.TopRight => dragged.Bottom - height,
            SelectionHandle.Left or SelectionHandle.Right => dragged.Y + ((dragged.Height - height) / 2),
            _ => dragged.Y,
        };

        return new CaptureRegion(
            Math.Clamp(left, bounds.X, Math.Max(bounds.X, bounds.Right - width)),
            Math.Clamp(top, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - height)),
            width,
            height);
    }

    /// <summary>
    /// The selection reshaped to <paramref name="aspect"/>, keeping roughly the size it
    /// already had. Locking a ratio is asking for that shape now, not only for the next
    /// drag.
    /// </summary>
    public static CaptureRegion ApplyAspect(CaptureRegion selection, double aspect, CaptureRegion bounds)
    {
        if (aspect <= 0 || selection.Width <= 1 || bounds.IsEmpty)
        {
            return selection;
        }

        // Width is kept and height follows, so the numbers the user is most likely
        // watching — the ones they just dragged out — change as little as possible.
        return Resize(selection, selection.Width, selection.Width / aspect, bounds);
    }
}
