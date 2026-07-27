using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// Composites annotations onto a BGRA, top-down frame.
/// </summary>
/// <remarks>
/// This is the headless export and test path. Tools that need font, emoji, or
/// image rendering (text, number, stamp, loupe, measure) are deliberately not
/// handled here and belong to the Win2D draw path; see
/// <c>docs/windows-port/architecture.md</c>, decision D3.
/// </remarks>
public static class AnnotationRasterizer
{
    private const int EllipseMinimumSegments = 32;

    /// <summary>Extra margin around a stroke so its antialiased edge is not clipped.</summary>
    private const double AntialiasMargin = 2;

    public static byte[] Render(
        int width,
        int height,
        ReadOnlySpan<byte> bgraPixels,
        IEnumerable<Annotation> annotations)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(annotations);

        var expectedLength = checked(width * height * 4);
        if (bgraPixels.Length != expectedLength)
        {
            throw new ArgumentException("The pixel buffer does not match the frame dimensions.", nameof(bgraPixels));
        }

        var output = bgraPixels.ToArray();
        foreach (var annotation in annotations)
        {
            annotation.Style.Validate();
            DrawAnnotation(output, width, height, annotation);
        }

        return output;
    }

    private static void DrawAnnotation(byte[] pixels, int width, int height, Annotation annotation)
    {
        switch (annotation.Tool)
        {
        case AnnotationTool.Pencil:
            CompositeStrokes(pixels, width, height, [BuildFreeformPath(annotation)], annotation.Style);
            break;
        case AnnotationTool.Line:
        case AnnotationTool.Marker:
        case AnnotationTool.Highlight:
            CompositeStrokes(pixels, width, height, [[annotation.Start, annotation.End]], annotation.Style);
            break;
        case AnnotationTool.Arrow:
            CompositeStrokes(pixels, width, height, BuildArrowPaths(annotation), annotation.Style);
            break;
        case AnnotationTool.Rectangle:
            CompositeStrokes(pixels, width, height, [BuildRectanglePath(annotation.BoundingRect)], annotation.Style);
            break;
        case AnnotationTool.Ellipse:
            CompositeStrokes(pixels, width, height, [BuildEllipsePath(annotation.BoundingRect)], annotation.Style);
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
        default:
            throw new NotSupportedException($"{annotation.Tool} is not yet rasterizable.");
        }
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

    private static IReadOnlyList<CapturePoint[]> BuildArrowPaths(Annotation annotation)
    {
        var angle = Math.Atan2(
            annotation.End.Y - annotation.Start.Y,
            annotation.End.X - annotation.Start.X);
        var headLength = Math.Max(annotation.Style.StrokeWidth * 4, 10);
        var left = new CapturePoint(
            annotation.End.X - headLength * Math.Cos(angle - Math.PI / 6),
            annotation.End.Y - headLength * Math.Sin(angle - Math.PI / 6));
        var right = new CapturePoint(
            annotation.End.X - headLength * Math.Cos(angle + Math.PI / 6),
            annotation.End.Y - headLength * Math.Sin(angle + Math.PI / 6));

        return
        [
            [annotation.Start, annotation.End],
            [annotation.End, left],
            [annotation.End, right],
        ];
    }

    private static void CompositeStrokes(
        byte[] pixels,
        int width,
        int height,
        IReadOnlyList<CapturePoint[]> paths,
        AnnotationStyle style)
    {
        // Bounds come from the built geometry, not from the annotation's start and
        // end points: an arrow head reaches outside the shaft's bounding box.
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
