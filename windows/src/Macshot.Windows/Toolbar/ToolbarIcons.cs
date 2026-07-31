using Macshot.Windows.Core.Annotations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Rect resolves to
// Macshot.Rect and does not compile.
using Windows.Foundation;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// The picture on each toolbar button.
/// </summary>
/// <remarks>
/// <para>
/// Shapes rather than words, the way macshot's toolbar is a row of SF Symbols: a row of
/// word buttons is wide, slow to scan, and tells a beginner nothing they did not already
/// know from the word. The word survives as the tooltip.
/// </para>
/// <para>
/// Built from <c>Line</c>, <c>Rectangle</c> and <c>Ellipse</c> rather than icon-font
/// codepoints. A codepoint written without a Windows to look at renders as an empty box
/// when it is wrong, which is worse than the word it replaced — and half of these have
/// no glyph in the icon font anyway. Where a tool's mark can be drawn, the icon is that
/// mark rather than a picture of an instrument.
/// </para>
/// </remarks>
internal static class ToolbarIcons
{
    private const double Extent = ToolbarPalette.IconExtent;

    /// <summary>The icon for a button, or null when the button draws itself.</summary>
    public static FrameworkElement? For(ToolbarItem item) => item.Command switch
    {
        // The colour button is a swatch of the colour itself, which the button draws.
        ToolbarCommand.PickColor => null,

        ToolbarCommand.PickTool => item.Tool is { } tool ? ForTool(tool) : null,
        _ => ForCommand(item.Command),
    };

