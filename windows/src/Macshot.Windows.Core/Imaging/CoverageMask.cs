using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// Per-pixel coverage for one annotation, sized to that annotation's bounds.
/// </summary>
/// <remarks>
/// Strokes are built by stamping overlapping round caps. Blending each stamp
/// straight into the frame would blend the overlaps twice, so a translucent
/// marker stroke would come out mottled and a dashed stroke would darken at its
/// joints. Accumulating coverage first and compositing once reproduces how
/// <c>NSBezierPath</c> stroking behaves on macOS, and the fractional coverage at
/// the stamp edge gives antialiasing for free.
/// </remarks>
internal sealed class CoverageMask
{
    private readonly float[] _coverage;

    private CoverageMask(int left, int top, int width, int height)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        _coverage = new float[checked(width * height)];
    }

    internal int Left { get; }

    internal int Top { get; }

    internal int Width { get; }

    internal int Height { get; }

    /// <summary>
    /// Allocates a mask covering <paramref name="bounds"/> grown by
    /// <paramref name="inflate"/> and clipped to the frame, or null when the
    /// annotation lies entirely outside the frame.
    /// </summary>
    internal static CoverageMask? ForBounds(CaptureRegion bounds, double inflate, int frameWidth, int frameHeight)
    {
        var left = Math.Max(0, (int)Math.Floor(bounds.X - inflate));
        var top = Math.Max(0, (int)Math.Floor(bounds.Y - inflate));
        var right = Math.Min(frameWidth, (int)Math.Ceiling(bounds.X + bounds.Width + inflate));
        var bottom = Math.Min(frameHeight, (int)Math.Ceiling(bounds.Y + bounds.Height + inflate));
        if (right <= left || bottom <= top)
        {
            return null;
        }

        return new CoverageMask(left, top, right - left, bottom - top);
    }

    internal void AddDisc(double centerX, double centerY, double radius)
    {
        var left = Math.Max(Left, (int)Math.Floor(centerX - radius - 1));
        var right = Math.Min(Left + Width - 1, (int)Math.Ceiling(centerX + radius + 1));
        var top = Math.Max(Top, (int)Math.Floor(centerY - radius - 1));
        var bottom = Math.Min(Top + Height - 1, (int)Math.Ceiling(centerY + radius + 1));

        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var deltaX = x + 0.5 - centerX;
                var deltaY = y + 0.5 - centerY;
                var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

                // One pixel of linear falloff at the edge is a cheap, stable
                // approximation of exact area coverage.
                var coverage = Math.Clamp(radius + 0.5 - distance, 0, 1);
                Accumulate(x, y, coverage);
            }
        }
    }

    /// <summary>Adds an axis-aligned rectangle with exact fractional edge coverage.</summary>
    internal void AddRectangle(CaptureRegion region)
    {
        var left = Math.Max(Left, (int)Math.Floor(region.X));
        var right = Math.Min(Left + Width - 1, (int)Math.Ceiling(region.X + region.Width));
        var top = Math.Max(Top, (int)Math.Floor(region.Y));
        var bottom = Math.Min(Top + Height - 1, (int)Math.Ceiling(region.Y + region.Height));

        for (var y = top; y <= bottom; y++)
        {
            var overlapY = Math.Clamp(Math.Min(region.Y + region.Height, y + 1) - Math.Max(region.Y, y), 0, 1);
            if (overlapY <= 0)
            {
                continue;
            }

            for (var x = left; x <= right; x++)
            {
                var overlapX = Math.Clamp(Math.Min(region.X + region.Width, x + 1) - Math.Max(region.X, x), 0, 1);
                Accumulate(x, y, overlapX * overlapY);
            }
        }
    }

    internal void Composite(byte[] framePixels, int frameWidth, AnnotationColor color, double opacity)
    {
        var alpha = color.Alpha / 255d * opacity;
        if (alpha <= 0)
        {
            return;
        }

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var coverage = _coverage[y * Width + x];
                if (coverage <= 0)
                {
                    continue;
                }

                var blend = coverage * alpha;
                var offset = ((Top + y) * frameWidth + Left + x) * 4;
                framePixels[offset] = Blend(framePixels[offset], color.Blue, blend);
                framePixels[offset + 1] = Blend(framePixels[offset + 1], color.Green, blend);
                framePixels[offset + 2] = Blend(framePixels[offset + 2], color.Red, blend);
                framePixels[offset + 3] = byte.MaxValue;
            }
        }
    }

    private void Accumulate(int frameX, int frameY, double coverage)
    {
        if (coverage <= 0)
        {
            return;
        }

        var index = (frameY - Top) * Width + (frameX - Left);
        if (index < 0 || index >= _coverage.Length)
        {
            return;
        }

        // Maximum, not sum: overlapping stamps of one stroke must not compound.
        if (coverage > _coverage[index])
        {
            _coverage[index] = (float)coverage;
        }
    }

    private static byte Blend(byte destination, byte source, double alpha)
    {
        return (byte)Math.Clamp(Math.Round(destination * (1 - alpha) + source * alpha), byte.MinValue, byte.MaxValue);
    }
}
