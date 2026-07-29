using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// Composites annotations onto a BGRA, top-down frame.
/// </summary>
/// <remarks>
/// This is the one draw path: the live preview and the delivered image both come
/// from here, so they cannot disagree. Tools that need font or emoji rendering
/// (text, number, stamp) carry their glyphs as an <see cref="AnnotationSprite"/>
/// the UI layer rasterized, which is what keeps them on this path instead of
/// growing a second one; see <c>docs/windows-port/architecture.md</c>, decisions D3
/// and D7.
/// </remarks>
public static class AnnotationRasterizer
{
    private const int EllipseMinimumSegments = 32;

    /// <summary>Extra margin around a stroke so its antialiased edge is not clipped.</summary>
    private const double AntialiasMargin = 2;

    /// <summary>
    /// The tools this rasterizer draws, which is what the toolbar may offer. A tool
    /// outside this list reaches <see cref="DrawAnnotation"/>'s default case and
    /// throws, so building the toolbar from here is what stops the two drifting
    /// apart.
    /// </summary>
    public static IReadOnlyList<AnnotationTool> SupportedTools { get; } =
    [
        AnnotationTool.Arrow,
        AnnotationTool.Rectangle,
        AnnotationTool.Ellipse,
        AnnotationTool.Line,
        AnnotationTool.Pencil,
        AnnotationTool.Marker,
        AnnotationTool.FilledRectangle,
        AnnotationTool.Pixelate,
        AnnotationTool.Blur,
        AnnotationTool.Text,
        AnnotationTool.Number,
        AnnotationTool.Stamp,
        AnnotationTool.Measure,
        AnnotationTool.Loupe,
    ];

    /// <summary>
    /// How much a loupe enlarges what is under it. Two, as on macOS: enough to read a
    /// hairline, little enough that the circle still shows its surroundings rather
    /// than four fat pixels.
    /// </summary>
    private const double LoupeZoom = 2;

    /// <summary>The bar across each end of a measure, in stroke widths.</summary>
    private const double MeasureCapLength = 3;

    /// <summary>
    /// Segments a bent line is flattened into. Enough that the curve reads as a curve
    /// at any size a capture is taken at, and cheap because each one is only a point.
    /// </summary>
    private const int BendSegments = 24;

