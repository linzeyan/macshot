using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// Whole-frame rearrangements of a BGRA, top-down buffer: flipping and cropping.
/// </summary>
/// <remarks>
/// These move pixels rather than draw on them, which is why they are not annotations.
/// A flip is not a mark that can sit in the undo stack alongside an arrow — it moves
/// every mark already placed — so the caller rewrites the frame and carries the
/// annotations across with <see cref="FlipPoint"/>, recording one step for the pair.
/// </remarks>
public static class FrameTransforms
{
    /// <summary>Mirrors the frame left to right.</summary>
    public static byte[] FlipHorizontal(int width, int height, ReadOnlySpan<byte> bgraPixels)
    {
        Validate(width, height, bgraPixels);

        var output = new byte[bgraPixels.Length];
        for (var row = 0; row < height; row++)
        {
            var line = row * width * 4;
            for (var column = 0; column < width; column++)
            {
                var from = line + column * 4;
                var to = line + (width - 1 - column) * 4;
                bgraPixels.Slice(from, 4).CopyTo(output.AsSpan(to, 4));
            }
        }

        return output;
    }

    /// <summary>Mirrors the frame top to bottom.</summary>
    public static byte[] FlipVertical(int width, int height, ReadOnlySpan<byte> bgraPixels)
    {
        Validate(width, height, bgraPixels);

        var output = new byte[bgraPixels.Length];
        var stride = width * 4;
        for (var row = 0; row < height; row++)
        {
            bgraPixels.Slice(row * stride, stride).CopyTo(output.AsSpan((height - 1 - row) * stride, stride));
        }

        return output;
    }

    /// <summary>
    /// One frame with another laid under it, left-aligned, on a canvas as wide as the
    /// wider of the two. This is what "add capture" does: the editor grows downwards and
    /// the new capture lands in the space that appeared.
    /// </summary>
    /// <remarks>
    /// Left-aligned rather than centred, as macshot's is, so a column of captures added
    /// one after another lines up down its left edge instead of drifting about the middle.
    /// The gap beside the narrower of the two is left at zero — transparent black — which
    /// the encoders keep and a viewer shows as the background it is.
    /// </remarks>
    public static byte[] StackBelow(
        int width,
        int height,
        ReadOnlySpan<byte> bgraPixels,
        int addedWidth,
        int addedHeight,
        ReadOnlySpan<byte> addedBgraPixels)
    {
        Validate(width, height, bgraPixels);
        Validate(addedWidth, addedHeight, addedBgraPixels);

        var stride = Math.Max(width, addedWidth) * 4;
        var output = new byte[stride * (height + addedHeight)];

        for (var row = 0; row < height; row++)
        {
            bgraPixels.Slice(row * width * 4, width * 4).CopyTo(output.AsSpan(row * stride, width * 4));
        }

        for (var row = 0; row < addedHeight; row++)
        {
            addedBgraPixels
                .Slice(row * addedWidth * 4, addedWidth * 4)
                .CopyTo(output.AsSpan(((height + row) * stride), addedWidth * 4));
        }

        return output;
    }

    /// <summary>
    /// The frame with every colour turned to its opposite.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What it is for is a screenshot of a dark interface being put into a light
    /// document, or the other way round — so it is a straight per-channel inversion
    /// rather than a hue rotation or a "smart" theme swap. Those guess at what the
    /// picture means; this one is reversible, which is what makes pressing the button
    /// twice the way back.
    /// </para>
    /// <para>
    /// Alpha is left alone. Inverting it would turn an opaque capture transparent, and
    /// the byte is undefined in most of what reaches here anyway.
    /// </para>
    /// </remarks>
    public static byte[] Invert(int width, int height, ReadOnlySpan<byte> bgraPixels)
    {
        Validate(width, height, bgraPixels);

        var output = new byte[bgraPixels.Length];
        for (var index = 0; index < bgraPixels.Length; index += 4)
        {
            output[index] = (byte)(255 - bgraPixels[index]);
            output[index + 1] = (byte)(255 - bgraPixels[index + 1]);
            output[index + 2] = (byte)(255 - bgraPixels[index + 2]);
            output[index + 3] = bgraPixels[index + 3];
        }

        return output;
    }

    /// <summary>
    /// The pixels inside <paramref name="region"/>, clamped to the frame.
    /// </summary>
    /// <remarks>
    /// Rounded outwards rather than to nearest. A crop is asked for by dragging a
    /// rectangle around the pixels to keep, and shaving the outermost row off is the
    /// one error the user would see.
    /// </remarks>
    public static (int Width, int Height, byte[] Pixels) Crop(
        int width,
        int height,
        ReadOnlySpan<byte> bgraPixels,
        CaptureRegion region)
    {
        Validate(width, height, bgraPixels);

        var left = Math.Clamp((int)Math.Floor(region.X), 0, width);
        var top = Math.Clamp((int)Math.Floor(region.Y), 0, height);
        var right = Math.Clamp((int)Math.Ceiling(region.Right), left, width);
        var bottom = Math.Clamp((int)Math.Ceiling(region.Bottom), top, height);

        var croppedWidth = right - left;
        var croppedHeight = bottom - top;
        if (croppedWidth <= 0 || croppedHeight <= 0)
        {
            throw new ArgumentException("The crop region does not overlap the frame.", nameof(region));
        }

        var output = new byte[checked(croppedWidth * croppedHeight * 4)];
        for (var row = 0; row < croppedHeight; row++)
        {
            var from = ((top + row) * width + left) * 4;
            bgraPixels.Slice(from, croppedWidth * 4).CopyTo(output.AsSpan(row * croppedWidth * 4));
        }

        return (croppedWidth, croppedHeight, output);
    }

    /// <summary>
    /// Where a frame-space point lands after the matching flip, so annotations can be
    /// carried across rather than left behind on pixels that moved.
    /// </summary>
    public static CapturePoint FlipPoint(CapturePoint point, int width, int height, bool horizontal)
    {
        return horizontal
            ? new CapturePoint(width - point.X, point.Y)
            : new CapturePoint(point.X, height - point.Y);
    }

    private static void Validate(int width, int height, ReadOnlySpan<byte> bgraPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (bgraPixels.Length != checked(width * height * 4))
        {
            throw new ArgumentException("The pixel buffer does not match the frame dimensions.", nameof(bgraPixels));
        }
    }
}
