using Macshot.Windows.Core.Annotations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

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
/// One outline per icon, drawn from path data on a 24-unit grid and scaled down to the
/// button's 16 — the grid every icon set is drawn on, and the size SF Symbols hands back
/// at <c>pointSize 14</c>. Each icon here answers to the SF Symbol macshot names for the
/// same button, named in the comment above it, down to whether that symbol is the outline
/// or the <c>.fill</c>: a filled pin beside an outlined tray is the kind of mismatch that
/// makes a row of icons look assembled rather than drawn.
/// </para>
/// <para>
/// Path data rather than <c>Line</c> and <c>Rectangle</c> laid out by margin. Curves that
/// are really curves, corners that are really rounded, and one stroke width across the
/// whole row — the three things a row of hand-placed primitives cannot hold on to, and
/// between them the difference between an icon and a diagram of one.
/// </para>
/// </remarks>
internal static class ToolbarIcons
{
    /// <summary>The grid the path data below is drawn on.</summary>
    private const double Grid = 24;

    /// <summary>The drawing area an icon is composed in, centred in a button.</summary>
    private const double Extent = ToolbarPalette.IconExtent;

    /// <summary>
    /// One weight for every stroke in the row, which is what SF Symbols' <c>.medium</c>
    /// comes back as at this size. Set on the pen rather than on the geometry, so scaling
    /// the 24-grid down to 16 moves the outline without thinning it.
    /// </summary>
    private const double Weight = 1.5;

    /// <summary>The weight of a line that is there to be read past rather than read.</summary>
    private const double Faint = 0.5;

    /// <summary>The icon for a button, or null when the button draws itself.</summary>
    public static FrameworkElement? For(ToolbarItem item) => item.Command switch
    {
        // The colour button is a swatch of the colour itself, which the button draws.
        ToolbarCommand.PickColor => null,

        ToolbarCommand.PickTool => item.Tool is { } tool ? ForTool(tool) : null,
        _ => ForCommand(item.Command),
    };