    private static FrameworkElement ForCommand(ToolbarCommand command)
    {
        var canvas = NewCanvas();

        switch (command)
        {
        case ToolbarCommand.Undo:
            // An arrow turning back on itself: the shaft points left, the hook above it
            // is what separates undo from a plain back arrow.
            canvas.Children.Add(Stroke(4, 9, 12, 9));
            canvas.Children.Add(Stroke(4, 9, 7, 6));
            canvas.Children.Add(Stroke(4, 9, 7, 12));
            canvas.Children.Add(Stroke(12, 9, 12, 6));
            break;

        case ToolbarCommand.Redo:
            canvas.Children.Add(Stroke(12, 9, 4, 9));
            canvas.Children.Add(Stroke(12, 9, 9, 6));
            canvas.Children.Add(Stroke(12, 9, 9, 12));
            canvas.Children.Add(Stroke(4, 9, 4, 6));
            break;

        case ToolbarCommand.Cancel:
            canvas.Children.Add(Stroke(4, 4, 12, 12, thickness: 1.8));
            canvas.Children.Add(Stroke(12, 4, 4, 12, thickness: 1.8));
            break;

        case ToolbarCommand.MoveSelection:
            // Arrows the four ways the region can go, which is what the button does
            // while it is held.
            canvas.Children.Add(Stroke(8, 2, 8, 14));
            canvas.Children.Add(Stroke(2, 8, 14, 8));
            canvas.Children.Add(Stroke(8, 2, 5.5, 4.5));
            canvas.Children.Add(Stroke(8, 2, 10.5, 4.5));
            canvas.Children.Add(Stroke(8, 14, 5.5, 11.5));
            canvas.Children.Add(Stroke(8, 14, 10.5, 11.5));
            canvas.Children.Add(Stroke(2, 8, 4.5, 5.5));
            canvas.Children.Add(Stroke(2, 8, 4.5, 10.5));
            canvas.Children.Add(Stroke(14, 8, 11.5, 5.5));
            canvas.Children.Add(Stroke(14, 8, 11.5, 10.5));
            break;

        case ToolbarCommand.OpenEditor:
            // A window with an arrow leaving it: the capture carries on somewhere else.
            canvas.Children.Add(Frame(2, 5, 9, 9));
            canvas.Children.Add(Stroke(8, 8, 14, 2));
            canvas.Children.Add(Stroke(14, 2, 9.5, 2));
            canvas.Children.Add(Stroke(14, 2, 14, 6.5));
            break;

        case ToolbarCommand.Copy:
            // Two sheets, one behind the other.
            canvas.Children.Add(Frame(2, 2, 9, 9, opacity: 0.6));
            canvas.Children.Add(Frame(5, 5, 9, 9));
            break;

        case ToolbarCommand.Save:
            // Into a tray: the arrow says down, the bracket says it lands somewhere.
            canvas.Children.Add(Stroke(8, 2, 8, 10));
            canvas.Children.Add(Stroke(8, 10, 5, 7));
            canvas.Children.Add(Stroke(8, 10, 11, 7));
            canvas.Children.Add(Stroke(3, 12.5, 13, 12.5));
            canvas.Children.Add(Stroke(3, 12.5, 3, 10.5));
            canvas.Children.Add(Stroke(13, 12.5, 13, 10.5));
            break;

        case ToolbarCommand.Pin:
            // A pin seen from the side: head, shaft, point.
            canvas.Children.Add(Stroke(5, 3, 11, 3, thickness: 2));
            canvas.Children.Add(Stroke(8, 3, 8, 10));
            canvas.Children.Add(Stroke(6, 10, 10, 10));
            canvas.Children.Add(Stroke(8, 10, 8, 14, thickness: 1.2));
            break;

        case ToolbarCommand.ReadText:
            // Lines of text inside a page, which is what the tool goes looking for.
            canvas.Children.Add(Frame(2, 2, 12, 12, opacity: 0.5));
            canvas.Children.Add(Stroke(4.5, 6, 11.5, 6));
            canvas.Children.Add(Stroke(4.5, 8.5, 11.5, 8.5));
            canvas.Children.Add(Stroke(4.5, 11, 8.5, 11));
            break;

        case ToolbarCommand.Adjust:
            // Three sliders at different settings, which is macshot's
            // slider.horizontal.3 and is what the popover behind the button holds.
            canvas.Children.Add(Stroke(2, 4, 14, 4, opacity: 0.5));
            canvas.Children.Add(Stroke(2, 8, 14, 8, opacity: 0.5));
            canvas.Children.Add(Stroke(2, 12, 14, 12, opacity: 0.5));
            canvas.Children.Add(Block(9.5, 2.5, 2, 3));
            canvas.Children.Add(Block(4, 6.5, 2, 3));
            canvas.Children.Add(Block(10.5, 10.5, 2, 3));
            break;

        case ToolbarCommand.Share:
            // A box with something leaving it upwards, which is the share glyph on both
            // systems: macshot's square.and.arrow.up is the same drawing.
            canvas.Children.Add(Stroke(3, 8, 3, 14));
            canvas.Children.Add(Stroke(3, 14, 13, 14));
            canvas.Children.Add(Stroke(13, 8, 13, 14));
            canvas.Children.Add(Stroke(8, 2, 8, 10));
            canvas.Children.Add(Stroke(8, 2, 5.5, 4.5));
            canvas.Children.Add(Stroke(8, 2, 10.5, 4.5));
            break;

        case ToolbarCommand.Translate:
            // Two scripts side by side — a Latin A and a mark built the way a CJK
            // character is — which is what the button turns one into. An arrow between
            // them would say the same thing and leave neither legible at this size.
            canvas.Children.Add(Stroke(2, 13, 4.5, 4));
            canvas.Children.Add(Stroke(4.5, 4, 7, 13));
            canvas.Children.Add(Stroke(3.1, 10, 5.9, 10));
            canvas.Children.Add(Stroke(9, 5.5, 14, 5.5));
            canvas.Children.Add(Stroke(11.5, 5.5, 11.5, 13));
            canvas.Children.Add(Stroke(9.5, 9, 13.5, 9));
            break;

        case ToolbarCommand.Redact:
            // A line of text with a block struck through it.
            canvas.Children.Add(Stroke(3, 5, 13, 5, opacity: 0.5));
            canvas.Children.Add(Stroke(3, 12, 9, 12, opacity: 0.5));
            canvas.Children.Add(Block(3, 7.5, 10, 3));
            break;

        case ToolbarCommand.InvertColors:
            // A circle with one half filled, which is what macshot draws: the shape says
            // "the same picture, the other way round" without naming a colour.
            canvas.Children.Add(Ring(1, 14, 1));
            canvas.Children.Add(new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = ToolbarPalette.IconBrush(),
                Margin = new Thickness(2, 1, 0, 0),

                // A clip rather than a drawn half-disc: the geometry a Path would need is
                // two arcs and a line, and the right half of a circle is exactly the
                // intersection of the circle with the rectangle beside it.
                Clip = new RectangleGeometry { Rect = new Rect(7, 0, 7, 14) },
            });
            break;

        case ToolbarCommand.Beautify:
            // A picture standing off its background, with the sparkle macshot's own
            // button uses: the frame is what the action adds, the sparkle is why.
            canvas.Children.Add(Frame(2, 5, 9, 9));
            canvas.Children.Add(Stroke(13, 2, 13, 6));
            canvas.Children.Add(Stroke(11, 4, 15, 4));
            break;

        case ToolbarCommand.ScrollCapture:
            // A page longer than the view, with an arrow going on down it.
            canvas.Children.Add(Frame(3, 2, 10, 7, opacity: 0.5));
            canvas.Children.Add(Stroke(8, 8, 8, 14));
            canvas.Children.Add(Stroke(8, 14, 5.5, 11.5));
            canvas.Children.Add(Stroke(8, 14, 10.5, 11.5));
            break;

        case ToolbarCommand.Record:
            // A filled circle, which is what every recorder anyone has used shows.
            canvas.Children.Add(new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = ToolbarPalette.IconBrush(),
                Margin = new Thickness(3.5, 3.5, 0, 0),
            });
            canvas.Children.Add(Ring(0.5, 15, 0.55));
            break;

