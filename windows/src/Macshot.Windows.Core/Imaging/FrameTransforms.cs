using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// The four ways a capture can be turned about without changing a pixel's colour.
/// </summary>
/// <remarks>
/// macshot's <c>ImageContextTransform</c>, in its order
/// (<c>FloatingThumbnailController.swift:12</c>). One list rather than four call sites,
/// because every surface that offers them offers all four.
/// </remarks>
public enum ImageTurn
{
    /// <summary>A quarter turn anticlockwise.</summary>
    Left,

    /// <summary>A quarter turn clockwise.</summary>
    Right,

    /// <summary>Mirrored left to right.</summary>
    FlipHorizontal,

    /// <summary>Mirrored top to bottom.</summary>
    FlipVertical,
}

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
    /// One of the four turns, with the size it leaves behind — which is the source's, the
    /// other way round, for the two quarter turns.
    /// </summary>
    /// <remarks>
    /// The switch is here rather than at each menu, so that a surface offering these has
    /// only to name one and the answer to "what size is it now" comes back with the
    /// pixels instead of being worked out again beside them.
    /// </remarks>
    public static (int Width, int Height, byte[] Pixels) Apply(
        ImageTurn turn,
        int width,
        int height,
        ReadOnlySpan<byte> bgraPixels) => turn switch
        {
            ImageTurn.Left => RotateLeft(width, height, bgraPixels),
            ImageTurn.Right => RotateRight(width, height, bgraPixels),
            ImageTurn.FlipHorizontal => (width, height, FlipHorizontal(width, height, bgraPixels)),
            ImageTurn.FlipVertical => (width, height, FlipVertical(width, height, bgraPixels)),
            _ => throw new ArgumentOutOfRangeException(nameof(turn)),
        };

    /// <summary>
    /// Turns the frame a quarter turn clockwise. The result is as wide as the source was
    /// tall.
    /// </summary>
    /// <remarks>
    /// For a capture taken of something that was not upright — a photograph dropped into
    /// the panel, a phone screen mirrored sideways. macshot offers it from the same menu
    /// as the two flips (<c>FloatingThumbnailController.swift:12</c>), and like them it
    /// moves pixels rather than drawing on them.
    /// </remarks>
    public static (int Width, int Height, byte[] Pixels) RotateRight(
        int width,
        int height,
        ReadOnlySpan<byte> bgraPixels) =>
        Rotate(width, height, bgraPixels, clockwise: true);

    /// <summary>Turns the frame a quarter turn anticlockwise.</summary>
    public static (int Width, int Height, byte[] Pixels) RotateLeft(
        int width,
        int height,
        ReadOnlySpan<byte> bgraPixels) =>
        Rotate(width, height, bgraPixels, clockwise: false);

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
        var (croppedWidth, croppedHeight) = CropSize(width, height, region);
        var output = new byte[checked(croppedWidth * croppedHeight * 4)];
        CropInto(width, height, bgraPixels, region, output);
        return (croppedWidth, croppedHeight, output);
    }

    /// <summary>
    /// The size <see cref="Crop"/> would produce, so a caller can have the buffer ready
    /// before the pixels are.
    /// </summary>
    public static (int Width, int Height) CropSize(int width, int height, CaptureRegion region)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var left = Math.Clamp((int)Math.Floor(region.X), 0, width);
        var top = Math.Clamp((int)Math.Floor(region.Y), 0, height);
        var right = Math.Clamp((int)Math.Ceiling(region.Right), left, width);
        var bottom = Math.Clamp((int)Math.Ceiling(region.Bottom), top, height);

        if (right - left <= 0 || bottom - top <= 0)
        {
            throw new ArgumentException("The crop region does not overlap the frame.", nameof(region));
        }

        return (right - left, bottom - top);
    }

    /// <summary>
    /// <see cref="Crop"/> into a buffer the caller already has, rather than into one of
    /// its own.
    /// </summary>
    /// <remarks>
    /// For the callers that run once per frame of a recording. A crop the size of a 1080p
    /// display is eight megabytes, which is past the large object heap's threshold by two
    /// orders of magnitude; thirty of those a second is a recording that spends its time
    /// collecting rather than encoding. With this the buffer can come from a pool and be
    /// given back when the frame has been handed over.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> is not exactly the size of the crop. Silently
    /// filling part of a buffer would hand the encoder a frame with the last one's pixels
    /// still in the rest of it.
    /// </exception>
    public static void CropInto(
        int width,
        int height,
        ReadOnlySpan<byte> bgraPixels,
        CaptureRegion region,
        Span<byte> destination)
    {
        Validate(width, height, bgraPixels);

        var (croppedWidth, croppedHeight) = CropSize(width, height, region);
        if (destination.Length != checked(croppedWidth * croppedHeight * 4))
        {
            throw new ArgumentException(
                $"A {croppedWidth}x{croppedHeight} crop needs {croppedWidth * croppedHeight * 4} bytes.",
                nameof(destination));
        }

        var left = Math.Clamp((int)Math.Floor(region.X), 0, width);
        var top = Math.Clamp((int)Math.Floor(region.Y), 0, height);

        for (var row = 0; row < croppedHeight; row++)
        {
            var from = (((top + row) * width) + left) * 4;
            bgraPixels.Slice(from, croppedWidth * 4).CopyTo(destination[(row * croppedWidth * 4)..]);
        }
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

    /// <summary>
    /// The two quarter turns, which differ only in where the source's first row lands:
    /// down the destination's right edge going clockwise, up its left edge going the
    /// other way.
    /// </summary>
    private static (int Width, int Height, byte[] Pixels) Rotate(
        int width,
        int height,
        ReadOnlySpan<byte> bgraPixels,
        bool clockwise)
    {
        Validate(width, height, bgraPixels);

        var output = new byte[bgraPixels.Length];
        var turnedStride = height * 4;

        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var turnedColumn = clockwise ? height - 1 - row : row;
                var turnedRow = clockwise ? column : width - 1 - column;

                bgraPixels
                    .Slice((row * width + column) * 4, 4)
                    .CopyTo(output.AsSpan(turnedRow * turnedStride + turnedColumn * 4, 4));
            }
        }

        return (height, width, output);
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
