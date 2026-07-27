using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Windows.UI.Color
// resolves to Macshot.Windows.UI.Color and does not compile.
using Windows.Foundation;
using Windows.UI;

namespace Macshot.Windows.Rendering;

/// <summary>
/// Draws annotations onto a XAML canvas for live preview during a capture.
/// </summary>
/// <remarks>
/// WinUI shapes rather than Win2D. No Win2D release is built against Windows App
/// SDK 2.x, so its runtime behavior cannot be verified here, and putting the whole
/// annotation UI on top of an unverifiable native renderer would make every later
/// failure ambiguous. Preview and export therefore run through different code, but
/// both consume the same frame-space geometry from the same
/// <see cref="Annotation"/>, so they agree on shape and position and differ only
/// in antialiasing. See <c>docs/windows-port/architecture.md</c>, decision D3.
/// </remarks>
public sealed class ShapeAnnotationRenderer
{
    /// <summary>Arrow head length as a multiple of the stroke width, matching the exporter.</summary>
    private const double ArrowHeadLengthFactor = 3.5;

    private const double ArrowHeadAngle = Math.PI / 7;

    private readonly Canvas _layer;
    private readonly MonitorLayout _layout;
    private readonly CaptureMonitor _monitor;

    public ShapeAnnotationRenderer(Canvas layer, MonitorLayout layout, CaptureMonitor monitor)
    {
        _layer = layer ?? throw new ArgumentNullException(nameof(layer));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
    }

    /// <summary>Tools this renderer can show, which is what the toolbar may offer.</summary>
    public static IReadOnlyList<AnnotationTool> SupportedTools { get; } =
    [
        AnnotationTool.Arrow,
        AnnotationTool.Rectangle,
        AnnotationTool.Ellipse,
        AnnotationTool.Line,
        AnnotationTool.Pencil,
        AnnotationTool.Marker,
        AnnotationTool.FilledRectangle,
    ];

    public void Render(IEnumerable<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        // Rebuilt wholesale rather than diffed: a capture carries a handful of
        // annotations, so reconciling identity would cost more than it saves.
        _layer.Children.Clear();
        foreach (var annotation in annotations)
        {
            foreach (var shape in Build(annotation))
            {
                _layer.Children.Add(shape);
            }
        }
    }

    private IEnumerable<Shape> Build(Annotation annotation)
    {
        switch (annotation.Tool)
        {
            case AnnotationTool.Pencil:
                yield return BuildPolyline(annotation, annotation.Points);
                break;

            case AnnotationTool.Line:
            case AnnotationTool.Marker:
            case AnnotationTool.Highlight:
                yield return BuildPolyline(annotation, [annotation.Start, annotation.End]);
                break;

            case AnnotationTool.Arrow:
                foreach (var shape in BuildArrow(annotation))
                {
                    yield return shape;
                }

                break;

            case AnnotationTool.Rectangle:
                yield return BuildBox(annotation, new Rectangle());
                break;

            case AnnotationTool.Ellipse:
                yield return BuildBox(annotation, new Ellipse());
                break;

            case AnnotationTool.FilledRectangle:
            {
                var plate = new Rectangle { Fill = CreateBrush(annotation) };
                Place(plate, annotation.BoundingRect);
                yield return plate;
                break;
            }

            default:
                // Loud on purpose. The toolbar is built from SupportedTools, so
                // reaching here means the two lists drifted apart, and silently
                // skipping would show the user a mark that is not there.
                throw new NotSupportedException($"{annotation.Tool} has no live preview.");
        }
    }

    private IEnumerable<Shape> BuildArrow(Annotation annotation)
    {
        yield return BuildPolyline(annotation, [annotation.Start, annotation.End]);

        var deltaX = annotation.End.X - annotation.Start.X;
        var deltaY = annotation.End.Y - annotation.Start.Y;
        if (deltaX == 0 && deltaY == 0)
        {
            // A zero length arrow has no direction, so a head would point anywhere.
            yield break;
        }

        var angle = Math.Atan2(deltaY, deltaX);
        var length = annotation.Style.StrokeWidth * ArrowHeadLengthFactor;

        // One open polyline through the tip draws both barbs with a single join,
        // which looks closer to the exporter than two separate strokes.
        yield return BuildPolyline(
            annotation,
            [
                HeadPoint(annotation.End, angle + Math.PI - ArrowHeadAngle, length),
                annotation.End,
                HeadPoint(annotation.End, angle + Math.PI + ArrowHeadAngle, length),
            ]);
    }

    private static CapturePoint HeadPoint(CapturePoint tip, double angle, double length) =>
        new(tip.X + (Math.Cos(angle) * length), tip.Y + (Math.Sin(angle) * length));

    private Polyline BuildPolyline(Annotation annotation, IReadOnlyList<CapturePoint> points)
    {
        var shape = new Polyline();
        ApplyStroke(shape, annotation);
        foreach (var point in points)
        {
            shape.Points.Add(ToPointer(point));
        }

        return shape;
    }

    private Shape BuildBox(Annotation annotation, Shape shape)
    {
        ApplyStroke(shape, annotation);
        Place(shape, annotation.BoundingRect);
        return shape;
    }

    private void Place(Shape shape, CaptureRegion frameBounds)
    {
        // Both corners are converted rather than scaling the width, so the shape
        // lands exactly where the pointer put it even when the scale is not a whole
        // number.
        var topLeft = ToPointer(new CapturePoint(frameBounds.X, frameBounds.Y));
        var bottomRight = ToPointer(new CapturePoint(frameBounds.Right, frameBounds.Bottom));

        Canvas.SetLeft(shape, topLeft.X);
        Canvas.SetTop(shape, topLeft.Y);
        shape.Width = Math.Max(0, bottomRight.X - topLeft.X);
        shape.Height = Math.Max(0, bottomRight.Y - topLeft.Y);
    }

    private void ApplyStroke(Shape shape, Annotation annotation)
    {
        shape.Stroke = CreateBrush(annotation);

        // Stroke width is stored in capture pixels but XAML draws in layout units,
        // so a 3px stroke on a 200% display must be 1.5 units wide to come back the
        // same thickness once the export rasterizes at capture resolution.
        shape.StrokeThickness = annotation.Style.StrokeWidth / _monitor.Scale;
        shape.StrokeLineJoin = PenLineJoin.Round;
        shape.StrokeStartLineCap = PenLineCap.Round;
        shape.StrokeEndLineCap = PenLineCap.Round;

        switch (annotation.Style.LineStyle)
        {
            case LineStyle.Dashed:
                shape.StrokeDashArray = new DoubleCollection { 4, 3 };
                break;

            case LineStyle.Dotted:
                // A dot is a zero length dash finished with a round cap. A literal
                // zero collapses to nothing, so the run is nominal rather than zero.
                shape.StrokeDashArray = new DoubleCollection { 0.01, 2 };
                shape.StrokeDashCap = PenLineCap.Round;
                break;

            case LineStyle.Solid:
            default:
                break;
        }
    }

    private static Brush CreateBrush(Annotation annotation)
    {
        var color = annotation.Style.Color;
        var alpha = (byte)Math.Clamp(Math.Round(color.Alpha * annotation.Style.Opacity), 0, byte.MaxValue);
        return new SolidColorBrush(new Color
        {
            A = alpha,
            R = color.Red,
            G = color.Green,
            B = color.Blue,
        });
    }

    private Point ToPointer(CapturePoint framePoint)
    {
        var pointer = _layout.FrameToPointer(_monitor, framePoint);
        return new Point(pointer.X, pointer.Y);
    }
}
