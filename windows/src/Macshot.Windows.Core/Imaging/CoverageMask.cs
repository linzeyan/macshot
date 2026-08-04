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

    /// <summary>The frame rectangle this mask covers, already clipped to the frame.</summary>
    internal CaptureRegion Bounds => new(Left, Top, Width, Height);

    /// <summary>
    /// A copy of the frame under this mask, to be handed back to
    /// <see cref="KeepInside"/> once something has rewritten it.
    /// </summary>
    internal byte[] Snapshot(byte[] framePixels, int frameWidth)
    {
        var copy = new byte[checked(Width * Height * 4)];
        for (var row = 0; row < Height; row++)
        {
            Buffer.BlockCopy(framePixels, (((Top + row) * frameWidth) + Left) * 4, copy, row * Width * 4, Width * 4);
        }

        return copy;
    }

    /// <summary>
    /// Puts <paramref name="before"/> back wherever this mask does not cover, so an
    /// operation that ran over the whole rectangle only survives inside the shape.
    /// </summary>
    /// <remarks>
    /// What a rotated region effect needs. Blurring or pixelating cannot be done along a
    /// turned axis — every one of them walks rows and columns of the frame — so the effect
    /// runs over the upright rectangle the shape sits in and this takes back the corners
    /// that were never inside it. The mask's fractional edge is kept, so the boundary of a
    /// redaction is as smooth as the shape's own outline rather than a staircase.
    /// </remarks>
    internal void KeepInside(byte[] framePixels, int frameWidth, byte[] before)
    {
        ArgumentNullException.ThrowIfNull(before);

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var coverage = _coverage[(y * Width) + x];
                if (coverage >= 1)
                {
                    continue;
                }

                var from = ((y * Width) + x) * 4;
                var to = (((Top + y) * frameWidth) + Left + x) * 4;
                for (var channel = 0; channel < 4; channel++)
                {
                    framePixels[to + channel] = Blend(before[from + channel], framePixels[to + channel], coverage);
                }
            }
        }
    }

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

    /// <summary>
    /// Adds a filled polygon, antialiased along every edge.
    /// </summary>
    /// <remarks>
    /// Scanline fill with four sample rows per pixel. The horizontal coverage of a span
    /// is exact — a span knows where it starts and ends within a pixel — so only the
    /// vertical direction is sampled, which is where an arrow head's sloping edges need
    /// it and where a stamped disc would be too coarse to help.
    /// </remarks>
    internal void AddPolygon(IReadOnlyList<CapturePoint> polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);

        // Two points enclose no area, so there is nothing to fill.
        if (polygon.Count < 3)
        {
            return;
        }

        const int SamplesPerRow = 4;
        var firstRow = Math.Max(Top, (int)Math.Floor(polygon.Min(point => point.Y)));
        var lastRow = Math.Min(Top + Height - 1, (int)Math.Ceiling(polygon.Max(point => point.Y)));

        var row = new double[Width];
        var crossings = new List<double>(polygon.Count);

        for (var y = firstRow; y <= lastRow; y++)
        {
            Array.Clear(row);

            for (var sample = 0; sample < SamplesPerRow; sample++)
            {
                var sampleY = y + ((sample + 0.5) / SamplesPerRow);
                CrossingsAt(polygon, sampleY, crossings);

                // Pairs, not singles: between one crossing and the next the sample row is
                // inside the polygon, which is what fills a concave shape correctly too.
                for (var pair = 0; pair + 1 < crossings.Count; pair += 2)
                {
                    AddSpan(row, crossings[pair], crossings[pair + 1], 1d / SamplesPerRow);
                }
            }

            for (var x = 0; x < Width; x++)
            {
                Accumulate(Left + x, y, Math.Min(row[x], 1));
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

    /// <summary>
    /// Where the polygon's edges cross one sample row, in order across the mask.
    /// </summary>
    /// <remarks>
    /// An edge counts at its upper end and not its lower one. Counting both would find
    /// two crossings where two edges meet at a vertex, and the fill would stop there.
    /// </remarks>
    private static void CrossingsAt(IReadOnlyList<CapturePoint> polygon, double sampleY, List<double> crossings)
    {
        crossings.Clear();

        for (var index = 0; index < polygon.Count; index++)
        {
            var from = polygon[index];
            var to = polygon[(index + 1) % polygon.Count];
            if (from.Y == to.Y)
            {
                continue;
            }

            var top = Math.Min(from.Y, to.Y);
            var bottom = Math.Max(from.Y, to.Y);
            if (sampleY < top || sampleY >= bottom)
            {
                continue;
            }

            crossings.Add(from.X + ((sampleY - from.Y) / (to.Y - from.Y) * (to.X - from.X)));
        }

        crossings.Sort();
    }

    private void AddSpan(double[] row, double startX, double endX, double weight)
    {
        var first = Math.Max(Left, (int)Math.Floor(startX));
        var last = Math.Min(Left + Width - 1, (int)Math.Ceiling(endX));

        for (var x = first; x <= last; x++)
        {
            var overlap = Math.Clamp(Math.Min(endX, x + 1) - Math.Max(startX, x), 0, 1);
            if (overlap > 0)
            {
                row[x - Left] += overlap * weight;
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
