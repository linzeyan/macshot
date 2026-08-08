using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Annotations;

[Flags]
public enum EditorModifiers
{
    None = 0,

    /// <summary>Shift: snap lines to 45 degrees and force shapes square.</summary>
    Constrain = 1,

    /// <summary>
    /// Alt — macOS's Option: ignore the marks under the pointer so the tool draws over
    /// them instead of grabbing them.
    /// </summary>
    /// <remarks>
    /// Without it there is no way to start a mark on top of one already drawn, because a
    /// press inside an existing mark is a grab. That is fine for an arrow placed beside a
    /// shape and useless for a censor, whose whole job is to cover what is underneath —
    /// including macshot's own marks. macOS's <c>drawThrough</c>
    /// (<c>OverlayView.swift:8211-8215</c>).
    /// </remarks>
    DrawThrough = 2,

    /// <summary>
    /// Ctrl — macOS's Control: a press on a line, arrow or ruler bends it through another
    /// anchor there instead of starting a gesture.
    /// </summary>
    /// <remarks>
    /// macOS reaches this two ways, Control-click and right-click
    /// (<c>OverlayView.swift:5491</c> and <c>:6851</c>). Only the first is ported: over a
    /// capture the right button already opens the ring of colours where the pointer is, and
    /// taking that away would send every colour change back across the screen to the
    /// toolbar.
    /// </remarks>
    AddAnchor = 4,
}

/// <summary>
/// The annotation interaction state machine: which tool is active, what is being
/// drawn or dragged, and what the canvas should show right now.
/// </summary>
/// <remarks>
/// This is the portable half of the macOS <c>OverlayView</c> plus its tool
/// handlers. Keeping it out of the UI layer is what makes drag behavior, snapping,
/// and undo granularity testable without a display attached: the UI only has to
/// convert pointer events to frame-space points and draw
/// <see cref="VisibleAnnotations"/>.
/// </remarks>
public sealed class AnnotationEditor
{
    /// <summary>
    /// A press that barely moves is a click, not a shape. Without this a stray
    /// click litters the canvas with invisible zero-size annotations that still
    /// consume undo steps and still answer hit tests.
    /// </summary>
    public const double MinimumDragDistance = 3;

    /// <summary>
    /// The lightest pressure a sample mid-stroke is recorded at. A digitizer that reports
    /// zero for one frame is reporting noise, not a lifted pen, and honouring it would put
    /// a break in a stroke the user drew in one movement.
    /// </summary>
    private const double MinRecordedPressure = 0.05;

    private readonly AnnotationDocument _document;
    private AnnotationTool _tool = AnnotationTool.Arrow;
    private CapturePoint _origin;
    private List<CapturePoint>? _freeformSamples;
    private List<double>? _freeformPressures;
    private Annotation? _dragTarget;

    // The whole handle rather than its kind, because an anchor grip is told apart from the
    // ones beside it only by its index — the kind alone would drag whichever came first.
    private AnnotationHandle? _handle;
    private bool _isPressed;
    private double _scale = 1;

    public AnnotationEditor(AnnotationDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public AnnotationDocument Document => _document;

    public AnnotationTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value)
            {
                return;
            }