    private static FrameworkElement ForCommand(ToolbarCommand command) => command switch
    {
        // arrow.uturn.backward — the shaft runs left to the head, and the turn below it
        // is what separates undo from a plain back arrow.
        ToolbarCommand.Undo => Icon(
            Outline("M9 5 4 10l5 5M4 10h9a6 6 0 0 1 0 12h-2")),

        // arrow.uturn.forward
        ToolbarCommand.Redo => Icon(
            Outline("M15 5l5 5 -5 5M20 10h-9a6 6 0 0 0 0 12h2")),

        // xmark
        ToolbarCommand.Cancel => Icon(Outline(Xmark)),

        // arrow.up.and.down.and.arrow.left.and.right
        ToolbarCommand.MoveSelection => Icon(Outline(
            "M12 2v20M2 12h20M9 5l3 -3 3 3M9 19l3 3 3 -3M5 9l-3 3 3 3M19 9l3 3 -3 3")),

        // arrow.up.forward.app — the capture carries on outside the frame it is in, so
        // the frame is the faint half and the arrow leaving it is the loud one.
        ToolbarCommand.OpenEditor => Icon(
            Outline("M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2H5a2 2 0 0 1 -2 -2V5a2 2 0 0 1 2 -2z", Faint),
            Outline("M9 15 15 9M10 9h5v5")),

        // doc.on.doc — two sheets, the one behind faint so the front one keeps its edge.
        ToolbarCommand.Copy => Icon(
            Outline("M6 17H5a2 2 0 0 1 -2 -2V4a2 2 0 0 1 2 -2h8a2 2 0 0 1 2 2v1", Faint),
            Outline("M11 7h8a2 2 0 0 1 2 2v11a2 2 0 0 1 -2 2h-8a2 2 0 0 1 -2 -2V9a2 2 0 0 1 2 -2z")),

        // square.and.arrow.down.fill — the tray is the filled half of that symbol, the
        // arrow going into it the stroked one.
        ToolbarCommand.Save => Icon(
            Solid("M3 13h3v5h12v-5h3v6a3 3 0 0 1 -3 3H6a3 3 0 0 1 -3 -3z"),
            Outline("M12 2v12M7 9.5l5 5 5 -5")),

        // pin.fill
        ToolbarCommand.Pin => Icon(Solid(
            "M9 2h6a1 1 0 0 1 0 2h-.6l.6 5.6 2.9 2.4a2 2 0 0 1 .7 1.6V15a1 1 0 0 1 -1 1h-5v6"
            + "a.8 .8 0 0 1 -1.6 0v-6h-5a1 1 0 0 1 -1 -1v-1.4a2 2 0 0 1 .7 -1.6L9 9.6 9.6 4H9a1 1 0 0 1 0 -2z")),

        // doc.text.viewfinder — four corners and the lines they are closing in on.
        ToolbarCommand.ReadText => Icon(
            Outline("M3 8V5a2 2 0 0 1 2 -2h3M16 3h3a2 2 0 0 1 2 2v3M21 16v3a2 2 0 0 1 -2 2h-3M8 21H5a2 2 0 0 1 -2 -2v-3"),
            Outline("M8 9h8M8 12.5h8M8 16h5", Faint)),

        // slider.horizontal.3 — three tracks at three settings, which is what the popover
        // behind the button holds.
        ToolbarCommand.Adjust => Icon(
            Outline("M3 6h18M3 12h18M3 18h18", Faint),
            Solid("M15 6a2 2 0 1 1 -4 0 2 2 0 0 1 4 0zM9 12a2 2 0 1 1 -4 0 2 2 0 0 1 4 0zM19 18a2 2 0 1 1 -4 0 2 2 0 0 1 4 0z")),

        // square.and.arrow.up
        ToolbarCommand.Share => Icon(
            Outline("M8 11H6a2 2 0 0 0 -2 2v7a2 2 0 0 0 2 2h12a2 2 0 0 0 2 -2v-7a2 2 0 0 0 -2 -2h-2"),
            Outline("M12 2v14M7.5 6.5 12 2l4.5 4.5")),

        // icloud.and.arrow.up
        ToolbarCommand.Upload => Icon(
            Outline("M7.5 13h9.5a4 4 0 0 0 .2 -8A5.5 5.5 0 0 0 6.8 4.2 4.4 4.4 0 0 0 7.5 13z", Faint),
            Outline("M12 22v-9M8.5 16.5 12 13l3.5 3.5")),

        // translate — two scripts side by side, a Latin A and a mark built the way a CJK
        // character is, which is what the button turns one into.
        ToolbarCommand.Translate => Icon(
            Outline("M3 19.5 7 6.5l4 13M4.4 15.4h5.2"),
            Outline("M13 9h8M17 9v10.5M14.5 13.5h5")),

        // person.crop.circle.dashed — the dashes are the symbol's own, and they are what
        // says the face inside is on its way out.
        ToolbarCommand.Redact => Icon(
            Outline("M12 2.5a9.5 9.5 0 1 1 0 19 9.5 9.5 0 0 1 0 -19z", 1, [2.2, 1.8]),
            Outline("M12 12.6a3.3 3.3 0 1 0 0 -6.6 3.3 3.3 0 0 0 0 6.6zM6.2 19.6a6.6 6.6 0 0 1 11.6 0")),

        // circle.righthalf.filled.inverse — the same picture the other way round, said
        // without naming a colour.
        ToolbarCommand.InvertColors => Icon(
            Outline("M12 2.5a9.5 9.5 0 1 1 0 19 9.5 9.5 0 0 1 0 -19z"),
            Solid("M12 3.6a8.4 8.4 0 0 1 0 16.8z")),

        // sparkles — one four-pointed star with a smaller one off its shoulder.
        ToolbarCommand.Beautify => Icon(Solid(
            "M10 3l1.5 5L16.5 9.5 11.5 11 10 16 8.5 11 3.5 9.5 8.5 8z"
            + "M18 13.5l.8 2.7 2.7 .8 -2.7 .8 -.8 2.7 -.8 -2.7 -2.7 -.8 2.7 -.8z")),

        // scroll — a page taller than the view with the capture going on down it. The
        // symbol itself is a rolled parchment, which at sixteen across is a lozenge; what
        // survives the size is the page and the direction, so that is what is drawn.
        ToolbarCommand.ScrollCapture => Icon(
            Outline("M6 3h12a2 2 0 0 1 2 2v6a2 2 0 0 1 -2 2H6a2 2 0 0 1 -2 -2V5a2 2 0 0 1 2 -2z", Faint),
            Outline("M12 10v11M7.5 16.5 12 21l4.5 -4.5")),

        // video.fill
        ToolbarCommand.Record => Icon(Solid(
            "M4 6h9a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2H4a2 2 0 0 1 -2 -2V8a2 2 0 0 1 2 -2z"
            + "M16 13.4V10.6l4.6 -2.7a.9 .9 0 0 1 1.4 .8v6.2a.9 .9 0 0 1 -1.4 .8z")),

        // record.circle — the same dot as Record, ringed, because this is the press that
        // actually starts it.
        ToolbarCommand.StartRecording => Icon(
            Outline("M12 2.5a9.5 9.5 0 1 1 0 19 9.5 9.5 0 0 1 0 -19z"),
            Solid("M12 7a5 5 0 1 1 0 10 5 5 0 0 1 0 -10z")),

        ToolbarCommand.CancelRecording => Icon(Outline(Xmark)),

        // cursorarrow.click.2 — the pointer, and the marks the recording draws round
        // each click.
        ToolbarCommand.MouseHighlight => Icon(
            Outline("M9.5 8.5l10 4.5 -4.3 1.7 -1.7 4.3z"),
            Outline("M4.5 4.5 6.7 6.7M3 11.5h3M11.5 3v3", Faint)),

        // keyboard
        ToolbarCommand.ShowKeystrokes => Icon(
            Outline("M4 5h16a2 2 0 0 1 2 2v10a2 2 0 0 1 -2 2H4a2 2 0 0 1 -2 -2V7a2 2 0 0 1 2 -2z"),
            Solid("M6 9h2v2H6zM10 9h2v2h-2zM14 9h2v2h-2zM18 9h2v2h-2zM8 14h8v2H8z")),

        // speaker.wave.2.fill — both waves are drawn whatever the state. The button
        // lights when it is on, and the drawing does not have to say so twice.
        ToolbarCommand.SystemAudio => Icon(
            Solid("M11 4.8v14.4a.8 .8 0 0 1 -1.3 .6L5.6 16H3a1 1 0 0 1 -1 -1v-6a1 1 0 0 1 1 -1h2.6l4.1 -3.8a.8 .8 0 0 1 1.3 .6z"),
            Outline("M15 9.2a4 4 0 0 1 0 5.6", 0.8),
            Outline("M18.2 6a8 8 0 0 1 0 12", Faint)),

        // mic.fill — the capsule is the filled half of that symbol, the cradle under it
        // the stroked one.
        ToolbarCommand.MicAudio => Icon(
            Solid("M12 2a3.2 3.2 0 0 1 3.2 3.2v6.4a3.2 3.2 0 0 1 -6.4 0V5.2A3.2 3.2 0 0 1 12 2z"),
            Outline("M18.5 11v.8a6.5 6.5 0 0 1 -13 0V11M12 18.3V22M8.5 22h7")),

        // web.camera — the lens, and the bar it stands on.
        ToolbarCommand.Webcam => Icon(
            Outline("M12 3a7.5 7.5 0 1 1 0 15 7.5 7.5 0 0 1 0 -15z"),
            Solid("M12 7a3.5 3.5 0 1 1 0 7 3.5 3.5 0 0 1 0 -7z"),
            Outline("M12 18v2M6 21h12", Faint)),

        // gearshape
        ToolbarCommand.RecordingSettings => Icon(
            Outline("M12 5a7 7 0 1 1 0 14 7 7 0 0 1 0 -14z"),
            Outline("M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9 7.1 7.1M16.9 16.9l2.2 2.2M19.1 4.9 16.9 7.1M7.1 16.9l-2.2 2.2"),
            Outline("M12 9a3 3 0 1 1 0 6 3 3 0 0 1 0 -6z", Faint)),

        _ => Unnamed("?"),
    };

