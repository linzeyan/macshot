using Macshot.Windows.Core.Annotations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Point resolves to
// Macshot.Point and does not compile.
using Windows.Foundation;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// The mark each style choice produces, drawn small enough to sit on a segment.
/// </summary>
/// <remarks>
/// <para>
/// macshot draws these with <c>NSBezierPath</c> into an <c>NSImage</c> per segment —
/// <c>ToolOptionsRowView.swift:588–722</c>. Here they are shapes on a canvas for the same
/// reason the toolbar icons are: a picture assembled from lines follows the toolbar's
/// colour, and there is no bitmap to regenerate when the user changes it.
/// </para>
/// <para>
/// Each one is a picture of what <c>AnnotationRasterizer</c> will actually draw,
/// not of what the style is called. That is the whole point of the row — the port's
/// tailed arrow ends in a bar rather than macshot's disc, so the preview shows a bar.
/// </para>
/// </remarks>
internal static class StylePreviews
{
    /// <summary>macshot's line-style image, 28 × 16 — <c>:589</c>.</summary>
    private const double LineWidth = 28;

    /// <summary>macshot's arrow-style image, 24 × 16 — <c>:604</c>.</summary>
    private const double ArrowWidth = 24;

    private const double Extent = 16;

    /// <summary>How wide a line-style segment is — <c>:443</c>.</summary>
    public const double LineSegmentWidth = 36;

    /// <summary>How wide an arrow-style segment is — <c>:486</c>.</summary>
    public const double ArrowSegmentWidth = 30;

    /// <summary>How wide a shape-fill segment is: macshot's 22-wide tile plus its gap.</summary>
    public const double ShapeFillSegmentWidth = 22;

    /// <summary>A dash pattern shown at the weight the preview is drawn in.</summary>
    private const double PreviewStroke = 2;

    public static FrameworkElement Line(LineStyle style)
    {
        var canvas = NewCanvas(LineWidth);
        var line = new Line
        {
            X1 = 4,
            Y1 = Extent / 2,
            X2 = LineWidth - 4,
            Y2 = Extent / 2,
            Stroke = ToolbarPalette.IconBrush(),
            StrokeThickness = PreviewStroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeDashCap = PenLineCap.Round,
        };

        // The same pattern the rasterizer uses, asked for at the preview's own weight so
        // a dashed segment shows the rhythm rather than a scaled-down guess at it.
        // A dash array is in multiples of the stroke, which is what the divide is for.
        var pattern = style.CreateDashPattern(PreviewStroke);
        if (pattern.Count > 0)
        {
            var dashes = new DoubleCollection();
            foreach (var length in pattern)
            {
                dashes.Add(length / PreviewStroke);
            }

            line.StrokeDashArray = dashes;
        }

        canvas.Children.Add(line);
        return canvas;
    }

    public static FrameworkElement Arrow(ArrowStyle style)
    {
        var canvas = NewCanvas(ArrowWidth);
        const double Mid = Extent / 2;
        const double NearX = 3;
        const double FarX = ArrowWidth - 3;

        // How far back a head reaches, and how wide it is at the base: the rasterizer's
        // 30° half-angle at a size that reads at this scale.
        const double Reach = 5;
        const double Spread = 3;

        switch (style)
        {
        case ArrowStyle.Open:
            canvas.Children.Add(Shaft(NearX, Mid, FarX, Mid));
            canvas.Children.Add(Shaft(FarX - Reach, Mid - Spread, FarX, Mid));
            canvas.Children.Add(Shaft(FarX, Mid, FarX - Reach, Mid + Spread));
            break;

        case ArrowStyle.Double:
            canvas.Children.Add(Shaft(NearX + Reach - 1, Mid, FarX - Reach + 1, Mid));
            canvas.Children.Add(Head(FarX, Mid, FarX - Reach, Spread));
            canvas.Children.Add(Head(NearX, Mid, NearX + Reach, Spread));
            break;

        case ArrowStyle.Tail:
            // The bar across the near end, half a head's reach either side of it, which
            // is what TailBar draws.
            canvas.Children.Add(Shaft(NearX, Mid - Spread, NearX, Mid + Spread));
            canvas.Children.Add(Shaft(NearX, Mid, FarX - Reach + 1, Mid));
            canvas.Children.Add(Head(FarX, Mid, FarX - Reach, Spread));
            break;

        default:
            canvas.Children.Add(Shaft(NearX, Mid, FarX - Reach + 1, Mid));
            canvas.Children.Add(Head(FarX, Mid, FarX - Reach, Spread));
            break;
        }

        return canvas;
    }

    /// <summary>
    /// The outline/wash/solid segment: macshot's own preview, which is the shape itself
    /// drawn the three ways — <c>ToolOptionsRowView.swift:723–744</c>.
    /// </summary>
    /// <remarks>
    /// The ellipse tool gets an oval and the rectangle a rounded box, because the segment
    /// is answering "how", not "what", and a rectangle in the ellipse tool's row would be
    /// read as a second shape picker.
    /// </remarks>
    public static FrameworkElement ShapeFillPreview(ShapeFill style, bool oval)
    {
        var canvas = NewCanvas(ShapeFillSegmentWidth);
        var stroke = ToolbarPalette.IconBrush();

        // macshot's 3-in, 2-down inset on a 22x16 tile, at this row's own width.
        const double Inset = 3;
        var box = new Rect(Inset, 2, ShapeFillSegmentWidth - (Inset * 2), Extent - 4);

        Shape shape = oval
            ? new Ellipse { Width = box.Width, Height = box.Height }
            : new Rectangle { Width = box.Width, Height = box.Height, RadiusX = 2, RadiusY = 2 };

        Canvas.SetLeft(shape, box.X);
        Canvas.SetTop(shape, box.Y);

        switch (style)
        {
        case ShapeFill.Fill:
            shape.Fill = stroke;
            break;

        case ShapeFill.StrokeAndFill:
            shape.Fill = ToolbarPalette.IconBrush(0.4);
            shape.Stroke = stroke;
            shape.StrokeThickness = 1.5;
            break;

        default:
            shape.Stroke = stroke;
            shape.StrokeThickness = 1.5;
            break;
        }

        canvas.Children.Add(shape);
        return canvas;
    }

    private static Canvas NewCanvas(double width) => new() { Width = width, Height = Extent };

    private static Line Shaft(double x1, double y1, double x2, double y2) => new()
    {
        X1 = x1,
        Y1 = y1,
        X2 = x2,
        Y2 = y2,
        Stroke = ToolbarPalette.IconBrush(),
        StrokeThickness = 1.5,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    /// <summary>A filled triangle with its tip at (<paramref name="tipX"/>, <paramref name="mid"/>).</summary>
    private static Polygon Head(double tipX, double mid, double baseX, double spread)
    {
        var points = new PointCollection
        {
            new Point(tipX, mid),
            new Point(baseX, mid - spread),
            new Point(baseX, mid + spread),
        };

        return new Polygon { Fill = ToolbarPalette.IconBrush(), Points = points };
    }
}