            // Switching tools abandons anything in flight rather than finishing it
            // with the wrong tool's semantics.
            Cancel();
            Selected = null;
            _tool = value;
        }
    }

    public AnnotationStyle Style { get; set; } = AnnotationStyle.Default;

    /// <summary>
    /// Frame pixels to the layout unit on the surface being drawn on: one display's DPI
    /// scaling over an overlay, and one over an image laid out at its own pixel size.
    /// </summary>
    /// <remarks>
    /// Only the grab points read it, and only because they are sizes a hand aims at
    /// rather than distances in the capture. Everything else here is in frame pixels
    /// throughout, which is why this is a property of the editor and not a parameter on
    /// every call.
    /// </remarks>
    public double Scale
    {
        get => _scale;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _scale = value;
        }
    }

    /// <summary>
    /// How much a finished freehand stroke is rounded off. Smoothed by default, because a
    /// path sampled from a mouse is a staircase and nobody draws one on purpose.
    /// </summary>
    public PencilSmoothing Smoothing { get; set; } = PencilSmoothing.Smooth;

    /// <summary>
    /// Whether a freehand stroke thins and thickens with how hard the pen is pressed.
    /// </summary>
    /// <remarks>
    /// The setting only says the user wants it; whether a stroke gets it depends on the
    /// device, and the host answers that by passing a pressure of zero for anything that
    /// does not report one. A mouse reports a constant half-press, and honouring that
    /// would silently draw every stroke at three quarters of the width the slider says.
    /// </remarks>
    public bool PenPressure { get; set; }

    /// <summary>
    /// How the ring round a spotlight is drawn. Dashed by default, as macshot's is.
    /// </summary>
    /// <remarks>
    /// Its own setting rather than a read of <see cref="Style"/>, because the spotlight's
    /// border and the dash picker the other tools share are two different choices that
    /// happen to be spelled with the same enum. Routing it through the style would mean the
    /// last spotlight drawn decided what a dashed line meant for every tool after it.
    /// </remarks>
    public LineStyle SpotlightBorder { get; set; } = LineStyle.Dashed;

    /// <summary>
    /// Whether marks line up with the marks already there — macshot's
    /// <c>snapGuidesEnabled</c>, on by default. Set by the host from the settings.
    /// </summary>
    public bool SnapGuides { get; set; } = true;

    /// <summary>
    /// What marks line up against besides each other: the capture's own edges and centre.
    /// Empty until the host says what the capture is, and then nothing lines up with the
    /// picture — only with other marks.
    /// </summary>
    public CaptureRegion SnapRegion { get; set; }

    /// <summary>
    /// Whether the ruler is held inside <see cref="SnapRegion"/> — macshot's
    /// <c>measureClampToSelection</c>, on as it is there. Set by the host from the
    /// settings, the way <see cref="SnapGuides"/> is.
    /// </summary>
    /// <remarks>
    /// The ruler alone, because it is the only tool whose mark is a claim about a distance.
    /// Every other mark that leaves the region is simply cropped at the edge, but a rule
    /// dragged past it reports a span the capture does not contain — and a wrong number
    /// written on the picture is worse than a mark trimmed off it.
    /// </remarks>
    public bool ClampRulerToRegion { get; set; } = true;

    /// <summary>
    /// Where the mark in flight lined up, for the host to draw. Cleared the moment the
    /// gesture ends: a guide left on screen is a line the user has to work out the
    /// meaning of.
    /// </summary>
    public SnapResult Snap { get; private set; }

    /// <summary>The annotation currently being drawn or dragged, for live preview.</summary>
    public Annotation? Draft { get; private set; }

    /// <summary>
    /// The ruler the auto-measure keys are offering: drawn on the canvas, not in the
    /// document, until it is taken.
    /// </summary>
    /// <remarks>
    /// Its own property rather than <see cref="Draft"/>, because it is not a gesture. No
    /// button is down while it is showing, it survives the pointer moving anywhere, and it
    /// is replaced wholesale on each move rather than extended from an origin — so putting
    /// it in Draft would have every gesture-shaped question in this class
    /// (<see cref="IsDragging"/>, cancel, commit-on-release) answering about something the
    /// user is not dragging.
    /// </remarks>
    public Annotation? AutoSpan { get; private set; }

    public Annotation? Selected { get; private set; }

    /// <summary>
    /// The selected annotation as it stands right now: its in-flight copy while a handle
    /// or a move is being dragged, so the chrome drawn around it tracks the drag instead
    /// of staying where the shape used to be.
    /// </summary>
    public Annotation? SelectionShown => _dragTarget is not null && Draft is not null ? Draft : Selected;

    /// <summary>
    /// The handles the canvas should draw, for whatever tool is in hand.
    /// </summary>
    /// <remarks>
    /// Not only for the pointer tool. <see cref="BeginSelection"/> tries the selected
    /// mark's handles before anything else whatever tool is armed, so offering them only
    /// under the pointer left every other tool with handles that worked and could not be
    /// seen — a press near a corner reshaping a mark the user thought they were drawing
    /// beside. macOS draws them the same way, from the selection rather than from the tool
    /// (<c>OverlayView.swift:1852-1856</c>).
    /// </remarks>
    public IReadOnlyList<AnnotationHandle> Handles =>
        SelectionShown is { } shown ? AnnotationHandles.For(shown, _scale) : [];

    public bool IsDragging => _isPressed && Draft is not null;

    /// <summary>
    /// What the canvas should render: the committed annotations, with the one being
    /// dragged swapped for its in-flight copy, plus any annotation being drawn.
    /// </summary>
    public IEnumerable<Annotation> VisibleAnnotations
    {
        get
        {
            foreach (var annotation in _document.Annotations)
            {
                if (_dragTarget is not null && annotation.Id == _dragTarget.Id && Draft is not null)
                {
                    yield return Draft;
                    continue;
                }

                yield return annotation;
            }

            if (_dragTarget is null && Draft is not null)
            {
                yield return Draft;
            }

            // Last, so the offer is drawn over everything it might be measuring rather
            // than under it.
            if (AutoSpan is not null)
            {
                yield return AutoSpan;
            }
        }
    }

    /// <summary>
    /// Offers a ruler between two points without committing it.
    /// </summary>
    /// <remarks>
    /// Rebuilt rather than amended on each call: the two ends both move as the pointer
    /// does, so there is nothing of the previous offer worth keeping.
    /// </remarks>
    public void ProposeSpan(CapturePoint from, CapturePoint to) =>
        AutoSpan = Annotation.Create(AnnotationTool.Measure, from, to, Style);

    /// <summary>
    /// Puts the offered ruler on the canvas and clears the offer.
    /// </summary>
    /// <returns>What was committed, or null when there was nothing offered.</returns>
    /// <remarks>
    /// The offer is cleared rather than left showing, and the caller makes a fresh one at
    /// the pointer's position. Leaving it would draw the committed ruler twice, and the
    /// copy on top would move away from the one underneath at the next mouse move.
    /// </remarks>
    public Annotation? CommitSpan()
    {
        if (AutoSpan is not { } offered)
        {
            return null;
        }

        AutoSpan = null;
        _document.Add(offered);
        return offered;
    }

    /// <summary>Takes the offer back. True when there was one to take back.</summary>
    public bool ClearSpan()
    {
        if (AutoSpan is null)
        {
            return false;
        }

        AutoSpan = null;
        return true;
    }

    /// <summary>
    /// Starts a gesture. Returns true when it took hold of a mark already drawn rather
    /// than starting a new one, which is what the caller needs to know before placing a
    /// label or a badge where the press landed.
    /// </summary>
    /// <remarks>
    /// A click on an existing mark grabs it whatever tool is in hand, so there is no
    /// pointer tool to switch to first — the same rule macOS uses
    /// (<c>OverlayView.swift:8254-8298</c>). The exceptions are the tools where grabbing
    /// would take the gesture away from what it is for: a freehand stroke drawn over an
    /// earlier mark is a stroke, not a grab. Drawing a mark deliberately on top of another
    /// is what <see cref="EditorModifiers.DrawThrough"/> is for.
    /// </remarks>
    /// <param name="pressure">
    /// How hard the pen is pressed, from 0 to 1, or 0 for a device that does not report
    /// it. Zero at the press is what decides the whole stroke: a pen lifted mid-stroke
    /// would otherwise turn a pressure stroke into a plain one halfway along.
    /// </param>
    public bool PointerPressed(
        CapturePoint point,
        EditorModifiers modifiers = EditorModifiers.None,
        double pressure = 0)
    {
        Cancel();
        _isPressed = true;
        _origin = point;

        // Ahead of everything else, and whatever tool is in hand. Adding an anchor is a
        // command rather than the start of a gesture: the mark is edited and committed
        // here, so the press is closed out and the release that follows has nothing left
        // to do. Answered as a grab, which is what keeps a label or a badge from also
        // being placed where the press landed.
        if (modifiers.HasFlag(EditorModifiers.AddAnchor) && AddAnchor(point))
        {
            _isPressed = false;
            return true;
        }

        if (GrabsExistingMarks(_tool, modifiers) && BeginSelection(point))
        {
            return true;
        }

        // Anything that starts a new mark clears the selection: the chrome around a
        // mark that is no longer the subject of the gesture is a lie about what Delete
        // would remove.
        Selected = null;

        // A sprite tool draws nothing here. Its mark is rasterized by the UI and handed
        // back, so the press only had to answer whether it grabbed something.
        if (Annotation.RequiresSprite(_tool))
        {
            return false;
        }

        if (IsFreeform(_tool))
        {
            _freeformSamples = [point];
            _freeformPressures = PenPressure && pressure > 0 ? [pressure] : null;
            Draft = Annotation.CreateFreeform(_tool, _freeformSamples, Style, _freeformPressures);
            return false;
        }

        // The loupe is placed rather than dragged out — macshot's own gesture
        // (LoupeToolHandler.swift:16-33) — so its width comes from the row and a drag only
        // decides where it lands. Dragged out, it would be the one tool whose size the
        // user sets twice: once on the slider and again with the mouse, with the second
        // silently winning.
        if (_tool == AnnotationTool.Loupe)
        {
            Draft = Placed(point, Style.LoupeSize);
            return false;
        }

        // Both ends of a ruler are held inside the region, not only the end being dragged:
        // a press that landed outside it would otherwise root the rule off the picture and
        // every reading taken from it would start from nowhere. macshot clamps the origin
        // in the same place (MeasureToolHandler.swift:10-11).
        var start = RuledInside(point);
        _origin = start;

        // The spotlight's ring takes its own border rather than the row's dash picker,
        // which is what macshot stamps on it as it is created
        // (HighlightToolHandler.swift:32-33).
        Draft = Annotation.Create(
            _tool,
            start,
            start,
            _tool == AnnotationTool.Highlight ? Style with { LineStyle = SpotlightBorder } : Style);
        return false;
    }

    /// <param name="pressure">
    /// How hard the pen is pressed at this sample. Only read while a pressure stroke is
    /// in flight, and floored rather than trusted: a digitizer reporting a momentary zero
    /// mid-stroke would otherwise put a gap in the line.
    /// </param>
    public void PointerMoved(
        CapturePoint point,
        EditorModifiers modifiers = EditorModifiers.None,
        double pressure = 0)
    {
        if (!_isPressed || Draft is null)
        {
            return;
        }

        if (_dragTarget is not null)
        {
            if (_handle is { } handle)
            {
                Draft = AnnotationHandles.Drag(_dragTarget, handle.Kind, point, modifiers, handle.Index);
                Snap = SnapResult.None;
                return;
            }

            var moved = _dragTarget.Translate(point.X - _origin.X, point.Y - _origin.Y);
            Snap = SnapFor(moved.BoundingRect, modifiers, _dragTarget.Id);
            Draft = Snap == SnapResult.None ? moved : moved.Translate(Snap.Dx, Snap.Dy);
            return;
        }

        if (_freeformSamples is not null)
        {
            _freeformSamples.Add(point);
            _freeformPressures?.Add(Math.Clamp(pressure, MinRecordedPressure, 1));
            Draft = Annotation.CreateFreeform(_tool, _freeformSamples, Style, _freeformPressures);
            return;
        }

        // The loupe keeps the width the row gave it and follows the pointer instead of
        // stretching, which is what makes the drag a placement rather than a second way of
        // sizing it.
        if (_tool == AnnotationTool.Loupe)
        {
            Draft = Placed(point, Style.LoupeSize);
            return;
        }

        // Only the corner under the pointer, because the other one is where the press
        // landed and pulling that about would move ink the user already placed.
        var end = RuledInside(Constrain(_tool, _origin, point, modifiers), modifiers);
        Snap = SnapFor(new CaptureRegion(end.X, end.Y, 0, 0), modifiers, null);
        Draft = Draft with { End = new CapturePoint(end.X + Snap.Dx, end.Y + Snap.Dy) };
    }

    /// <summary>
    /// Where <paramref name="moved"/> lines up, or nothing at all when the user has said
    /// they do not want it.
    /// </summary>
    /// <param name="exclude">
    /// The mark being dragged, which must not line up against its own old position — it
    /// would be within nothing of it and never leave.
    /// </param>
    /// <remarks>
    /// Shift turns snapping off for the gesture, because Shift already means "the exact
    /// angle I asked for" and a nudge afterwards would take it away again. It is also the
    /// way out when a mark genuinely belongs three pixels from another one.
    /// </remarks>
    private SnapResult SnapFor(CaptureRegion moved, EditorModifiers modifiers, Guid? exclude)
    {
        if (!SnapGuides || modifiers.HasFlag(EditorModifiers.Constrain))
        {
            return SnapResult.None;
        }

        var others = exclude is { } id
            ? _document.Annotations.Where(a => a.Id != id)
            : _document.Annotations;

        return AnnotationSnapping.ForMove(moved, SnapRegion, others);
    }

    /// <summary>
    /// Ends the gesture, committing it to the document when it produced something, and
    /// answers the mark it committed.
    /// </summary>
    /// <remarks>
    /// The mark comes back rather than the caller reading it off the document, because
    /// some of them are not finished when the drag stops: a ruler has no reading until its
    /// span is known, and a highlighter set to snap has to be told what text it crossed.
    /// Both need pixels read asynchronously, which is the host's work and not this class's
    /// — and the host has to be told exactly which mark to finish, not merely that
    /// something happened.
    /// </remarks>
    /// <param name="pressure">
    /// How hard the pen was pressed as it left the surface. Carried through to the last
    /// sample: without it the final sample of every pressure stroke would record nothing,
    /// and each stroke would taper away to a hairline as it was let go.
    /// </param>
    public Annotation? PointerReleased(
        CapturePoint point,
        EditorModifiers modifiers = EditorModifiers.None,
        double pressure = 0)
    {
        if (!_isPressed)
        {
            return null;
        }

        PointerMoved(point, modifiers, pressure);
        _isPressed = false;

        var draft = Draft;
        var dragTarget = _dragTarget;
        Snap = SnapResult.None;
        Draft = null;
        _dragTarget = null;
        _handle = null;
        _freeformSamples = null;
        _freeformPressures = null;

        if (draft is null)
        {
            return null;
        }

        if (dragTarget is not null)
        {
            // The whole drag is one undo step. Committing every intermediate
            // position would make Ctrl+Z replay the mouse path.
            if (AnnotationHandles.Differ(draft, dragTarget))
            {
                _document.Replace(draft);
                Selected = draft;
            }

            // Reshaping a mark that is already there is not committing a new one, so
            // nothing here is unfinished and there is nothing for the host to finish.
            return null;
        }

        if (!IsWorthKeeping(draft))
        {
            return null;
        }

        var committed = Finished(draft);
        _document.Add(committed);
        return committed;
    }

    /// <summary>
    /// The mark as it is committed. Smoothing happens here rather than during the drag:
    /// rounding a path the user is still adding to would move the ink they just laid down
    /// and read as the stroke lagging behind the pointer.
    /// </summary>
    private Annotation Finished(Annotation draft)
    {
        if (Smoothing == PencilSmoothing.None || draft.Points.Count < 3)
        {
            return draft;
        }

        var smoothed = StrokeSmoothing.Smooth(draft.Points, Smoothing);
        return draft with
        {
            Points = smoothed,

            // Smoothing changes how many samples there are, and the pressures are one per
            // sample. Resampled rather than dropped: the taper is the whole point of
            // having drawn with a pen, and losing it whenever smoothing is on would mean
            // the two options quietly cancel each other out.
            Pressures = Resampled(draft.Pressures, smoothed.Count),

            // Start and End follow the points, since a freeform mark's ends are its
            // first and last samples and everything from hit testing to the bounding
            // rectangle reads them.
            Start = smoothed[0],
            End = smoothed[^1],
        };
    }

    /// <summary>
    /// Stretches a run of pressures over a different number of samples, keeping its shape.
    /// </summary>
    /// <remarks>
    /// By position along the run rather than by arc length. Smoothing moves the samples a
    /// little and adds more of them, so the two runs describe the same stroke at different
    /// resolutions — which is exactly the case where the cheap mapping and the careful one
    /// agree to well under a pixel of width.
    /// </remarks>
    private static IReadOnlyList<double> Resampled(IReadOnlyList<double> pressures, int count)
    {
        if (pressures.Count == 0 || count <= 0)
        {
            return [];
        }

        if (pressures.Count == count)
        {
            return pressures;
        }

        if (pressures.Count == 1 || count == 1)
        {
            return [.. Enumerable.Repeat(pressures[0], count)];
        }

        var stretched = new double[count];
        for (var index = 0; index < count; index++)
        {
            var at = (double)index / (count - 1) * (pressures.Count - 1);
            var lower = Math.Clamp((int)at, 0, pressures.Count - 2);
            var into = at - lower;
            stretched[index] = pressures[lower] + ((pressures[lower + 1] - pressures[lower]) * into);
        }

        return stretched;
    }

    /// <summary>Abandons an in-flight gesture. Returns whether there was one.</summary>
    public bool Cancel()
    {
        if (Draft is null && !_isPressed)
        {
            return false;
        }

        Draft = null;
        Snap = SnapResult.None;
        _dragTarget = null;
        _handle = null;
        _freeformSamples = null;
        _freeformPressures = null;
        _isPressed = false;
        return true;
    }

    public bool DeleteSelected()
    {
        if (Selected is null)
        {
            return false;
        }

        var removed = _document.Remove(Selected.Id);
        Selected = null;
        return removed;
    }

    public bool Undo()
    {
        Cancel();
        var undone = _document.Undo();
        DropStaleSelection();
        return undone;
    }

    public bool Redo()
    {
        Cancel();
        var redone = _document.Redo();
        DropStaleSelection();
        return redone;
    }

    /// <summary>
    /// Bends the line, arrow or ruler under <paramref name="point"/> through one more
    /// anchor, put where the press landed. False when nothing under the pointer takes one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The selected mark is tried first and anything under the pointer second, selecting
    /// what it finds — macshot's own order (<c>OverlayView.swift:6853-6875</c>). The reason
    /// for it is that the selected mark wears the chrome the user is aiming at, so a press
    /// on the curve they can see has to reach the mark they can see it on rather than
    /// whatever else happens to be within a few pixels.
    /// </para>
    /// <para>
    /// One undo step, because it is one edit to one mark: <see cref="AnnotationDocument.Replace"/>
    /// rather than <c>Amend</c>, so Ctrl+Z takes the anchor back off instead of leaving a
    /// bend the user cannot undo.
    /// </para>
    /// </remarks>
    public bool AddAnchor(CapturePoint point)
    {
        var target = Anchorable(Selected, point) ?? TopmostAnchorable(point);
        if (target is null)
        {
            return false;
        }

        var bent = target.WithAnchorAt(point);
        if (!_document.Replace(bent))
        {
            return false;
        }

        Selected = bent;
        return true;
    }

    private static Annotation? Anchorable(Annotation? annotation, CapturePoint point) =>
        annotation is not null
            && Annotation.AcceptsWaypoints(annotation.Tool)
            && annotation.HitTest(point)
                ? annotation
                : null;

    /// <summary>
    /// The topmost line, arrow or ruler under the pointer, ignoring whatever else is over
    /// it.
    /// </summary>
    /// <remarks>
    /// Not <see cref="AnnotationDocument.HitTest"/>, which answers with the topmost mark of
    /// any kind: a curve is a few pixels wide and is very often crossed by the very shape
    /// it points at, so refusing the anchor because a rectangle is in front would make the
    /// gesture unreliable exactly where it is most wanted. macshot searches the same way.
    /// </remarks>
    private Annotation? TopmostAnchorable(CapturePoint point)
    {
        var annotations = _document.Annotations;
        for (var index = annotations.Count - 1; index >= 0; index--)
        {
            if (Anchorable(annotations[index], point) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Takes hold of the mark under the pointer, or of a handle on the one already
    /// selected. False when there was nothing there to take.
    /// </summary>
    private bool BeginSelection(CapturePoint point)
    {
        // The selected annotation's handles are tried before anything else, so a handle
        // can be grabbed even where a later mark covers it — otherwise reshaping the
        // rectangle under a stamp would be impossible without moving the stamp first.
        if (Selected is { } selected && AnnotationHandles.At(selected, point, _scale) is { } handle)
        {
            _handle = handle;
            _dragTarget = selected;
            Draft = selected;
            return true;
        }

        var hit = _document.HitTest(point);
        if (hit is null)
        {
            // Only the pointer tool clears the selection on a miss. For every other
            // tool the press is about to draw, and clearing there would be doing it
            // twice with different rules.
            if (_tool == AnnotationTool.Select)
            {
                Selected = null;
            }

            return false;
        }

        Selected = hit;
        if (!hit.IsMovable)
        {
            // Selected, so it can be deleted or restyled, but there is nothing to drag.
            return true;
        }

        _dragTarget = hit;
        Draft = hit;
        return true;
    }

    /// <summary>
    /// Whether this tool takes hold of what is already on the canvas. False for the
    /// freehand tools, which are used to draw over marks often enough that grabbing
    /// would be wrong more often than right, and false for any tool while
    /// <see cref="EditorModifiers.DrawThrough"/> is held.
    /// </summary>
    /// <remarks>
    /// The pointer tool keeps grabbing whatever is held: interacting with marks is the
    /// whole of what it does, so a modifier that turned that off would leave it with
    /// nothing. macOS excludes it from draw-through for the same reason.
    /// </remarks>
    private static bool GrabsExistingMarks(AnnotationTool tool, EditorModifiers modifiers) =>
        tool is not (AnnotationTool.Pencil or AnnotationTool.Marker or AnnotationTool.ColorSampler)
        && (tool == AnnotationTool.Select || !modifiers.HasFlag(EditorModifiers.DrawThrough));

    /// <summary>Keeps a selection from pointing at an annotation undo has removed.</summary>
    private void DropStaleSelection()
    {
        if (Selected is null)
        {
            return;
        }

        var id = Selected.Id;
        Selected = _document.Annotations.FirstOrDefault(annotation => annotation.Id == id);
    }

    /// <summary>
    /// Whether the tool draws by following the pointer rather than by dragging out a
    /// shape — which is the same question as whether <see cref="Smoothing"/> applies to
    /// it, so the toolbar asks here instead of keeping a list of its own.
    /// </summary>
    public static bool IsFreeform(AnnotationTool tool) => tool is AnnotationTool.Pencil;

    private static bool IsWorthKeeping(Annotation annotation)
    {
        // A single pencil dot is a deliberate mark, so freeform strokes are exempt
        // from the drag threshold.
        if (annotation.Points.Count > 0)
        {
            return true;
        }

        var deltaX = annotation.End.X - annotation.Start.X;
        var deltaY = annotation.End.Y - annotation.Start.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY) >= MinimumDragDistance;
    }

    /// <summary>
    /// A mark of the given width centred on <paramref name="centre"/>, for the one tool
    /// whose size comes from the row rather than from the drag.
    /// </summary>
    private Annotation Placed(CapturePoint centre, double width)
    {
        var half = width / 2;
        return Annotation.Create(
            _tool,
            new CapturePoint(centre.X - half, centre.Y - half),
            new CapturePoint(centre.X + half, centre.Y + half),
            Style);
    }

    /// <summary>
    /// <paramref name="point"/> brought back inside the region while a ruler is being
    /// drawn, and untouched for every other tool.
    /// </summary>
    /// <remarks>
    /// With Shift held the rule is at an angle the user asked for, so it is shortened
    /// along that angle instead of being clamped one axis at a time. Clamping x and y
    /// apart would bend the rule where it crosses the edge, and the reading would then be
    /// of a line nobody drew. macshot makes the same distinction
    /// (<c>MeasureToolHandler.swift:35-40</c>).
    /// </remarks>
    private CapturePoint RuledInside(CapturePoint point, EditorModifiers modifiers = EditorModifiers.None)
    {
        if (_tool != AnnotationTool.Measure || !ClampRulerToRegion || SnapRegion.IsEmpty)
        {
            return point;
        }

        return modifiers.HasFlag(EditorModifiers.Constrain)
            ? ShortenedInto(_origin, point, SnapRegion)
            : new CapturePoint(
                Math.Clamp(point.X, SnapRegion.X, SnapRegion.Right),
                Math.Clamp(point.Y, SnapRegion.Y, SnapRegion.Bottom));
    }

    /// <summary>
    /// Pulls <paramref name="end"/> back along the ray from <paramref name="start"/> until
    /// it is inside <paramref name="region"/>, keeping the direction it was dragged in.
    /// </summary>
    private static CapturePoint ShortenedInto(CapturePoint start, CapturePoint end, CaptureRegion region)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var reach = 1d;

        if (deltaX > 0)
        {
            reach = Math.Min(reach, (region.Right - start.X) / deltaX);
        }
        else if (deltaX < 0)
        {
            reach = Math.Min(reach, (region.X - start.X) / deltaX);
        }

        if (deltaY > 0)
        {
            reach = Math.Min(reach, (region.Bottom - start.Y) / deltaY);
        }
        else if (deltaY < 0)
        {
            reach = Math.Min(reach, (region.Y - start.Y) / deltaY);
        }

        reach = Math.Max(0, reach);
        return new CapturePoint(start.X + (deltaX * reach), start.Y + (deltaY * reach));
    }

    private static CapturePoint Constrain(
        AnnotationTool tool,
        CapturePoint origin,
        CapturePoint point,
        EditorModifiers modifiers)
    {
        if (!modifiers.HasFlag(EditorModifiers.Constrain))
        {
            return point;
        }

        // Constraining a line means an angle; constraining an area means a square. A
        // spotlight is an area — it is dragged out as the rectangle that stays lit, not
        // as a stroke.
        return tool is AnnotationTool.Line or AnnotationTool.Arrow or AnnotationTool.Marker
            or AnnotationTool.Measure
            ? SnapToAxis(origin, point)
            : SnapToSquare(origin, point);
    }

    private static CapturePoint SnapToAxis(CapturePoint origin, CapturePoint point)
    {
        var deltaX = point.X - origin.X;
        var deltaY = point.Y - origin.Y;
        var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (length == 0)
        {
            return point;
        }

        // Round the angle to the nearest 45 degrees but keep the drag length, which
        // is what makes a shift-drag feel like it snaps rather than jumps.
        var step = Math.PI / 4;
        var angle = Math.Round(Math.Atan2(deltaY, deltaX) / step) * step;
        return new CapturePoint(origin.X + length * Math.Cos(angle), origin.Y + length * Math.Sin(angle));
    }

    private static CapturePoint SnapToSquare(CapturePoint origin, CapturePoint point)
    {
        var deltaX = point.X - origin.X;
        var deltaY = point.Y - origin.Y;
        var size = Math.Max(Math.Abs(deltaX), Math.Abs(deltaY));
        return new CapturePoint(
            origin.X + Math.CopySign(size, deltaX),
            origin.Y + Math.CopySign(size, deltaY));
    }
}
