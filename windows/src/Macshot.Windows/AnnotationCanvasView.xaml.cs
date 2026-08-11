using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Core.Recognition;
using Macshot.Windows.Rendering;
using Macshot.Windows.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Point resolves to
// Macshot.Point and does not compile.
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace Macshot.Windows;

/// <summary>
/// The surface annotations are drawn on: the live preview, the sprite tools, and the
/// text tool's entry box.
/// </summary>
/// <remarks>
/// <para>
/// A control rather than markup inside one window for the same reason the toolbar is
/// one: the capture overlay and the editor window need identical behaviour here, and
/// the sprite half of it is subtle enough that a second copy would be a second set of
/// bugs — placements are queued because rasterizing is asynchronous, and committing a
/// label has to survive the focus change that removing its box raises.
/// </para>
/// <para>
/// It knows nothing about where it is. Everything that differs between a display-sized
/// overlay and an image in a scroll viewer is behind <see cref="IFramePlacement"/>, and
/// the host owns the pointer: it converts its own input to frame-space points and calls
/// in. That is the same division as the macOS <c>AnnotationCanvas</c> protocol.
/// </para>
/// </remarks>
public sealed partial class AnnotationCanvasView : UserControl
{
    /// <summary>
    /// How big a grab handle is drawn, in layout units. Smaller than
    /// <see cref="AnnotationHandles.GrabRadius"/> is generous with, because a handle drawn
    /// as large as its catchment would cover the mark it belongs to.
    /// </summary>
    private const double HandleSize = 9;

    /// <summary>
    /// How tall the delete button a group of marks is given is drawn, and — halved — how
    /// round its ends are. macshot's own 28 (<c>OverlayView.swift:4889</c>).
    /// </summary>
    private const double PillHeight = 28;

    /// <summary>How far the button clears the marks it belongs to. macshot's 8.</summary>
    private const double PillGap = 8;