    private static FrameworkElement ForTool(AnnotationTool tool) => tool switch
    {
        // scribble — a stroke that followed a hand, which is the whole difference from
        // the line tool.
        AnnotationTool.Pencil => Icon(Outline(
            "M2 12c1.6 -6 4.8 -6 6.4 0s4.8 6 6.4 0 4.8 -6 6.4 0")),

        // line.diagonal
        AnnotationTool.Line => Icon(Outline("M4 20 20 4")),

        // arrow.up.right
        AnnotationTool.Arrow => Icon(Outline("M6 18 18 6M9 6h9v9")),

        // rectangle
        AnnotationTool.Rectangle => Icon(Outline(
            "M4 6h16a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2H4a2 2 0 0 1 -2 -2V8a2 2 0 0 1 2 -2z")),

        // oval
        AnnotationTool.Ellipse => Icon(Outline(
            "M12 5c5.5 0 10 3.1 10 7s-4.5 7 -10 7 -10 -3.1 -10 -7 4.5 -7 10 -7z")),

        // highlighter — the pen on its angle, and under it the swipe it leaves.
        AnnotationTool.Marker => Icon(
            Outline("M10 14 6.5 10.5 15 2a2.5 2.5 0 0 1 3.5 3.5zM6.5 10.5 4 15l1.5 1.5L10 14"),
            Solid("M3 19.5h14v2.5H3z", 0.55)),

        // textformat — the symbol's own A and a. Drawn rather than typed: a glyph would
        // be the one icon whose size and weight follow the toolbar's font instead of the
        // row's.
        AnnotationTool.Text => Icon(
            Outline("M3.5 19 9 5.5 14.5 19M5.6 15.2h6.8"),
            Outline("M18.6 13.4a2.3 2.3 0 1 0 0 4.6 2.3 2.3 0 0 0 0 -4.6M20.9 13.2V18")),

        // 1.circle.fill, less the fill: knocking the numeral out of a filled disc needs
        // the disc painted in whatever is behind the button, and behind this one is the
        // capture. Ringed instead, which is the same symbol without the hole.
        AnnotationTool.Number => Icon(
            Outline("M12 2.5a9.5 9.5 0 1 1 0 19 9.5 9.5 0 0 1 0 -19z"),
            Outline("M10.2 9.4 12.6 7.6V16.4M10.4 16.4h4.4")),

        // A checkerboard, which is macshot's own icon for it and what the effect looks
        // like at the size anyone notices it. The mode is chosen on the options row, so
        // the button stands for all four.
        AnnotationTool.Censor => Icon(
            Solid("M3 3h9v9H3zM12 12h9v9h-9z"),
            Solid("M12 3h9v9h-9zM3 12h9v9H3z", 0.45)),

        // sun.max
        AnnotationTool.Highlight => Icon(
            Outline("M12 8a4 4 0 1 1 0 8 4 4 0 0 1 0 -8z"),
            Outline("M12 2v2.5M12 19.5V22M2 12h2.5M19.5 12H22M4.9 4.9 6.7 6.7M17.3 17.3l1.8 1.8M19.1 4.9 17.3 6.7M6.7 17.3l-1.8 1.8")),

        // magnifyingglass — the one icon here that is a picture of the instrument rather
        // than of its mark, because the mark is the pixels underneath at twice the size
        // and there is no drawing that.
        AnnotationTool.Loupe => Icon(Outline(
            "M10.5 3a7.5 7.5 0 1 1 0 15 7.5 7.5 0 0 1 0 -15zM16 16l5.5 5.5")),

        // face.smiling — the mark is whichever emoji is chosen, and no single drawing
        // stands for all of them.
        AnnotationTool.Stamp => Icon(
            Outline("M12 2.5a9.5 9.5 0 1 1 0 19 9.5 9.5 0 0 1 0 -19zM7.8 14.2a5.2 5.2 0 0 0 8.4 0"),
            Solid("M8.4 9a1.3 1.3 0 1 1 0 2.6 1.3 1.3 0 0 1 0 -2.6zM15.6 9a1.3 1.3 0 1 1 0 2.6 1.3 1.3 0 0 1 0 -2.6z")),

        // eyedropper
        AnnotationTool.ColorSampler => Icon(
            Solid("M17 2.6a2.9 2.9 0 0 1 4.1 4.1l-1.9 1.9 -4.1 -4.1z"),
            Outline("M15 4.6 19.4 9M14.6 7 5 16.6V19.5h2.9L17.5 9.9z")),

        // ruler
        AnnotationTool.Measure => Icon(
            Outline("M4 14.5 14.5 4a2 2 0 0 1 2.8 0l2.7 2.7a2 2 0 0 1 0 2.8L9.5 20a2 2 0 0 1 -2.8 0L4 17.3a2 2 0 0 1 0 -2.8z"),
            Outline("M7.6 13.9l1.5 1.5M10.6 10.9l1.5 1.5M13.6 7.9l1.5 1.5", Faint)),

        _ => Unnamed(ToolbarActions.Tooltip(tool)[..1]),
    };

