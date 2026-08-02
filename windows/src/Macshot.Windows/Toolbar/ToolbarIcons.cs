using Macshot.Windows.Core.Annotations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

// Aliased rather than imported: the project's implicit usings bring in System.IO, and a
// bare Path there is a file path. Neither name is the one to give up, so the shape wins
// here and the file that wants a file path can say System.IO.Path.
using Path = Microsoft.UI.Xaml.Shapes.Path;

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

        // square.and.arrow.down.fill — a solid square with the arrow cut out of it, which
        // is what the .fill of that symbol is; the earlier open tray with an arrow above
        // it was the plain square.and.arrow.down. One path, because the cut is the
        // even-odd rule doing the work: the arrow inside the square counts twice and
        // drops out, the shaft above it counts once and stays.
        ToolbarCommand.Save => Icon(Solid(
            "M7 7h10a4 4 0 0 1 4 4v8a4 4 0 0 1 -4 4H7a4 4 0 0 1 -4 -4v-8a4 4 0 0 1 4 -4z"
            + "M11 1.2h2v13.3h2.5L12 18.5 8.5 14.5h2.5z")),

        // pin.fill
        ToolbarCommand.Pin => Icon(Solid(
            "M9 2h6a1 1 0 0 1 0 2h-.6l.6 5.6 2.9 2.4a2 2 0 0 1 .7 1.6V15a1 1 0 0 1 -1 1h-5v6"
            + "a.8 .8 0 0 1 -1.6 0v-6h-5a1 1 0 0 1 -1 -1v-1.4a2 2 0 0 1 .7 -1.6L9 9.6 9.6 4H9a1 1 0 0 1 0 -2z")),

        // doc.text.viewfinder — four corners and the lines they are closing in on.
        ToolbarCommand.ReadText => Icon(
            Outline("M3 8V5a2 2 0 0 1 2 -2h3M16 3h3a2 2 0 0 1 2 2v3M21 16v3a2 2 0 0 1 -2 2h-3M8 21H5a2 2 0 0 1 -2 -2v-3"),
            Outline("M8 9h8M8 12.5h8M8 16h5", Faint)),

        // slider.horizontal.3 — three tracks at three settings, which is what the popover
        // behind the button holds. The knobs are rings and each track stops short of the
        // one on it, the way the symbol draws them; solid dots on a track that ran
        // straight through read as beads rather than as something to take hold of. One
        // weight throughout, because the symbol has no faint half.
        ToolbarCommand.Adjust => Icon(
            Outline("M2 5h9.6M19.4 5h2.6M2 12h2.6M12.4 12h9.6M2 19h8.1M17.9 19h4.1"),
            Outline("M15.5 2.6a2.4 2.4 0 1 1 0 4.8 2.4 2.4 0 0 1 0 -4.8z"
                + "M8.5 9.6a2.4 2.4 0 1 1 0 4.8 2.4 2.4 0 0 1 0 -4.8z"
                + "M14 16.6a2.4 2.4 0 1 1 0 4.8 2.4 2.4 0 0 1 0 -4.8z")),

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
        // says the figure inside is being lifted away from what is behind it. The figure
        // is filled and nearly as wide as the ring, as the symbol draws it: outlined, and
        // small enough to leave a margin, it read as a second ring inside the first.
        ToolbarCommand.RemoveBackground => Icon(
            Outline("M12 2.5a9.5 9.5 0 1 1 0 19 9.5 9.5 0 0 1 0 -19z", 1, [2, 1.4]),
            Solid("M12 5.7a3.4 3.9 0 1 1 0 7.8 3.4 3.9 0 0 1 0 -7.8z"
                + "M2.91 17.5A15.7 15.7 0 0 1 21.09 17.5A10.625 10.625 0 0 1 2.91 17.5z")),

        // text.redaction — macshot draws no button for this at all (its auto-redact is a
        // right-click on the censor tool), so this is the port's own, taken from the
        // symbol whose whole subject is the thing: two lines of text and the bar that has
        // gone over the one in the middle.
        ToolbarCommand.Redact => Icon(
            Outline("M4 5h16M4 19h10", Faint),
            Solid("M4 9h16v6H4z")),

        // circle.righthalf.filled.inverse — the same picture the other way round, said
        // without naming a colour.
        ToolbarCommand.InvertColors => Icon(
            Outline("M12 2.5a9.5 9.5 0 1 1 0 19 9.5 9.5 0 0 1 0 -19z"),
            Solid("M12 3.6a8.4 8.4 0 0 1 0 16.8z")),

        // sparkles — three four-pointed stars, not two, and the arms curve in to a waist
        // rather than running straight to the tip. Both are what the symbol is: a
        // straight-sided star is a diamond, and it is the pinch halfway along each arm
        // that makes the shape read as a glint.
        ToolbarCommand.Beautify => Icon(Solid(
            "M11 9.1Q12.01 15.29 18.2 16.3Q12.01 17.31 11 23.5Q9.99 17.31 3.8 16.3Q9.99 15.29 11 9.1z"
            + "M6 2.8Q6.62 6.58 10.4 7.2Q6.62 7.82 6 11.6Q5.38 7.82 1.6 7.2Q5.38 6.58 6 2.8z"
            + "M15 1.4Q15.42 3.78 17.8 4.2Q15.42 4.62 15 7Q14.58 4.62 12.2 4.2Q14.58 3.78 15 1.4z")),

        // scroll — a page taller than the view with the capture going on down it. The
        // symbol itself is a rolled parchment, which at sixteen across is a lozenge; what
        // survives the size is the page and the direction, so that is what is drawn.
        ToolbarCommand.ScrollCapture => Icon(
            Outline("M6 3h12a2 2 0 0 1 2 2v6a2 2 0 0 1 -2 2H6a2 2 0 0 1 -2 -2V5a2 2 0 0 1 2 -2z", Faint),
            Outline("M12 10v11M7.5 16.5 12 21l4.5 -4.5")),

        // video.fill — grown to the grid. Drawn two thirds the height the symbol has, the
        // camcorder was the smallest thing in a strip of full-height icons.
        ToolbarCommand.Record => Icon(Solid(
            "M4.1 4h8.8a2.6 2.6 0 0 1 2.6 2.6v10.8a2.6 2.6 0 0 1 -2.6 2.6H4.1a2.6 2.6 0 0 1 -2.6 -2.6V6.6a2.6 2.6 0 0 1 2.6 -2.6z"
            + "M16 16V8l5 -3.1a1 1 0 0 1 1.5 .9v12.4a1 1 0 0 1 -1.5 .9z")),

        // record.circle — the same dot as Record, ringed, because this is the press that
        // actually starts it.
        ToolbarCommand.StartRecording => Icon(
            Outline("M12 2.5a9.5 9.5 0 1 1 0 19 9.5 9.5 0 0 1 0 -19z"),
            Solid("M12 7a5 5 0 1 1 0 10 5 5 0 0 1 0 -10z")),

        ToolbarCommand.CancelRecording => Icon(Outline(Xmark)),

        // cursorarrow.click.2 — the pointer, and the two arcs the click sends out from its
        // tip. The "2" in the name is the pair of arcs; they were three straight ticks,
        // which is a different symbol's idea and read as a sparkle rather than a ripple.
        ToolbarCommand.MouseHighlight => Icon(
            Outline("M6.62 18.46A8.5 8.5 0 1 1 19.97 10.76"),
            Outline("M8.86 15.27A4.6 4.6 0 1 1 16.08 11.1"),
            Solid("M11 11V21.9L13.7 19.5 15.4 23.2 17.1 22.5 15.4 18.8H18.5z")),

        // keyboard — two rows of keys over a space bar. One row read as a calculator;
        // what says keyboard at this size is the density, which is the symbol's own.
        ToolbarCommand.ShowKeystrokes => Icon(
            Outline("M4.5 5.5h15a3 3 0 0 1 3 3v7a3 3 0 0 1 -3 3h-15a3 3 0 0 1 -3 -3v-7a3 3 0 0 1 3 -3z"),
            Solid("M4.7 8.4h1.9v1.9H4.7zM7.6 8.4h1.9v1.9H7.6zM10.5 8.4h1.9v1.9h-1.9z"
                + "M13.4 8.4h1.9v1.9h-1.9zM16.3 8.4h1.9v1.9h-1.9zM19.2 8.4h1.9v1.9h-1.9z"
                + "M4.7 11.3h1.9v1.9H4.7zM7.6 11.3h1.9v1.9H7.6zM10.5 11.3h1.9v1.9h-1.9z"
                + "M13.4 11.3h1.9v1.9h-1.9zM16.3 11.3h1.9v1.9h-1.9zM19.2 11.3h1.9v1.9h-1.9z"
                + "M7.6 14.2h8.8v1.9H7.6z")),

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

        // gearshape — one outline that goes round the teeth, which is what a gear is. A
        // ring with eight spokes standing off it, which is what this was, is a sun or a
        // ship's wheel; the teeth have to be part of the same edge as the body for the
        // eye to read a cog. Eight of them, at the symbol's proportions: the tip of a
        // tooth spans twice the valley between two.
        ToolbarCommand.RecordingSettings => Icon(
            Outline("M10.1 2.0A10.2 10.2 0 0 1 13.9 2.0L14.2 4.9A7.4 7.4 0 0 1 15.4 5.4"
                + "L17.7 3.5A10.2 10.2 0 0 1 20.5 6.3L18.6 8.6A7.4 7.4 0 0 1 19.1 9.8"
                + "L22.0 10.1A10.2 10.2 0 0 1 22.0 13.9L19.1 14.2A7.4 7.4 0 0 1 18.6 15.4"
                + "L20.5 17.7A10.2 10.2 0 0 1 17.7 20.5L15.4 18.6A7.4 7.4 0 0 1 14.2 19.1"
                + "L13.9 22.0A10.2 10.2 0 0 1 10.1 22.0L9.8 19.1A7.4 7.4 0 0 1 8.6 18.6"
                + "L6.3 20.5A10.2 10.2 0 0 1 3.5 17.7L5.4 15.4A7.4 7.4 0 0 1 4.9 14.2"
                + "L2.0 13.9A10.2 10.2 0 0 1 2.0 10.1L4.9 9.8A7.4 7.4 0 0 1 5.4 8.6"
                + "L3.5 6.3A10.2 10.2 0 0 1 6.3 3.5L8.6 5.4A7.4 7.4 0 0 1 9.8 4.9z"),
            Outline("M12 8.7a3.3 3.3 0 1 1 0 6.6 3.3 3.3 0 0 1 0 -6.6z")),

        _ => Unnamed("?"),
    };

    private static FrameworkElement ForTool(AnnotationTool tool) => tool switch
    {
        // scribble — a stroke that followed a hand, which is the whole difference from
        // the line tool. It has to wander off its own axis to say so: an even wave with a
        // period, which is what this was, is a diagram of a wave and not a scribble.
        AnnotationTool.Pencil => Icon(Outline(
            "M2.5 18C4 9 8 2.5 11 4.5s-3.5 10.5 -1.5 13 6.5 -3 8 -6 4 1 3 5")),

        // line.diagonal
        AnnotationTool.Line => Icon(Outline("M4 20 20 4")),

        // arrow.up.right
        AnnotationTool.Arrow => Icon(Outline("M6 18 18 6M9 6h9v9")),

        // rectangle — five wide to four tall, which is the symbol's proportion. Drawn
        // flatter it stopped being the shape the tool draws and became a bar.
        AnnotationTool.Rectangle => Icon(Outline(
            "M5 4h14a3 3 0 0 1 3 3v10a3 3 0 0 1 -3 3H5a3 3 0 0 1 -3 -3V7a3 3 0 0 1 3 -3z")),

        // oval — taken out to the grid. At the old radii it sat a third smaller than the
        // rectangle beside it, which the two symbols are not.
        AnnotationTool.Ellipse => Icon(Outline(
            "M12 4c5.85 0 10.6 3.58 10.6 8s-4.75 8 -10.6 8 -10.6 -3.58 -10.6 -8 4.75 -8 10.6 -8z")),

        // highlighter — the pen on its angle, and under it the swipe it leaves. The swipe
        // is at full strength and runs the width of the icon, as the symbol draws it: a
        // pale short bar reads as a shadow under the pen rather than as ink.
        AnnotationTool.Marker => Icon(
            Outline("M10 14 6.5 10.5 15 2a2.5 2.5 0 0 1 3.5 3.5zM6.5 10.5 4 15l1.5 1.5L10 14"),
            Solid("M2.5 19.6h16v2.6h-16z")),

        // textformat — the symbol's own A and a. Drawn rather than typed: a glyph would
        // be the one icon whose size and weight follow the toolbar's font instead of the
        // row's. The bowl of the a is wider than the pen is: at the old radius the pen
        // met itself across the middle and closed the counter into a blot.
        AnnotationTool.Text => Icon(
            Outline("M2.5 19.5 8.5 5l6 14.5M4.8 15h7.4"),
            Outline("M18.8 12.9a3 3 0 1 0 0 6 3 3 0 0 0 0 -6M21.8 12.7V19.5")),

        // 1.circle.fill — a filled disc with the numeral cut out of it. No colour is
        // needed for the hole after all: both shapes go in one path, and the even-odd
        // rule leaves the numeral unpainted wherever it lies inside the disc, so what
        // shows through is the capture rather than a guess at the button's background.
        AnnotationTool.Number => Icon(Solid(
            "M12 1a11 11 0 1 1 0 22 11 11 0 0 1 0 -22z"
            + "M13.7 6.5V17.5H11.1V9.9L9.4 11 8.3 9.3 11.6 6.5z")),

        // A checkerboard, which is macshot's own icon for it and what the effect looks
        // like at the size anyone notices it. The mode is chosen on the options row, so
        // the button stands for all four. Four cells to a side and rounded at the corner,
        // which is what macshot draws (ToolbarButtonView.checkerboardIcon) — two to a
        // side is a quartered square, and the pattern is the whole point.
        AnnotationTool.Censor => Icon(
            Solid("M6 0h6v6H6zM18 0h1.5A4.5 4.5 0 0 1 24 4.5V6h-6z"
                + "M0 6h6v6H0zM12 6h6v6h-6zM6 12h6v6H6zM18 12h6v6h-6z"
                + "M0 18h6v6H4.5A4.5 4.5 0 0 1 0 19.5zM12 18h6v6h-6z"),
            Solid("M4.5 0H6v6H0V4.5A4.5 4.5 0 0 1 4.5 0zM12 0h6v6h-6z"
                + "M6 6h6v6H6zM18 6h6v6h-6zM0 12h6v6H0zM12 12h6v6h-6z"
                + "M6 18h6v6H6zM18 18h6v1.5A4.5 4.5 0 0 1 19.5 24H18z", 0.35)),

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
        // stands for all of them. Filled with the eyes and the mouth cut out of it, which
        // is how that symbol comes: there is no outlined face in the set, and an outlined
        // one here sat beside a row of solid ones looking like a different drawing.
        AnnotationTool.Stamp => Icon(Solid(
            "M12 1a11 11 0 1 1 0 22 11 11 0 0 1 0 -22z"
            + "M8.6 8.1a1.5 1.9 0 1 1 0 3.8 1.5 1.9 0 0 1 0 -3.8z"
            + "M15.4 8.1a1.5 1.9 0 1 1 0 3.8 1.5 1.9 0 0 1 0 -3.8z"
            + "M6.63 14.1A6.2 6.2 0 0 0 17.37 14.1L15.46 13A4 4 0 0 1 8.54 13z")),

        // eyedropper
        AnnotationTool.ColorSampler => Icon(
            Solid("M17 2.6a2.9 2.9 0 0 1 4.1 4.1l-1.9 1.9 -4.1 -4.1z"),
            Outline("M15 4.6 19.4 9M14.6 7 5 16.6V19.5h2.9L17.5 9.9z")),

        // ruler — lying flat, with the graduations hanging off its top edge. The symbol
        // is horizontal; drawn on the diagonal with three strokes across it, this was a
        // plaster rather than a rule. The ticks are filled slivers rather than strokes
        // because the pen is one width for the whole row, and eight marks at the row's
        // weight across fourteen points is a solid bar.
        AnnotationTool.Measure => Icon(
            Outline("M4 7.5h16a2.5 2.5 0 0 1 2.5 2.5v4a2.5 2.5 0 0 1 -2.5 2.5H4a2.5 2.5 0 0 1 -2.5 -2.5v-4A2.5 2.5 0 0 1 4 7.5z"),
            Solid("M4 8.4h0.9v4.4H4zM6.2 8.4h0.9v2.6h-0.9zM8.4 8.4h0.9v4.4h-0.9z"
                + "M10.6 8.4h0.9v2.6h-0.9zM12.8 8.4h0.9v4.4h-0.9zM15 8.4h0.9v2.6h-0.9z"
                + "M17.2 8.4h0.9v4.4h-0.9zM19.4 8.4h0.9v2.6h-0.9z")),

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