    private readonly Brush _chromeStroke = new SolidColorBrush(Color.FromArgb(255, 76, 194, 255));
    private readonly Brush _handleFill = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));

    // The chrome colour at macshot's 0.6, so an alignment guide reads as the editor
    // talking rather than as something drawn on the screenshot.
    private readonly Brush _guideStroke = new SolidColorBrush(Color.FromArgb(153, 76, 194, 255));

    // systemBlue at the tenth and the six tenths macshot fills and strokes the marquee
    // with. Deliberately not the chrome blue above: the two are on screen together while
    // a sweep is live, and one says what is selected while the other says what letting go
    // would select.
    private readonly Brush _lassoFill = new SolidColorBrush(Color.FromArgb(26, 0, 122, 255));
    private readonly Brush _lassoStroke = new SolidColorBrush(Color.FromArgb(153, 0, 122, 255));

    // macshot's pill: near-black at 0.94, a hairline of white at 0.08, and a bin in the
    // red it tints the trash symbol with. Fixed rather than system-adaptive, because this
    // is chrome over a screenshot and has to read the same whatever the desktop is set to.
    private readonly Brush _pillFill = new SolidColorBrush(Color.FromArgb(240, 31, 31, 31));
    private readonly Brush _pillBorder = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
    private readonly Brush _pillIcon = new SolidColorBrush(Color.FromArgb(255, 255, 102, 102));
    private readonly Brush _pillText = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));

    private AnnotationEditor? _editor;
    private IFramePlacement _placement = new ImageFramePlacement();
    private Action<string> _reportHint = _ => { };
    private Func<double>? _rasterizationScale;

    private RasterAnnotationPreview? _preview;
    private CapturedFrame? _source;
    private CaptureRegion _region;

    private TextBox? _textEntry;
    private CapturePoint _textEntryOrigin;

    /// <summary>
    /// The sprite placement still rasterizing, if any. Producing a sprite is async, so
    /// finishing has to wait for it: without this, clicking Done right after typing
    /// would deliver an image missing the text that click committed.
    /// </summary>
    private Task _pendingSprite = Task.CompletedTask;

    public AnnotationCanvasView()
    {
        InitializeComponent();
        // Every string in the XAML is already the English text macshot keys by,
        // so the page is translated in place rather than written twice.
        this.Localize();
    }

    /// <summary>The stamp tool's emoji, read from the toolbar when one is placed.</summary>
    public Func<string> StampEmoji { get; set; } = () => StampGlyph.Default;

    /// <summary>
    /// The picture the stamp tool places instead of its emoji, or null while none has been
    /// loaded. Asked for the same way and for the same reason as the emoji beside it.
    /// </summary>
    public Func<CapturedFrame?> StampPicture { get; set; } = () => null;

    /// <summary>
    /// What the first badge of this capture counts from. Asked for rather than held,
    /// because the toolbar owns the control and the answer can change between two clicks.
    /// </summary>
    public Func<int> NumberStartAt { get; set; } = () => 1;

    /// <summary>Whether a highlighter stroke should land on the text it was drawn across.</summary>
    public Func<bool> SmartMarker { get; set; } = () => false;

    /// <summary>Whether a censor drag should cover only the text found inside its region.</summary>
    public Func<bool> CensorTextOnly { get; set; } = () => false;

    /// <summary>True while the text tool's entry box is open and owns the keyboard.</summary>
    public bool IsTyping => _textEntry is not null;

    public void Bind(AnnotationEditor editor, Func<double> rasterizationScale, Action<string> reportHint)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(rasterizationScale);
        ArgumentNullException.ThrowIfNull(reportHint);

        _editor = editor;
        _rasterizationScale = rasterizationScale;
        _reportHint = reportHint;
    }

    /// <summary>
    /// Shows <paramref name="pixels"/> as the region being annotated, replacing whatever
    /// was there. Called again when the region changes, which is what makes an adjusted
    /// selection preview the pixels it actually covers.
    /// </summary>
    public void Present(CapturedFrame pixels, CaptureRegion region, IFramePlacement placement)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(placement);

        _source = pixels;
        _region = region;
        _placement = placement;

        // The grab handles are the one part of the editor measured in what a hand can
        // aim at rather than in capture pixels, and only the placement knows the two
        // apart. Set here rather than in Bind, because an overlay binds once and is then
        // presented onto whichever display's region was chosen.
        if (_editor is { } editor)
        {
            editor.Scale = placement.Scale;

            // Alongside the scale for the same reason: the region a mark can line up with
            // is the one being annotated, and which region that is only becomes known
            // here.
            editor.SnapRegion = region;
        }

        _preview?.Detach();
        _preview = new RasterAnnotationPreview(AnnotationLayer, placement, pixels, region);
        Render();
    }

    /// <summary>The pixels on show, ready to be delivered.</summary>
    public CapturedFrame? ToFrame() => _preview?.ToFrame();

    /// <summary>
    /// The same capture in the pieces it was made from, for archiving it in a form that
    /// can be edited again. Null before anything is being previewed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The unadjusted pixels come from the caller rather than from the preview: the
    /// preview holds what is on screen, and what is on screen is the adjustment already
    /// applied. Archiving that would leave <paramref name="state"/> describing something
    /// the pixels have already had done to them, and reopening would do it twice.
    /// </para>
    /// <para>
    /// The whole document rather than <c>VisibleAnnotations</c>: what is hidden is
    /// hidden for the length of a drag, and a mark left out here would be lost from the
    /// archive rather than merely not drawn.
    /// </para>
    /// </remarks>
    public EditableCapture? ToEditable(CapturedFrame raw, CaptureEditState state) =>
        _preview is { } preview
            ? new EditableCapture(raw, preview.Archivable(_editor?.Document.Annotations ?? []), state)
            : null;

    public void Render()
    {
        _preview?.Render(_editor?.VisibleAnnotations ?? []);
        DrawSelectionChrome();
        DrawSnapGuides();

        // A ruler is worth nothing until it says a number, and the number it will say
        // once it is let go is worth having during the drag. It goes in the hint line
        // rather than beside the pointer: a figure that jitters along with the mouse is
        // harder to read than one that sits still.
        if (_editor?.Draft is { Tool: AnnotationTool.Measure } ruler)
        {
            _reportHint(MeasureReading.Format(ruler.Span, ruler.Style.MeasureInPoints, SpriteScale));
        }
    }

    /// <summary>
    /// Finishes whatever the gesture that just ended could not finish itself.
    /// </summary>
    /// <param name="committed">
    /// The mark the editor committed, or null when the gesture drew nothing new.
    /// </param>
    /// <remarks>
    /// Called by the host once a gesture ends, because none of this can be done during
    /// one: a reading is not known until the drag stops, and the two options that read the
    /// pixels are asynchronous. Each amends the mark the drag produced rather than adding
    /// beside it, so the whole gesture stays one undo step.
    /// </remarks>
    public void FinishedGesture(Annotation? committed)
    {
        LabelRulers();

        if (committed is null)
        {
            return;
        }

        // Both of these read the screen, which is why they are here rather than in the
        // editor: Core has no OCR engine and no business acquiring one.
        if (committed.Tool == AnnotationTool.Marker && SmartMarker())
        {
            QueueSprite(() => SnapMarkerAsync(committed));
        }
        else if (committed.Tool == AnnotationTool.Censor && CensorTextOnly())
        {
            QueueSprite(() => CensorTextAsync(committed));
        }
    }

    /// <summary>
    /// Lays a highlighter stroke over the line of text it was drawn across.
    /// </summary>
    /// <remarks>
    /// The stroke is already on the canvas by the time this runs, and is amended in place
    /// once the text is known. Waiting for the OCR pass before showing anything would make
    /// the highlighter feel as slow as the engine is; showing the hand-drawn stroke and
    /// then straightening it is what macshot does, and it reads as the tool tidying up.
    /// </remarks>
    private async Task SnapMarkerAsync(Annotation stroke)
    {
        var snapped = TextSnapping.SnapToText(stroke, await RecognizeAsync());
        if (!ReferenceEquals(snapped, stroke))
        {
            _editor?.Document.Amend(snapped);
            Render();
        }
    }

    /// <summary>
    /// Replaces a censor region with one box per line of text inside it.
    /// </summary>
    /// <remarks>
    /// The region is taken back off, so what is left is only the boxes. Kept as well, it
    /// would cover the whole area anyway and the option would appear to do nothing —
    /// and the two removals are one undo step because the whole thing was one drag.
    /// </remarks>
    private async Task CensorTextAsync(Annotation region)
    {
        if (_editor is not { } editor)
        {
            return;
        }

        var lines = await RecognizeAsync();
        var boxes = AutoRedactor.RedactAllText(lines, region.BoundingRect, region.Style);

        // Nothing found means nothing to cover, and the region the user dragged is left
        // exactly as it was: covering nothing at all would lose a redaction they made.
        if (boxes.Count == 0)
        {
            // macshot's own wording for the same answer, so it is already translated
            // everywhere rather than being one more English string in this port's file.
            _reportHint(Localization.L("(No text detected in the selected area)"));
            return;
        }

        editor.Document.Amend(region.Id, boxes);
        Render();
    }

    /// <summary>
    /// Gives every ruler that has been drawn but not yet labelled its reading.
    /// </summary>
    /// <remarks>
    /// The label is amended onto the annotation rather than replacing it, so the ruler and
    /// its reading are one undo step — the drag the user made.
    /// </remarks>
    private void LabelRulers()
    {
        if (_editor is not { } editor)
        {
            return;
        }

        foreach (var ruler in editor.Document.Annotations
            .Where(annotation => annotation.Tool == AnnotationTool.Measure && annotation.Sprite is null)
            .ToArray())
        {
            QueueSprite(() => LabelRulerAsync(ruler));
        }
    }

    private async Task LabelRulerAsync(Annotation ruler)
    {
        var reading = MeasureReading.Build(ruler.Style, ruler.Span, SpriteScale);
        var sprite = await GlyphSpriteFactory.RenderAsync(SpriteHost, reading);

        // Amend, not add: undone or reshaped while the sprite was rendering, the ruler
        // this belongs to may be gone, and a reading for a ruler that is not there would
        // be a number floating on the screenshot.
        _editor?.Document.Amend(ruler with { Sprite = sprite });
        Render();
    }

    /// <summary>
    /// Redraws the outlines, handles and buttons around the selected marks, and the
    /// marquee if one is being swept.
    /// </summary>
    /// <remarks>
    /// Rebuilt from scratch on every render rather than kept and moved. A selection is a
    /// few small shapes and at most five handles, and the alternative is a cache that has
    /// to know when a rotation, a reshape or an undo invalidated it — which is every
    /// render this is called from anyway.
    /// </remarks>
    private void DrawSelectionChrome()
    {
        SelectionLayer.Children.Clear();
        SelectionControls.Children.Clear();

        if (_editor is not { } editor)
        {
            return;
        }

        foreach (var selected in editor.SelectedAsShown)
        {
            AddOutline(selected);
        }

        // Handles only where there is a single subject. A group is moved and deleted
        // whole, so a set of corners on each member would offer to reshape one of them
        // alone — which is why macOS draws the full controls only at a count of one
        // (OverlayView.swift:1853-1856).
        if (editor.SelectionShown is { } shown)
        {
            // The dashed chain through the anchors, which macshot draws for the reason it
            // draws the arms out to a bend grip (OverlayView.swift:4314-4322): the mark
            // itself is a smooth curve that passes near the anchors rather than turning the
            // corners they describe, so without the chain there is nothing saying which
            // grip belongs to which part of it. Before the handles, so they are drawn over
            // it.
            if (shown.HasWaypoints)
            {
                var chain = shown.AnchorPath;
                for (var span = 1; span < chain.Count; span++)
                {
                    AddArm(_placement.ToLayout(chain[span - 1]), _placement.ToLayout(chain[span]));
                }
            }

            var outline = AnnotationHandles.Outline(shown).Select(_placement.ToLayout).ToArray();
            foreach (var handle in editor.Handles)
            {
                AddHandle(handle, outline, shown);
            }
        }

        if (editor.MultiSelectionBounds is { } group)
        {
            AddGroupDelete(group, editor.SelectedAnnotations.Count);
        }

        if (editor.Lasso is { } lasso)
        {
            AddLasso(lasso);
        }
    }

    /// <summary>The dashed rectangle drawn round one selected mark.</summary>
    private void AddOutline(Annotation selected)
    {
        var border = new Polygon
        {
            Stroke = _chromeStroke,
            StrokeThickness = 1,

            // Dashed, so an outline drawn around a rectangle the user drew cannot be
            // mistaken for a second rectangle they did not.
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsHitTestVisible = false,
        };

        // Added to the collection the shape already owns rather than assigning a new one,
        // because the XAML collection types are not all constructible from code.
        foreach (var corner in AnnotationHandles.Outline(selected))
        {
            border.Points.Add(_placement.ToLayout(corner));
        }

        SelectionLayer.Children.Add(border);
    }

    /// <summary>
    /// The marquee a Ctrl+drag is sweeping over the canvas.
    /// </summary>
    /// <remarks>
    /// macshot's own colours and dash: systemBlue at a tenth inside, at six tenths on a
    /// 4/3 dashed border (<c>OverlayView.swift:1869-1879</c>). Not the chrome blue the
    /// selection outlines use, because the two are on screen together while the sweep is
    /// live and they are saying different things — one is what is selected, the other is
    /// what letting go would select.
    /// </remarks>
    private void AddLasso(CaptureRegion lasso)
    {
        var origin = _placement.ToLayout(new CapturePoint(lasso.X, lasso.Y));
        var opposite = _placement.ToLayout(new CapturePoint(lasso.Right, lasso.Bottom));

        var marquee = new Rectangle
        {
            Width = Math.Max(0, opposite.X - origin.X),
            Height = Math.Max(0, opposite.Y - origin.Y),
            Fill = _lassoFill,
            Stroke = _lassoStroke,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(marquee, origin.X);
        Canvas.SetTop(marquee, origin.Y);
        SelectionLayer.Children.Add(marquee);
    }

    /// <summary>
    /// The one delete button a group of selected marks gets, hung under everything it
    /// covers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A real control rather than a drawn pill with a rectangle hit test, which is what
    /// macshot has to do on a canvas that owns every pixel of its window
    /// (<c>OverlayView.swift:4863</c>). The picture is the same one: a dark rounded pill
    /// carrying a red bin and the count, under the middle of the group's bounds.
    /// </para>
    /// <para>
    /// Built after the page-wide localization pass has run, so it asks for its own
    /// translation. The key is macshot's own "Delete", which its forty languages already
    /// carry.
    /// </para>
    /// </remarks>
    private void AddGroupDelete(CaptureRegion bounds, int count)
    {
        var label = $"{Localization.L("Delete")} {count}";
        var caption = new TextBlock
        {
            Text = label,
            FontSize = 13,

            // On the control, because no theme resource carries a weight. macshot draws
            // this label at medium.
            FontWeight = AppFonts.Heavier(label, FontWeights.Medium),
            Foreground = _pillText,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
        };

        row.Children.Add(new FontIcon
        {
            // Segoe Fluent Icons' bin, the same character the history window's
            // own delete already uses.
            Glyph = "\uE74D",
            FontSize = 13,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Foreground = _pillIcon,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(caption);

        var pill = new Border
        {
            Height = PillHeight,
            CornerRadius = new CornerRadius(PillHeight / 2),
            Padding = new Thickness(12, 0, 12, 0),
            Background = _pillFill,
            BorderBrush = _pillBorder,
            BorderThickness = new Thickness(1),
            Child = row,
        };

        pill.PointerPressed += GroupDelete_PointerPressed;

        var centre = _placement.ToLayout(new CapturePoint(bounds.X + (bounds.Width / 2), bounds.Bottom));

        // Below the marks rather than above them, which is where macshot hangs it: the
        // toolbar is above the capture, and a button in the same place would be behind it
        // whenever the selection reached the top of the region.
        Canvas.SetTop(pill, centre.Y + PillGap);
        SelectionControls.Children.Add(pill);

        // How wide the pill is depends on the count and on the language it is written in,
        // so where its left edge goes is not known until it has been measured. Measured
        // after it is in the tree, and corrected on the layout pass that follows: a
        // measurement taken off the tree has no font context to take it in.
        pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(pill, centre.X - (pill.DesiredSize.Width / 2));
        pill.SizeChanged += (_, args) => Canvas.SetLeft(pill, centre.X - (args.NewSize.Width / 2));
    }

    private void GroupDelete_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Handled, or the press would carry on to the host underneath and start a mark
        // where the button used to be.
        e.Handled = true;

        if (_editor?.DeleteSelected() == true)
        {
            Render();
        }
    }

    /// <summary>
    /// Draws the lines saying what the mark in flight lined up with.
    /// </summary>
    /// <remarks>
    /// Into the selection layer, which <see cref="DrawSelectionChrome"/> has just cleared:
    /// a guide belongs to a gesture, and the layer that is emptied every render is the one
    /// that cannot leave one behind.
    /// </remarks>
    private void DrawSnapGuides()
    {
        if (_editor?.Snap is not { } snap)
        {
            return;
        }

        if (snap.GuideX is { } x)
        {
            AddGuide(new CapturePoint(x, _region.Y), new CapturePoint(x, _region.Bottom));
        }

        if (snap.GuideY is { } y)
        {
            AddGuide(new CapturePoint(_region.X, y), new CapturePoint(_region.Right, y));
        }
    }

    /// <summary>One leg of the dashed arms drawn from a line's ends to its control point.</summary>
    private void AddArm(Point from, Point to)
    {
        SelectionLayer.Children.Add(new Line
        {
            X1 = from.X,
            Y1 = from.Y,
            X2 = to.X,
            Y2 = to.Y,
            Stroke = _chromeStroke,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsHitTestVisible = false,
        });
    }

    private void AddGuide(CapturePoint from, CapturePoint to)
    {
        var start = _placement.ToLayout(from);
        var end = _placement.ToLayout(to);

        SelectionLayer.Children.Add(new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = _guideStroke,

            // A whole unit rather than macshot's half: a half-unit line on a display at
            // 100% falls between pixels and comes out as a grey smear, and a guide that
            // faint is worse than none.
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsHitTestVisible = false,
        });
    }

    private void AddHandle(AnnotationHandle handle, IReadOnlyList<Point> outline, Annotation shown)
    {
        var at = _placement.ToLayout(handle.Position);

        if (handle.Kind == AnnotationHandleKind.Bend)
        {
            // The arms macshot draws out to the control point
            // (OverlayView.swift:4324-4332). The grip sits beside the curve rather than on
            // it, so without them it is a dot floating near an arrow with nothing saying
            // it belongs to it — and the further it is dragged the less obviously it does.
            AddArm(_placement.ToLayout(shown.Start), at);
            AddArm(at, _placement.ToLayout(shown.End));
        }

        if (handle.Kind == AnnotationHandleKind.Rotate)
        {
            // Tethered to the edge it swings, so a circle floating clear of the shape is
            // legibly part of it rather than a stray mark.
            var top = new Point((outline[0].X + outline[1].X) / 2, (outline[0].Y + outline[1].Y) / 2);
            SelectionLayer.Children.Add(new Line
            {
                X1 = top.X,
                Y1 = top.Y,
                X2 = at.X,
                Y2 = at.Y,
                Stroke = _chromeStroke,
                StrokeThickness = 1,
                IsHitTestVisible = false,
            });
        }

        var round = handle.Kind is AnnotationHandleKind.Rotate
            or AnnotationHandleKind.Bend
            or AnnotationHandleKind.Waypoint;
        var shape = new Rectangle
        {
            Width = HandleSize,
            Height = HandleSize,

            // Round for the handles that shape the mark rather than move a corner of it, so
            // the shape of the grab point says what letting go will do. The anchors are
            // among them: a bent line's ends and its anchors sit on the same curve, and a
            // square end against round anchors is what says which two are the ends. macshot
            // draws them apart the same way, endpoints filled in the accent colour and
            // anchors filled in white (OverlayView.swift:4343-4365).
            RadiusX = round ? HandleSize / 2 : 1,
            RadiusY = round ? HandleSize / 2 : 1,
            Fill = _handleFill,
            Stroke = _chromeStroke,
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(shape, at.X - (HandleSize / 2));
        Canvas.SetTop(shape, at.Y - (HandleSize / 2));
        SelectionLayer.Children.Add(shape);
    }

    /// <summary>
    /// Whether <paramref name="tool"/> is placed with a click rather than dragged out.
    /// </summary>
    /// <remarks>
    /// Sprite tools take their size from the rasterized pixels, so there is nothing left
    /// for a drag to decide. The editor is still told about the press first — it may take
    /// hold of a mark already there — and answers with nothing to drag, which is what
    /// keeps its move and release handlers no-ops for a placement.
    /// </remarks>
    public static bool IsPlacedByClick(AnnotationTool tool) => Annotation.RequiresSprite(tool);

    /// <summary>
    /// Starts placing the active sprite tool's mark at <paramref name="point"/>. Text
    /// opens an entry box and commits later; the other two commit as soon as their
    /// glyphs are rasterized. See <c>docs/windows-port/architecture.md</c>, decision D7.
    /// </summary>
    public void PlaceSprite(CapturePoint point)
    {
        switch (_editor?.Tool)
        {
            case AnnotationTool.Text:
                QueueSprite(() => ReplaceTextEntryAsync(point));
                return;
            case AnnotationTool.Number:
                QueueSprite(() => PlaceNumberAsync(point));
                return;
            case AnnotationTool.Stamp:
                QueueSprite(() => PlaceStampAsync(point));
                return;
            default:
                return;
        }
    }

    /// <summary>
    /// Lands everything in flight, so the pixels handed over are the ones the user made.
    /// </summary>
    /// <remarks>
    /// Text still in the entry box is part of the image the moment the user finishes,
    /// and clicking Done is finishing. An in-flight mark is not: the preview still shows
    /// the draft, so it is dropped and the preview brought back into agreement before
    /// its pixels are taken.
    /// </remarks>
    public async Task FlushAsync()
    {
        QueueSprite(CommitTextEntryAsync);
        await _pendingSprite;

        if (_editor?.Cancel() == true)
        {
            Render();
        }
    }

    /// <summary>
    /// Reads the text in the pixels on show.
    /// </summary>
    /// <remarks>
    /// From the preview's own source rather than from the host's screenshot: for a
    /// snapped window those are different images, and the one worth reading is the one
    /// the user is looking at. The line boxes come back in frame space, which is what
    /// the redaction rectangles are then placed in.
    /// </remarks>
    public async Task<IReadOnlyList<RecognizedLine>> RecognizeAsync()
    {
        if (_source is not { } source)
        {
            return [];
        }

        return await TextRecognizer.RecognizeAsync(source, _region.X, _region.Y);
    }

    /// <summary>
    /// Lays each translation over the words it replaces, and answers how many were
    /// placed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The boxes are painted the average colour of the pixels underneath, sampled from
    /// the preview's own source: a translation on a grey panel wants that grey behind it,
    /// and a white box would announce itself as a patch.
    /// </para>
    /// <para>
    /// One <c>AddRange</c> under one group, so a single Ctrl+Z takes the whole page of
    /// translations back off. Put in one at a time they would need one undo each, and a
    /// half-undone page is worse than either state.
    /// </para>
    /// </remarks>
    public async Task<int> LayTranslationsOverAsync(IReadOnlyList<TranslatedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (_editor is not { } editor || _source is not { } source)
        {
            return 0;
        }

        var group = Guid.NewGuid();
        var placed = new List<Annotation>(lines.Count);
        foreach (var line in lines)
        {
            // The line boxes came back in frame space; the pixels are the crop, which
            // starts at the region's own corner.
            var underneath = new CaptureRegion(
                line.Bounds.X - _region.X,
                line.Bounds.Y - _region.Y,
                line.Bounds.Width,
                line.Bounds.Height);

            var average = PixelEffects.AverageColor(source.BgraPixels, source.Width, source.Height, underneath);
            var background = Color.FromArgb(byte.MaxValue, average.Red, average.Green, average.Blue);

            var box = TranslationGlyphs.Build(line, background, SpriteScale);
            var sprite = await GlyphSpriteFactory.RenderAsync(SpriteHost, box);

            placed.Add(Annotation.CreateSprite(
                AnnotationTool.Text,
                new CapturePoint(line.Bounds.X, line.Bounds.Y),
                sprite,
                editor.Style) with
            {
                // Kept alongside the pixels so the translation is still readable as
                // text — by the history, and by whatever reads a capture back.
                Text = line.Text,
                GroupId = group,
            });
        }

        editor.Document.AddRange(placed);
        Render();
        return placed.Count;
    }

    /// <summary>
    /// Runs sprite work behind whatever is already in flight, and keeps the tail of the
    /// chain so finishing can wait for all of it.
    /// </summary>
    /// <remarks>
    /// Sprites are rasterized asynchronously, so two placements a moment apart would
    /// otherwise interleave: clicking away from a half-typed label would open the next
    /// entry box before the previous one had committed, and the first label would be
    /// lost. Ordering them is cheaper than making each one defend itself.
    /// </remarks>
    private void QueueSprite(Func<Task> work) => _pendingSprite = RunAfterAsync(_pendingSprite, work);

    private async Task RunAfterAsync(Task previous, Func<Task> work)
    {
        try
        {
            await previous;
            await work();
        }
        catch (Exception exception)
        {
            // Reported through the host's hint line rather than a dialog: an overlay is a
            // borderless always-on-top window covering the screen, so a dialog has
            // nowhere to go. Catching here also keeps one failed placement from poisoning
            // every later one that waits on it.
            _reportHint(exception.Message);
        }
    }

    /// <summary>
    /// Finishes whatever was being typed before starting the next label, so clicking
    /// elsewhere moves the text on rather than abandoning it.
    /// </summary>
    private async Task ReplaceTextEntryAsync(CapturePoint point)
    {
        await CommitTextEntryAsync();
        BeginTextEntry(point);
    }

    /// <summary>
    /// Places a numbered badge centred on <paramref name="point"/>. Rasterizing the
    /// digits is async, so the badge appears a frame or two after the click — which is
    /// exactly why a sprite is produced once when the annotation is committed and never
    /// from inside the draw path.
    /// </summary>
    private async Task PlaceNumberAsync(CapturePoint point)
    {
        if (_editor is not { } editor)
        {
            return;
        }

        // The badge's own remembered width, which is what sizes the circle — see
        // AnnotationStyle.ForTool. Read from editor.Style, it would be drawn at whatever
        // the last arrow was set to.
        var style = editor.DrawingStyle;

        // The next number is read off the document rather than kept in a counter, so
        // undoing a badge frees its number instead of leaving a hole in the sequence.
        // Offset by where the user asked the sequence to start, which is how a screenshot
        // carries on the numbering of the figure before it.
        var value = NumberStartAt()
            + editor.Document.Annotations.Count(existing => existing.Tool == AnnotationTool.Number);

        var badge = NumberBadge.Build(value, style, SpriteScale);
        var sprite = await GlyphSpriteFactory.RenderAsync(SpriteHost, badge);

        Commit(Annotation.CreateSprite(AnnotationTool.Number, Centred(point, sprite), sprite, style) with
        {
            // Kept alongside the pixels so the badge stays readable as data, not only
            // as an image.
            NumberValue = value,
        });
    }

    private async Task PlaceStampAsync(CapturePoint point)
    {
        if (_editor is not { } editor)
        {
            return;
        }

        var style = editor.Style;

        if (StampPicture() is { } picture)
        {
            var image = StampGlyph.BuildPicture(picture, style, SpriteScale);
            var stamped = await GlyphSpriteFactory.RenderAsync(SpriteHost, image);

            // No Text: the emoji field describes the mark, and a picture has no character
            // that stands for it. The sprite carries the pixels, as it does for every
            // other stamp.
            Commit(Annotation.CreateSprite(AnnotationTool.Stamp, Centred(point, stamped), stamped, style));
            return;
        }

        var emoji = StampEmoji();

        var glyph = StampGlyph.Build(emoji, style, SpriteScale);
        var sprite = await GlyphSpriteFactory.RenderAsync(SpriteHost, glyph);

        Commit(Annotation.CreateSprite(AnnotationTool.Stamp, Centred(point, sprite), sprite, style) with
        {
            Text = emoji,
        });
    }

    /// <summary>
    /// Opens the on-canvas entry box. The box is what makes the text tool feel like
    /// typing on the screenshot rather than filling in a dialog, and it uses the same
    /// font size the sprite will, so what is typed is what is committed.
    /// </summary>
    private void BeginTextEntry(CapturePoint point)
    {
        if (_editor is not { } editor)
        {
            return;
        }

        var position = _placement.ToLayout(point);
        var entry = new TextBox
        {
            MinWidth = 120,

            // No padding, so the first glyph sits at the click point rather than inset
            // from it by whatever the theme's padding happens to be.
            Padding = new Thickness(0),
            FontSize = TextGlyphs.FontSizeFor(editor.Style, SpriteScale),
            FontFamily = TextGlyphs.FamilyFor(editor.Style),
            FontWeight = TextGlyphs.WeightFor(editor.Style),
            FontStyle = TextGlyphs.SlantFor(editor.Style),
            TextAlignment = TextGlyphs.AlignmentFor(editor.Style),

            // The underline, the strikethrough and the line round the glyphs are missing
            // here and drawn on the committed sprite. A TextBox has no decorations and no
            // way to stroke what it is showing, and the three ways round that — a
            // RichEditBox, a TextBlock laid over the box, a box that is really a canvas —
            // each cost more of what typing on the screenshot feels like than they buy.
            Foreground = new SolidColorBrush(GlyphSpriteFactory.ToBrushColor(editor.Style)),
            // Return has to be accepted for Shift+Enter to be able to insert one; plain
            // Enter is taken below before the box ever sees it.
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
        };

        entry.KeyDown += TextEntry_KeyDown;
        entry.LostFocus += TextEntry_LostFocus;
        Canvas.SetLeft(entry, position.X);
        Canvas.SetTop(entry, position.Y);
        TextEntryLayer.Children.Add(entry);
        _textEntry = entry;
        _textEntryOrigin = point;
        entry.Focus(FocusState.Programmatic);
        _reportHint(Localization.L("Type the label • Enter to place • Shift+Enter for a new line • Esc to discard it"));
    }

    private void TextEntry_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter when IsShiftDown():
                // Left for the box to insert. A label that has to say two things needs
                // two lines, and the alternative — placing a second label underneath —
                // means lining them up by hand.
                return;

            case VirtualKey.Enter:
                // Handled here, or it would bubble up and finish the whole capture.
                e.Handled = true;
                QueueSprite(CommitTextEntryAsync);
                return;

            case VirtualKey.Escape:
                // The first Escape discards the text being typed, the same way it
                // discards a half-drawn mark before it cancels the capture.
                e.Handled = true;
                RemoveTextEntry();
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// Clicking anywhere else — another tool, the canvas, Done — means the text is
    /// finished, so it is committed rather than lost.
    /// </summary>
    private void TextEntry_LostFocus(object sender, RoutedEventArgs e) => QueueSprite(CommitTextEntryAsync);

    /// <summary>
    /// Read from the keyboard rather than from the event, because a
    /// <see cref="KeyRoutedEventArgs"/> carries the key that was pressed and not the
    /// modifiers held with it.
    /// </summary>
    private static bool IsShiftDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

    private async Task CommitTextEntryAsync()
    {
        if (_textEntry is not { } entry || _editor is not { } editor)
        {
            return;
        }

        var text = entry.Text.Trim();
        var origin = _textEntryOrigin;
        var style = editor.Style;

        // Torn down before the await, so the LostFocus that removing the box raises
        // cannot commit the same text a second time.
        RemoveTextEntry();
        if (text.Length == 0)
        {
            return;
        }

        var glyphs = TextGlyphs.Build(text, style, SpriteScale);
        var sprite = await GlyphSpriteFactory.RenderAsync(SpriteHost, glyphs);

        // Anchored at the click rather than centred on it: the user typed from there.
        Commit(Annotation.CreateSprite(AnnotationTool.Text, origin, sprite, style) with { Text = text });
    }

    private void RemoveTextEntry()
    {
        if (_textEntry is not { } entry)
        {
            return;
        }

        _textEntry = null;
        entry.KeyDown -= TextEntry_KeyDown;
        entry.LostFocus -= TextEntry_LostFocus;
        TextEntryLayer.Children.Remove(entry);

        // The keyboard belongs to the host again: Enter finishes, Ctrl+Z undoes. Which
        // element gets it back is the host's business, so it is told rather than asked.
        TypingEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raised when the entry box closes, so the host can take the keyboard back and put
    /// its standing instruction back in the hint line.
    /// </summary>
    public event EventHandler? TypingEnded;

    private void Commit(Annotation annotation)
    {
        _editor?.Document.Add(annotation);
        Render();
    }

    /// <summary>
    /// The scale XAML will actually rasterize at, which is what decides how many pixels
    /// a sprite comes out as. Named for what it is for rather than after
    /// <see cref="UIElement.RasterizationScale"/>, which this would otherwise hide —
    /// and which reads 0 until the element has been arranged.
    /// </summary>
    private double SpriteScale => _rasterizationScale?.Invoke() ?? 1;

    /// <summary>
    /// A mark aimed at a point belongs centred on it; anchoring its top-left there would
    /// drop it down and right of what the user aimed at.
    /// </summary>
    private static CapturePoint Centred(CapturePoint point, AnnotationSprite sprite) =>
        new(point.X - (sprite.Width / 2.0), point.Y - (sprite.Height / 2.0));
}