        default:
            return new TextBlock
            {
                Text = "?",
                Foreground = ToolbarPalette.IconBrush(),
                FontSize = 12,
            };
        }

        return canvas;
    }

    private static FrameworkElement ForTool(AnnotationTool tool)
    {
        var canvas = NewCanvas();

        switch (tool)
        {
        case AnnotationTool.Line:
            canvas.Children.Add(Stroke(2, 14, 14, 2));
            break;

        case AnnotationTool.Arrow:
            canvas.Children.Add(Stroke(2, 14, 14, 2));
            canvas.Children.Add(Stroke(14, 2, 8.5, 2.5));
            canvas.Children.Add(Stroke(14, 2, 13.5, 7.5));
            break;

        case AnnotationTool.Pencil:
            // A zigzag rather than a straight line: what separates this from the line
            // tool is that the stroke follows the hand.
            canvas.Children.Add(Stroke(2, 12, 6, 4));
            canvas.Children.Add(Stroke(6, 4, 10, 12));
            canvas.Children.Add(Stroke(10, 12, 14, 5));
            break;

        case AnnotationTool.Marker:
            // Wide and translucent, which is the whole difference from the pencil.
            canvas.Children.Add(Stroke(2, 13, 14, 3, thickness: 5, opacity: 0.55));
            break;

        case AnnotationTool.Rectangle:
            canvas.Children.Add(Frame(2, 3.5, 12, 9));
            break;

        case AnnotationTool.FilledRectangle:
            canvas.Children.Add(Block(2, 3.5, 12, 9));
            break;

        case AnnotationTool.Ellipse:
            canvas.Children.Add(new Ellipse
            {
                Width = 13,
                Height = 10,
                Stroke = ToolbarPalette.IconBrush(),
                StrokeThickness = 1.6,
                Margin = new Thickness(1.5, 3, 0, 0),
            });
            break;

        case AnnotationTool.Pixelate:
            // Four blocks in a checker, which is what the effect looks like at the size
            // anyone actually notices it.
            canvas.Children.Add(Block(2, 3, 5, 4));
            canvas.Children.Add(Block(8, 3, 5, 4, opacity: 0.45));
            canvas.Children.Add(Block(2, 9, 5, 4, opacity: 0.45));
            canvas.Children.Add(Block(8, 9, 5, 4));
            break;

        case AnnotationTool.Blur:
            // Nested rings fading outwards: the same shape losing its edge, which is
            // what distinguishes it from the hard blocks above.
            canvas.Children.Add(Ring(1, 14, 0.35));
            canvas.Children.Add(Ring(4, 8, 0.7));
            canvas.Children.Add(Ring(6, 4, 1));
            break;

        case AnnotationTool.Highlight:
            // A bright centre with the surround dimmed: a spotlight, not a marker.
            canvas.Children.Add(Ring(1, 14, 0.35));
            canvas.Children.Add(new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = ToolbarPalette.IconBrush(0.9),
                Margin = new Thickness(4.5, 4.5, 0, 0),
            });
            break;

        case AnnotationTool.Measure:
            // A span with a bar across each end, which is exactly what the tool draws.
            canvas.Children.Add(Stroke(3, 8, 13, 8));
            canvas.Children.Add(Stroke(3, 4, 3, 12));
            canvas.Children.Add(Stroke(13, 4, 13, 12));
            break;

        case AnnotationTool.Loupe:
            // A circle with a handle: the one icon here that is a picture of the
            // instrument rather than of its mark, because the mark is the pixels
            // underneath at twice the size and there is no drawing that.
            canvas.Children.Add(Ring(1, 11, 1));
            canvas.Children.Add(Stroke(11, 11, 14.5, 14.5, thickness: 2));
            break;

        case AnnotationTool.Text:
            // A capital T, drawn rather than typed: a glyph would be the one icon whose
            // size and weight follow the toolbar's font instead of the row.
            canvas.Children.Add(Stroke(3, 3.5, 13, 3.5));
            canvas.Children.Add(Stroke(8, 3.5, 8, 13));
            break;

        case AnnotationTool.Number:
            canvas.Children.Add(Ring(1, 14, 1));
            canvas.Children.Add(Stroke(8, 4.5, 8, 11.5));
            canvas.Children.Add(Stroke(8, 4.5, 6, 6.5));
            break;

        case AnnotationTool.ColorSampler:
            // A dropper: the barrel on the diagonal with a tip at the bottom left.
            canvas.Children.Add(Stroke(6, 10, 12, 4, thickness: 2.4));
            canvas.Children.Add(Stroke(10, 2, 14, 6, thickness: 2));
            canvas.Children.Add(Stroke(6, 10, 3, 13));
            canvas.Children.Add(Stroke(3, 13, 4.5, 11.5, opacity: 0.6));
            break;

        case AnnotationTool.Stamp:
            // A face, because the mark is whichever emoji is chosen and no single
            // drawing stands for all of them.
            canvas.Children.Add(Ring(1, 14, 1));
            canvas.Children.Add(Block(5.5, 6, 1.6, 1.6));
            canvas.Children.Add(Block(9, 6, 1.6, 1.6));
            canvas.Children.Add(Stroke(5, 10, 8, 11.5));
            canvas.Children.Add(Stroke(8, 11.5, 11, 10));
            break;

        default:
            return new TextBlock
            {
                Text = ToolbarActions.Tooltip(tool)[..1],
                Foreground = ToolbarPalette.IconBrush(),
                FontSize = 12,
            };
        }

        return canvas;
    }

    private static Canvas NewCanvas() => new() { Width = Extent, Height = Extent };

    private static Line Stroke(
        double x1,
        double y1,
        double x2,
        double y2,
        double thickness = 1.6,
        double opacity = 1) =>
        new()
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = ToolbarPalette.IconBrush(opacity),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };

    private static Rectangle Frame(double x, double y, double width, double height, double opacity = 1) => new()
    {
        Width = width,
        Height = height,
        Stroke = ToolbarPalette.IconBrush(opacity),
        StrokeThickness = 1.6,
        Margin = new Thickness(x, y, 0, 0),
    };

    private static Rectangle Block(double x, double y, double width, double height, double opacity = 1) => new()
    {
        Width = width,
        Height = height,
        Fill = ToolbarPalette.IconBrush(opacity),
        Margin = new Thickness(x, y, 0, 0),
    };

    private static Ellipse Ring(double inset, double extent, double opacity) => new()
    {
        Width = extent,
        Height = extent,
        Stroke = ToolbarPalette.IconBrush(opacity),
        StrokeThickness = 1.4,
        Margin = new Thickness(inset + 1, inset, 0, 0),
    };
}