    /// <summary>The two commands that are the same cross, written once.</summary>
    private const string Xmark = "M6 6 18 18M18 6 6 18";

    /// <summary>
    /// Puts an icon's layers on the 16-unit square a button centres.
    /// </summary>
    /// <remarks>
    /// A <see cref="Canvas"/> rather than a <see cref="Grid"/>: a path in a Grid is
    /// measured to its own bounding box and then aligned to the cell, which slides each
    /// layer of a two-layer icon to a different place and pulls the icon apart. A Canvas
    /// leaves every layer on the coordinates it was drawn on.
    /// </remarks>
    private static Canvas Icon(params Path[] layers)
    {
        var canvas = new Canvas { Width = Extent, Height = Extent, IsHitTestVisible = false };

        foreach (var layer in layers)
        {
            canvas.Children.Add(layer);
        }

        return canvas;
    }

    /// <summary>A stroked outline, optionally broken into dashes.</summary>
    private static Path Outline(string data, double opacity = 1, double[]? dashes = null)
    {
        var path = Scaled(data);

        path.Stroke = ToolbarPalette.IconBrush(opacity);
        path.StrokeThickness = Weight;
        path.StrokeStartLineCap = PenLineCap.Round;
        path.StrokeEndLineCap = PenLineCap.Round;
        path.StrokeLineJoin = PenLineJoin.Round;

        if (dashes is not null)
        {
            // Flat caps for a dashed outline: a round cap adds half the stroke width to
            // each end of every dash, which at this size closes the gaps back up.
            path.StrokeStartLineCap = PenLineCap.Flat;
            path.StrokeEndLineCap = PenLineCap.Flat;

            // Assigned rather than added to: the collection a Shape starts with is the
            // property's default value, and adding to a default is the kind of thing that
            // is shared between every Shape in the process.
            var pattern = new DoubleCollection();

            foreach (var dash in dashes)
            {
                pattern.Add(dash);
            }

            path.StrokeDashArray = pattern;
        }

        return path;
    }