    public static byte[] Render(
        int width,
        int height,
        ReadOnlySpan<byte> bgraPixels,
        IEnumerable<Annotation> annotations)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var output = new byte[checked(width * height * 4)];
        RenderInto(width, height, bgraPixels, output, annotations);
        return output;
    }

    /// <summary>
    /// Renders into a caller-owned buffer. The live preview redraws on every pointer
    /// move, and allocating a fresh multi-megabyte frame each time would put the
    /// whole capture under GC pressure for the length of a drag.
    /// </summary>
    public static void RenderInto(
        int width,
        int height,
        ReadOnlySpan<byte> bgraPixels,
        byte[] destination,
        IEnumerable<Annotation> annotations)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(annotations);

        var expectedLength = checked(width * height * 4);
        if (bgraPixels.Length != expectedLength)
        {
            throw new ArgumentException("The pixel buffer does not match the frame dimensions.", nameof(bgraPixels));
        }

        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                "The destination buffer does not match the frame dimensions.",
                nameof(destination));
        }

        bgraPixels.CopyTo(destination);
        foreach (var annotation in annotations)
        {
            annotation.Style.Validate();
            DrawAnnotation(destination, width, height, annotation);
        }
    }

    private static void DrawAnnotation(byte[] pixels, int width, int height, Annotation annotation)
    {
        switch (annotation.Tool)
        {
        case AnnotationTool.Pencil:
            CompositeStrokes(pixels, width, height, [BuildFreeformPath(annotation)], annotation);
            break;
        case AnnotationTool.Line:
        case AnnotationTool.Marker:
        case AnnotationTool.Highlight:
            CompositeStrokes(pixels, width, height, [BuildShaftPath(annotation)], annotation);
            break;
        case AnnotationTool.Arrow:
            CompositeStrokes(pixels, width, height, BuildArrowPaths(annotation), annotation);
            break;
        case AnnotationTool.Measure:
            CompositeStrokes(pixels, width, height, BuildMeasurePaths(annotation), annotation);

            // The reading itself is glyphs, which Core cannot draw. It arrives as a
            // sprite the way text does, and is absent while the ruler is still being
            // dragged out, so this draws whatever is there.
            if (annotation.Sprite is not null)
            {
                CompositeSprite(pixels, width, height, annotation);
            }

            break;
        case AnnotationTool.Loupe:
            PixelEffects.Magnify(pixels, width, height, annotation.BoundingRect, LoupeZoom);
            CompositeStrokes(
                pixels,
                width,
                height,
                [BuildEllipsePath(annotation.BoundingRect)],
                annotation);
            break;
        case AnnotationTool.Rectangle:
            CompositeStrokes(pixels, width, height, [BuildRectanglePath(annotation.BoundingRect)], annotation);
            break;
        case AnnotationTool.Ellipse:
            CompositeStrokes(pixels, width, height, [BuildEllipsePath(annotation.BoundingRect)], annotation);
            break;
        case AnnotationTool.FilledRectangle:
            CompositeFill(pixels, width, height, annotation.BoundingRect, annotation.Style);
            break;
        case AnnotationTool.Pixelate:
            PixelEffects.Pixelate(pixels, width, height, annotation.BoundingRect, PixelateBlockSize(annotation.Style));
            break;
        case AnnotationTool.Blur:
            PixelEffects.Blur(pixels, width, height, annotation.BoundingRect, BlurRadius(annotation.Style));
            break;
        case AnnotationTool.Text:
        case AnnotationTool.Number:
        case AnnotationTool.Stamp:
            CompositeSprite(pixels, width, height, annotation);
            break;
        default:
            throw new NotSupportedException($"{annotation.Tool} is not yet rasterizable.");
        }
    }

    /// <summary>
    /// Draws a sprite one to one at the annotation's top-left. Nothing is resampled:
    /// the sprite was rasterized at capture resolution, and scaling glyph pixels here
    /// would blur exactly the marks whose sharpness is the point.
    /// </summary>
    /// <remarks>
    /// <see cref="AnnotationStyle"/> plays no part. Colour, size, and opacity were
    /// chosen when the sprite was rasterized, so applying them again would apply them
    /// twice.
    /// </remarks>
    private static void CompositeSprite(byte[] pixels, int width, int height, Annotation annotation)
    {
        if (annotation.Sprite is not { } sprite)
        {
            // Fail loudly rather than skip: a sprite tool with no sprite is a bug in
            // whatever committed the annotation, and silently drawing nothing would
            // hand the user an export missing a mark they placed.
            throw new NotSupportedException(
                $"A {annotation.Tool} annotation carries no sprite. Sprites are produced by the UI "
                + "layer when the annotation is committed; see architecture decision D7.");
        }

        var bounds = annotation.BoundingRect;
        var originX = (int)Math.Round(bounds.X);
        var originY = (int)Math.Round(bounds.Y);
        var source = sprite.Pixels;

        for (var row = 0; row < sprite.Height; row++)
        {
            var y = originY + row;
            if (y < 0 || y >= height)
            {
                continue;
            }

            for (var column = 0; column < sprite.Width; column++)
            {
                var x = originX + column;
                if (x < 0 || x >= width)
                {
                    continue;
                }

                var from = (row * sprite.Width + column) * 4;
                var alpha = source[from + 3];
                if (alpha == 0)
                {
                    // Glyph sprites are mostly empty, so this is the common case.
                    continue;
                }

                var to = (y * width + x) * 4;
                pixels[to] = OverPremultiplied(pixels[to], source[from], alpha);
                pixels[to + 1] = OverPremultiplied(pixels[to + 1], source[from + 1], alpha);
                pixels[to + 2] = OverPremultiplied(pixels[to + 2], source[from + 2], alpha);
                pixels[to + 3] = byte.MaxValue;
            }
        }
    }

    /// <summary>
    /// Source-over for a premultiplied source: <c>dst = src + dst × (1 - a)</c>. The
    /// source colour is already scaled by its alpha, so it is added rather than
    /// interpolated; treating it as straight alpha would darken every glyph edge.
    /// </summary>
    private static byte OverPremultiplied(byte destination, byte source, byte sourceAlpha)
    {
        var kept = (destination * (byte.MaxValue - sourceAlpha) + byte.MaxValue / 2) / byte.MaxValue;
        return (byte)Math.Min(byte.MaxValue, source + kept);
    }

    // Intensity is derived from the stroke width until the tool options row can
    // supply an explicit strength, so the existing width slider already controls it.
    private static double PixelateBlockSize(AnnotationStyle style) => Math.Max(4, style.StrokeWidth * 3);

    private static double BlurRadius(AnnotationStyle style) => Math.Max(2, style.StrokeWidth * 2);

    private static CapturePoint[] BuildFreeformPath(Annotation annotation)
    {
        return annotation.Points.Count > 0
            ? [.. annotation.Points]
            : [annotation.Start, annotation.End];
    }

    private static CapturePoint[] BuildRectanglePath(CaptureRegion bounds)
    {
        var left = bounds.X;
        var top = bounds.Y;
        var right = bounds.X + bounds.Width;
        var bottom = bounds.Y + bounds.Height;
        return
        [
            new CapturePoint(left, top),
            new CapturePoint(right, top),
            new CapturePoint(right, bottom),
            new CapturePoint(left, bottom),
            new CapturePoint(left, top),
        ];
    }

    private static CapturePoint[] BuildEllipsePath(CaptureRegion bounds)
    {
        var radiusX = bounds.Width / 2;
        var radiusY = bounds.Height / 2;
        var centerX = bounds.X + radiusX;
        var centerY = bounds.Y + radiusY;
        if (radiusX <= 0 || radiusY <= 0)
        {
            return
            [
                new CapturePoint(bounds.X, bounds.Y),
                new CapturePoint(bounds.X + bounds.Width, bounds.Y + bounds.Height),
            ];
        }

        var segments = Math.Max(EllipseMinimumSegments, (int)Math.Ceiling(Math.PI * (radiusX + radiusY)));
        var path = new CapturePoint[segments + 1];
        for (var segment = 0; segment <= segments; segment++)
        {
            var angle = 2 * Math.PI * segment / segments;
            path[segment] = new CapturePoint(
                centerX + radiusX * Math.Cos(angle),
                centerY + radiusY * Math.Sin(angle));
        }

        return path;
    }

    /// <summary>
    /// The path from one end of a line to the other: straight, or bowed out to the
    /// side when the annotation carries a bend.
    /// </summary>
    /// <remarks>
    /// A quadratic curve rather than the cubic macOS uses. One control point is all a
    /// single drag can describe, and a second would have nothing to set it from.
    /// </remarks>
    private static CapturePoint[] BuildShaftPath(Annotation annotation)
    {
        if (annotation.Bend == 0)
        {
            return [annotation.Start, annotation.End];
        }

        var control = BendControlPoint(annotation);
        var path = new CapturePoint[BendSegments + 1];
        for (var step = 0; step <= BendSegments; step++)
        {
            path[step] = QuadraticAt(annotation.Start, control, annotation.End, (double)step / BendSegments);
        }

        return path;
    }

    /// <summary>
    /// Where a bent line's control point sits: off the midpoint, at right angles to
    /// the straight path, by the bend fraction of that path's length.
    /// </summary>
    /// <remarks>
    /// Doubled because a quadratic curve only reaches halfway to its control point, so
    /// without it the curve would bow by half what the handle was dragged to and the
    /// handle would not sit on the line it is bending.
    /// </remarks>
    private static CapturePoint BendControlPoint(Annotation annotation)
    {
        var deltaX = annotation.End.X - annotation.Start.X;
        var deltaY = annotation.End.Y - annotation.Start.Y;
        var midX = (annotation.Start.X + annotation.End.X) / 2;
        var midY = (annotation.Start.Y + annotation.End.Y) / 2;

        return new CapturePoint(
            midX - (deltaY * annotation.Bend * 2),
            midY + (deltaX * annotation.Bend * 2));
    }

    private static CapturePoint QuadraticAt(CapturePoint start, CapturePoint control, CapturePoint end, double t)
    {
        var inverse = 1 - t;
        var a = inverse * inverse;
        var b = 2 * inverse * t;
        var c = t * t;

        return new CapturePoint(
            (a * start.X) + (b * control.X) + (c * end.X),
            (a * start.Y) + (b * control.Y) + (c * end.Y));
    }

    /// <summary>
    /// A ruler: the span itself, with a bar square across each end so the exact pixel
    /// being measured from is unambiguous.
    /// </summary>
    private static IReadOnlyList<CapturePoint[]> BuildMeasurePaths(Annotation annotation)
    {
        var deltaX = annotation.End.X - annotation.Start.X;
        var deltaY = annotation.End.Y - annotation.Start.Y;
        var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (length <= 0)
        {
            return [[annotation.Start, annotation.End]];
        }

        // Unit vector across the span, which is the direction the end bars run in.
        var acrossX = -deltaY / length;
        var acrossY = deltaX / length;
        var reach = Math.Max(annotation.Style.StrokeWidth * MeasureCapLength, 6);

        return
        [
            [annotation.Start, annotation.End],
            Bar(annotation.Start, acrossX, acrossY, reach),
            Bar(annotation.End, acrossX, acrossY, reach),
        ];
    }

    private static CapturePoint[] Bar(CapturePoint at, double acrossX, double acrossY, double reach)
    {
        return
        [
            new CapturePoint(at.X - (acrossX * reach), at.Y - (acrossY * reach)),
            new CapturePoint(at.X + (acrossX * reach), at.Y + (acrossY * reach)),
        ];
    }

    /// <summary>
    /// The same paths turned about the centre of the annotation's upright bounding
    /// rectangle, which is the point the rotation handle swings around.
    /// </summary>
    private static IReadOnlyList<CapturePoint[]> Oriented(
        Annotation annotation,
        IReadOnlyList<CapturePoint[]> paths)
    {
        if (annotation.Rotation == 0)
        {
            return paths;
        }

        var bounds = annotation.BoundingRect;
        var centerX = bounds.X + (bounds.Width / 2);
        var centerY = bounds.Y + (bounds.Height / 2);
        var sin = Math.Sin(annotation.Rotation);
        var cos = Math.Cos(annotation.Rotation);

        var turned = new CapturePoint[paths.Count][];
        for (var index = 0; index < paths.Count; index++)
        {
            var path = paths[index];
            var rotated = new CapturePoint[path.Length];
            for (var step = 0; step < path.Length; step++)
            {
                var offsetX = path[step].X - centerX;
                var offsetY = path[step].Y - centerY;
                rotated[step] = new CapturePoint(
                    centerX + (offsetX * cos) - (offsetY * sin),
                    centerY + (offsetX * sin) + (offsetY * cos));
            }

            turned[index] = rotated;
        }

        return turned;
    }

    private static IReadOnlyList<CapturePoint[]> BuildArrowPaths(Annotation annotation)
    {
        // Taken from the tangent where the shaft arrives rather than from the straight
        // line between the ends, so a bent arrow's head still points along the curve.
        var shaft = BuildShaftPath(annotation);
        var approach = shaft.Length >= 2 ? shaft[^2] : annotation.Start;

        var angle = Math.Atan2(
            annotation.End.Y - approach.Y,
            annotation.End.X - approach.X);
        var headLength = Math.Max(annotation.Style.StrokeWidth * 4, 10);
        var left = new CapturePoint(
            annotation.End.X - headLength * Math.Cos(angle - Math.PI / 6),
            annotation.End.Y - headLength * Math.Sin(angle - Math.PI / 6));
        var right = new CapturePoint(
            annotation.End.X - headLength * Math.Cos(angle + Math.PI / 6),
            annotation.End.Y - headLength * Math.Sin(angle + Math.PI / 6));

        return
        [
            shaft,
            [annotation.End, left],
            [annotation.End, right],
        ];
    }

    private static void CompositeStrokes(
        byte[] pixels,
        int width,
        int height,
        IReadOnlyList<CapturePoint[]> paths,
        Annotation annotation)
    {
        var style = annotation.Style;
        paths = Oriented(annotation, paths);

        // Bounds come from the built geometry, not from the annotation's start and
        // end points: an arrow head reaches outside the shaft's bounding box, and a
        // rotated shape outside its upright one.
        if (!TryGetPathBounds(paths, out var bounds))
        {
            return;
        }

        var mask = CoverageMask.ForBounds(bounds, style.StrokeWidth / 2 + AntialiasMargin, width, height);
        if (mask is null)
        {
            return;
        }

        foreach (var path in paths)
        {
            StrokePath(mask, path, style);
        }

        mask.Composite(pixels, width, style.Color, style.Opacity);
    }

    private static void CompositeFill(
        byte[] pixels,
        int width,
        int height,
        CaptureRegion bounds,
        AnnotationStyle style)
    {
        var mask = CoverageMask.ForBounds(bounds, 1, width, height);
        if (mask is null)
        {
            return;
        }

        mask.AddRectangle(bounds);
        mask.Composite(pixels, width, style.Color, style.Opacity);
    }

    private static void StrokePath(CoverageMask mask, CapturePoint[] path, AnnotationStyle style)
    {
        if (path.Length == 0)
        {
            return;
        }

        var polyline = new Polyline(path);
        var radius = Math.Max(0.5, style.StrokeWidth / 2);

        // Half a radius between stamps keeps the stroke gap-free without stamping
        // far more discs than the stroke width warrants.
        var step = Math.Clamp(radius / 2, 0.25, 1);
        var total = polyline.Length;
        if (total <= 0)
        {
            mask.AddDisc(path[0].X, path[0].Y, radius);
            return;
        }

        var pattern = style.LineStyle.CreateDashPattern(style.StrokeWidth);
        if (pattern.Count == 0 || pattern.Sum() <= 0)
        {
            StampRange(mask, polyline, 0, total, radius, step);
            return;
        }

        var cursor = 0d;
        var index = 0;
        var inked = true;
        while (cursor < total)
        {
            var run = pattern[index];
            if (inked)
            {
                if (run <= 0)
                {
                    // A zero length "on" run is exactly how the dotted style is
                    // expressed: it must still deposit one round cap. Treating it as
                    // an empty range is what made dotted strokes render as nothing.
                    var dot = polyline.PointAt(cursor);
                    mask.AddDisc(dot.X, dot.Y, radius);
                }
                else
                {
                    StampRange(mask, polyline, cursor, Math.Min(cursor + run, total), radius, step);
                }
            }

            cursor += run;
            index = (index + 1) % pattern.Count;
            inked = !inked;
        }
    }

    private static void StampRange(
        CoverageMask mask,
        Polyline polyline,
        double from,
        double to,
        double radius,
        double step)
    {
        for (var distance = from; distance < to; distance += step)
        {
            var point = polyline.PointAt(distance);
            mask.AddDisc(point.X, point.Y, radius);
        }

        var end = polyline.PointAt(to);
        mask.AddDisc(end.X, end.Y, radius);
    }

    private static bool TryGetPathBounds(IReadOnlyList<CapturePoint[]> paths, out CaptureRegion bounds)
    {
        var minX = double.MaxValue;
        var maxX = double.MinValue;
        var minY = double.MaxValue;
        var maxY = double.MinValue;
        var found = false;

        foreach (var path in paths)
        {
            foreach (var point in path)
            {
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
                found = true;
            }
        }

        bounds = found ? CaptureRegion.FromPoints(minX, minY, maxX, maxY) : default;
        return found;
    }

    /// <summary>A polyline that can be sampled by arc length, which is what dashing needs.</summary>
    private sealed class Polyline
    {
        private readonly CapturePoint[] _points;
        private readonly double[] _cumulative;

        internal Polyline(CapturePoint[] points)
        {
            _points = points;
            _cumulative = new double[points.Length];
            for (var index = 1; index < points.Length; index++)
            {
                var deltaX = points[index].X - points[index - 1].X;
                var deltaY = points[index].Y - points[index - 1].Y;
                _cumulative[index] = _cumulative[index - 1] + Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            }
        }

        internal double Length => _cumulative.Length == 0 ? 0 : _cumulative[^1];

        internal CapturePoint PointAt(double distance)
        {
            if (_points.Length == 1 || distance <= 0)
            {
                return _points[0];
            }

            if (distance >= Length)
            {
                return _points[^1];
            }

            var segment = Array.BinarySearch(_cumulative, distance);
            if (segment < 0)
            {
                segment = ~segment;
            }

            segment = Math.Clamp(segment, 1, _points.Length - 1);
            var segmentLength = _cumulative[segment] - _cumulative[segment - 1];
            if (segmentLength <= 0)
            {
                return _points[segment];
            }

            var progress = (distance - _cumulative[segment - 1]) / segmentLength;
            var start = _points[segment - 1];
            var end = _points[segment];
            return new CapturePoint(
                start.X + (end.X - start.X) * progress,
                start.Y + (end.Y - start.Y) * progress);
        }
    }
}