    /// <summary>A filled shape, for the symbols macshot names the <c>.fill</c> of.</summary>
    private static Path Solid(string data, double opacity = 1)
    {
        var path = Scaled(data);

        path.Fill = ToolbarPalette.IconBrush(opacity);

        return path;
    }

    /// <summary>
    /// Parses path data drawn on the 24-unit grid and brings it down to the button's 16.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scale goes on the geometry rather than on the <see cref="Path"/>: a
    /// <c>RenderTransform</c> would take the pen down with it and leave the row two
    /// thirds of the weight it is supposed to be.
    /// </para>
    /// <para>
    /// Loaded through <see cref="XamlReader"/> rather than converted from the string
    /// directly. WinUI's path mini-language is implemented by the XAML parser and is
    /// reachable no other way that is guaranteed to be there; the alternative is
    /// assembling every curve as <c>PathFigure</c> objects, which is the same drawing at
    /// ten times the length.
    /// </para>
    /// </remarks>
    private static Path Scaled(string data)
    {
        var path = (Path)XamlReader.Load(
            $"""<Path xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Data="{data}" />""");

        path.Data.Transform = new ScaleTransform { ScaleX = Extent / Grid, ScaleY = Extent / Grid };

        return path;
    }

    /// <summary>
    /// What a button with no drawing shows. Only reachable for a command that was added
    /// without an icon, so it is a reminder rather than a design.
    /// </summary>
    private static FrameworkElement Unnamed(string text) => new TextBlock
    {
        Text = text,
        Foreground = ToolbarPalette.IconBrush(),
        FontSize = 12,
    };
}
