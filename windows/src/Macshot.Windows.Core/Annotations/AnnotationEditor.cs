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
    /// Ctrl: add to whatever the press landed on. On the selected line, arrow or ruler
    /// that is one more anchor to bend it through; on any other mark it is that mark
    /// joining the selection, or leaving it when it was already in; over empty space it
    /// is a lasso dragged over everything to be selected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One key with three readings because macOS spends <c>.control</c> on all three, and
    /// tells them apart by what is under the pointer rather than by a second modifier: the
    /// anchor first, and only when the selected mark is one that takes anchors and the
    /// press is on it (<c>OverlayView.swift:5491-5503</c>), then the selection
    /// (<c>:8239-8246</c>). <see cref="PointerPressed"/> asks in that order.
    /// </para>
    /// <para>
    /// Ctrl and not Shift, which every drawing tool already spends on constraining an
    /// angle: a modifier cannot mean "square this rectangle off" and "add this to the
    /// selection" in the same gesture.
    /// </para>
    /// <para>
    /// macOS reaches the anchor two ways, Control-click and right-click (<c>:5491</c> and
    /// <c>:6851</c>). Only the first is ported: over a capture the right button already
    /// opens the ring of colours where the pointer is, and taking that away would send
    /// every colour change back across the screen to the toolbar.
    /// </para>
    /// </remarks>
    Extend = 4,
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

    /// <summary>
    /// How far a marquee has to be dragged before it selects anything.
    /// </summary>
    /// <remarks>
    /// A Ctrl+click that missed everything sweeps a rectangle of nearly nothing, and
    /// letting that through would make it a way to lose the whole selection by accident.
    /// macshot's own two pixels (<c>OverlayView.swift:6427</c>).
    /// </remarks>
    private const double MinimumLasso = 2;

    private readonly AnnotationDocument _document;

    /// <summary>Every mark selected right now, in the order they joined the selection.</summary>
    private readonly List<Annotation> _selected = [];

    /// <summary>
    /// The originals of the marks travelling with <see cref="_dragTarget"/>, so each one's
    /// new position is measured from where it started rather than from where it now is.
    /// </summary>
    private readonly List<Annotation> _movingWith = [];

    /// <summary>Their in-flight copies, keyed by the annotation each stands in for.</summary>
    private readonly Dictionary<Guid, Annotation> _movedWith = [];

    private AnnotationTool _tool = AnnotationTool.Arrow;
    private CapturePoint _origin;
    private List<CapturePoint>? _freeformSamples;
    private List<double>? _freeformPressures;
    private Annotation? _dragTarget;

    // The whole handle rather than its kind, because an anchor grip is told apart from the
    // ones beside it only by its index — the kind alone would drag whichever came first.
    private AnnotationHandle? _handle;

    /// <summary>
    /// The mark a Ctrl+press found already selected, waiting to be taken out at the
    /// release.
    /// </summary>
    private Annotation? _pendingDeselect;

    private bool _lassoing;
    private CapturePoint _lassoFrom;
    private CapturePoint _lassoTo;
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
            // with the wrong tool's semantics. The selection is left standing: macshot's
            // handleToolbarAction changes the tool and nothing else
            // (OverlayView.swift:7887-7898), and clearing it here meant reaching for a
            // different tool silently disarmed Delete on the mark the user had picked.
            Cancel();
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

    /// <summary>Every mark selected right now — macshot's <c>selectedAnnotations</c>.</summary>
    public IReadOnlyList<Annotation> SelectedAnnotations => _selected;

    /// <summary>
    /// The one selected mark, or null when nothing is selected and when several are —
    /// macshot's <c>selectedAnnotation</c> (<c>OverlayView.swift:345-355</c>).
    /// </summary>
    /// <remarks>
    /// Null for a group rather than the first of it, because everything that reads this
    /// asks about a single subject: which handles to draw, which mark a handle drag
    /// reshapes, which mark the options row is editing. Answering with one member would
    /// hang that mark's handles off a selection the user is treating as a whole.
    /// </remarks>
    public Annotation? Selected => _selected.Count == 1 ? _selected[0] : null;

    /// <summary>
    /// The single selected annotation as it stands right now: its in-flight copy while a
    /// handle or a move is being dragged, so the chrome drawn around it tracks the drag
    /// instead of staying where the shape used to be.
    /// </summary>
    public Annotation? SelectionShown => Selected is { } only ? AsShown(only) : null;

    /// <summary>
    /// Every selected mark as it stands right now, which is what the canvas outlines.
    /// </summary>
    public IEnumerable<Annotation> SelectedAsShown => _selected.Select(AsShown);

    /// <summary>
    /// What the whole selection covers, or null unless several marks are selected: where
    /// the one delete button a group is given hangs from.
    /// </summary>
    /// <remarks>
    /// Only for several. A single selection has handles and, on this port as on macOS's
    /// own, no delete button of its own — the keyboard is that affordance. A group has no
    /// handles drawn at all, so without this there would be nothing on screen saying it
    /// can be removed (<c>OverlayView.swift:1850-1859</c>, <c>:4863</c>).
    /// </remarks>
    public CaptureRegion? MultiSelectionBounds
    {
        get
        {
            if (_selected.Count < 2)
            {
                return null;
            }

            CaptureRegion? bounds = null;
            foreach (var shown in SelectedAsShown)
            {
                bounds = bounds is { } far ? far.Union(shown.BoundingRect) : shown.BoundingRect;
            }

            return bounds;
        }
    }

    /// <summary>
    /// The marquee a Ctrl+drag is sweeping, or null when none is or when it has swept
    /// nothing worth drawing yet.
    /// </summary>
    public CaptureRegion? Lasso
    {
        get
        {
            if (!_lassoing)
            {
                return null;
            }

            var swept = CaptureRegion.FromPoints(_lassoFrom.X, _lassoFrom.Y, _lassoTo.X, _lassoTo.Y);
            return swept.IsEmpty ? null : swept;
        }
    }

    /// <summary>
    /// The handles the canvas should draw, for whatever tool is in hand.
    /// </summary>
    /// <remarks>
    /// Not only for the pointer tool. <see cref="PointerPressed"/> tries the selected
    /// mark's handles before anything else whatever tool is armed, so offering them only
    /// under the pointer left every other tool with handles that worked and could not be
    /// seen — a press near a corner reshaping a mark the user thought they were drawing
    /// beside. macOS draws them the same way, from the selection rather than from the tool
    /// (<c>OverlayView.swift:1852-1856</c>), and from a selection of one: a group is moved
    /// and deleted whole, so handles on each member would reshape one of them alone.
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
                yield return AsShown(annotation);
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
    /// <paramref name="annotation"/> as it looks at this instant: the in-flight copy while
    /// a drag has hold of it, and the committed one otherwise.
    /// </summary>
    /// <remarks>
    /// Both the marks and the chrome round them go through here, which is what keeps a
    /// selection outline on the shape rather than at the place the shape used to be. The
    /// mark the press took hold of carries the snap and lives in <see cref="Draft"/>;
    /// everything else moving with it is in <see cref="_movedWith"/>.
    /// </remarks>
    private Annotation AsShown(Annotation annotation)
    {
        if (_dragTarget is not null && _dragTarget.Id == annotation.Id && Draft is not null)
        {
            return Draft;
        }

        return _movedWith.TryGetValue(annotation.Id, out var moved) ? moved : annotation;
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

        // Ahead of everything else, including the handles: Ctrl on the selected curve is
        // one more anchor for it, and macOS asks that before anything the selection or the
        // tool would otherwise do with the press (OverlayView.swift:5491-5503). Adding an
        // anchor is a command rather than the start of a gesture — the mark is edited and
        // committed here, so the press is closed out and the release has nothing left to
        // do. Answered as a grab, which is what keeps a label or a badge from also being
        // placed where the press landed.
        if (modifiers.HasFlag(EditorModifiers.Extend) && AddAnchor(point))
        {
            _isPressed = false;
            return true;
        }

        // Before anything else, and for every tool but the sampler: the handles belong to
        // the mark that is selected rather than to the tool in hand, so a press on one has
        // to reshape it whatever is armed — including the two freehand tools, which never
        // take hold by clicking at all. macOS makes the same check in the same place, with
        // the same two exceptions (OverlayView.swift:8232-8237).
        if (_tool != AnnotationTool.ColorSampler
            && !DrawsThrough(_tool, modifiers)
            && GrabSelectedHandle(point))
        {
            return true;
        }

        if (GrabsExistingMarks(modifiers) && BeginSelection(point, modifiers))
        {
            return true;
        }

        // Anything that starts a new mark clears the selection: the chrome around a
        // mark that is no longer the subject of the gesture is a lie about what Delete
        // would remove.
        _selected.Clear();

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
        if (!_isPressed)
        {
            return;
        }

        // The one gesture with no draft: a marquee draws nothing and moves nothing, so it
        // has to be answered before the guard that assumes a mark is in flight.
        if (_lassoing)
        {
            _lassoTo = point;
            return;
        }

        if (Draft is null)
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

            var deltaX = point.X - _origin.X;
            var deltaY = point.Y - _origin.Y;
            var moved = _dragTarget.Translate(deltaX, deltaY);

            // A group does not line up with anything. Its members are already arranged
            // against each other, and nudging the whole of it to put one of them on a
            // guide would take every other one off the place the user had put it. macshot
            // snaps a single selection and moves a group raw (OverlayView.swift:6253-6267).
            Snap = _movingWith.Count == 0
                ? SnapFor(moved.BoundingRect, modifiers, _dragTarget.Id)
                : SnapResult.None;

            Draft = Snap == SnapResult.None ? moved : moved.Translate(Snap.Dx, Snap.Dy);

            foreach (var companion in _movingWith)
            {
                _movedWith[companion.Id] = companion.Translate(deltaX, deltaY);
            }

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

        if (_lassoing)
        {
            TakeLassoed();
            return null;
        }

        var draft = Draft;
        var dragTarget = _dragTarget;
        var pending = _pendingDeselect;
        var edits = draft is not null && dragTarget is not null ? Edits(draft, dragTarget) : [];

        Snap = SnapResult.None;
        Draft = null;
        _dragTarget = null;
        _handle = null;
        _freeformSamples = null;
        _freeformPressures = null;
        _pendingDeselect = null;
        _movingWith.Clear();
        _movedWith.Clear();

        // A Ctrl+press on a mark already selected takes it back out only if the press
        // turned out to be a click. The same press with a drag on it is how a group is
        // moved from one of its own members (OverlayView.swift:6436-6445).
        if (pending is not null && edits.Count == 0)
        {
            Deselect(pending);
        }

        if (draft is null)
        {
            return null;
        }

        if (dragTarget is not null)
        {
            if (edits.Count > 0)
            {
                // The whole drag is one undo step, however many marks it moved.
                // Committing every intermediate position would make Ctrl+Z replay the
                // mouse path, and one step per mark would make it replay the selection.
                _document.ReplaceRange(edits);
                foreach (var edit in edits)
                {
                    Reselect(edit);
                }
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
        _movingWith.Clear();
        _movedWith.Clear();
        _pendingDeselect = null;
        _lassoing = false;
        _isPressed = false;
        return true;
    }

    /// <summary>
    /// Removes every selected mark, as one undo step.
    /// </summary>
    /// <remarks>
    /// One step because one keystroke did it: taking six marks off with Delete and then
    /// having to press Ctrl+Z six times to get them back is not undoing what the user did.
    /// macshot removes them in one pass and clears the selection after
    /// (<c>OverlayView.swift:9055-9066</c>).
    /// </remarks>
    public bool DeleteSelected()
    {
        if (_selected.Count == 0)
        {
            return false;
        }

        var removed = _document.RemoveRange(_selected.Select(annotation => annotation.Id));
        _selected.Clear();
        return removed;
    }

    /// <summary>
    /// How long a freehand press has to be held still before it takes hold of what is
    /// under it instead of drawing — macshot's 0.3 s (<c>OverlayView.swift:8319</c>).
    /// </summary>
    /// <remarks>
    /// Here rather than in the two hosts that run the timer, so the overlay and the editor
    /// cannot drift apart on how long a hold is.
    /// </remarks>
    public static readonly TimeSpan HoldToSelect = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Whether a press with the tool in hand selects by being held still rather than by
    /// clicking, which is what the host arms its timer on.
    /// </summary>
    /// <remarks>
    /// Only the two tools whose press always draws, and only where no modifier has already
    /// given the press another meaning: with Ctrl the press selects outright, and with
    /// draw-through it is being told to ignore what is underneath entirely
    /// (<c>OverlayView.swift:8313</c>).
    /// </remarks>
    public bool SelectsByHolding(EditorModifiers modifiers = EditorModifiers.None) =>
        DrawsOnPress(_tool)
        && !modifiers.HasFlag(EditorModifiers.Extend)
        && !DrawsThrough(_tool, modifiers);

    /// <summary>
    /// Answers a press that has been held still: takes hold of the mark under it, and
    /// abandons the stroke that had started. False when there was nothing under it, which
    /// leaves the stroke to carry on.
    /// </summary>
    /// <remarks>
    /// The host owns the clock — a timer needs a dispatcher, and Core has none — but not
    /// the decision. What the hold selects, and the fact that the ink laid down so far is
    /// thrown away rather than committed, are the same rules a click goes through, and
    /// splitting them across the two hosts would be two copies of them.
    /// </remarks>
    /// <param name="point">
    /// Where the press landed, not where the pointer has drifted to inside the few pixels
    /// a hold allows: the drag that follows is measured from here, and measuring it from
    /// the drift would start the mark's travel with a jump.
    /// </param>
    public bool LongPressed(CapturePoint point, EditorModifiers modifiers = EditorModifiers.None)
    {
        if (!_isPressed || _document.HitTest(point) is not { IsMovable: true } hit)
        {
            return false;
        }

        // The stroke this press had started is abandoned rather than committed. The user
        // held still, which is the gesture for picking something up, and a dot left behind
        // every time would put an undo step between them and what they meant to do.
        Draft = null;
        _freeformSamples = null;
        _freeformPressures = null;
        _origin = point;

        Take(hit, modifiers);
        BeginMove(hit);
        return true;
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
    /// Bends the selected line, arrow or ruler through one more anchor, put where the
    /// press landed. False when the selected mark is not one that takes anchors, when the
    /// press missed it, or when the selection is not a single mark.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The selected mark and only that one, which is macOS's own condition
    /// (<c>OverlayView.swift:5491-5497</c>). It is also what keeps the one key from
    /// meaning two things at once: Ctrl on any other line has to be that line joining the
    /// selection, and a version of this that searched the whole document would swallow
    /// every such press before <see cref="BeginSelection"/> ever saw it.
    /// </para>
    /// <para>
    /// One undo step, because it is one edit to one mark: <see cref="AnnotationDocument.Replace"/>
    /// rather than <c>Amend</c>, so Ctrl+Z takes the anchor back off instead of leaving a
    /// bend the user cannot undo.
    /// </para>
    /// </remarks>
    public bool AddAnchor(CapturePoint point)
    {
        if (Anchorable(Selected, point) is not { } target)
        {
            return false;
        }

        var bent = target.WithAnchorAt(point);
        if (!_document.Replace(bent))
        {
            return false;
        }

        Reselect(bent);
        return true;
    }

    private static Annotation? Anchorable(Annotation? annotation, CapturePoint point) =>
        annotation is not null
            && Annotation.AcceptsWaypoints(annotation.Tool)
            && annotation.HitTest(point)
                ? annotation
                : null;

    /// <summary>
    /// Takes hold of a handle on the mark already selected. False when the press was not
    /// on one.
    /// </summary>
    /// <remarks>
    /// Tried before the marks themselves, so a handle can be grabbed even where a later
    /// mark covers it — otherwise reshaping the rectangle under a stamp would be
    /// impossible without moving the stamp first. Only a lone selection has handles, which
    /// is why this asks <see cref="Selected"/> rather than walking the whole selection.
    /// </remarks>
    private bool GrabSelectedHandle(CapturePoint point)
    {
        if (Selected is not { } selected || AnnotationHandles.At(selected, point, _scale) is not { } handle)
        {
            return false;
        }

        _handle = handle;
        _dragTarget = selected;
        Draft = selected;
        return true;
    }

    /// <summary>
    /// Takes hold of the mark under the pointer, or begins a marquee over the empty space
    /// the press landed on. False when the press is free to draw.
    /// </summary>
    private bool BeginSelection(CapturePoint point, EditorModifiers modifiers)
    {
        var hit = _document.HitTest(point);
        if (hit is null)
        {
            // Ctrl over empty space sweeps a selection rather than missing one
            // (OverlayView.swift:8301-8308). Nothing is drawn or moved by the drag that
            // follows, so it is the one gesture that leaves Draft empty.
            if (modifiers.HasFlag(EditorModifiers.Extend))
            {
                _lassoing = true;
                _lassoFrom = point;
                _lassoTo = point;
                return true;
            }

            // Only the pointer tool clears the selection on a miss. For every other
            // tool the press is about to draw, and clearing there would be doing it
            // twice with different rules.
            if (_tool == AnnotationTool.Select)
            {
                _selected.Clear();
            }

            return false;
        }

        Take(hit, modifiers);
        if (!hit.IsMovable)
        {
            // Selected, so it can be deleted or restyled, but there is nothing to drag.
            return true;
        }

        BeginMove(hit);
        return true;
    }

    /// <summary>
    /// Puts <paramref name="hit"/> into the selection the way the modifiers ask for.
    /// </summary>
    /// <remarks>
    /// A press on a mark that is already selected leaves the whole selection standing,
    /// which is what lets a group be dragged from any one of its members
    /// (<c>OverlayView.swift:8254-8268</c>).
    /// </remarks>
    private void Take(Annotation hit, EditorModifiers modifiers)
    {
        _pendingDeselect = null;

        if (modifiers.HasFlag(EditorModifiers.Extend))
        {
            if (IsSelected(hit))
            {
                // Not removed here. A Ctrl+press on one of a group is as likely to be the
                // start of dragging the group as it is a click undoing that member's
                // selection, and only the release says which.
                _pendingDeselect = hit;
            }
            else
            {
                _selected.Add(hit);
            }

            return;
        }

        if (!IsSelected(hit))
        {
            _selected.Clear();
            _selected.Add(hit);
        }
    }

    /// <summary>
    /// Starts a move on <paramref name="held"/>, enlisting the rest of the selection so a
    /// drag on any member takes the whole group with it.
    /// </summary>
    private void BeginMove(Annotation held)
    {
        _dragTarget = held;
        Draft = held;

        foreach (var annotation in _selected)
        {
            if (annotation.Id != held.Id && annotation.IsMovable)
            {
                _movingWith.Add(annotation);
            }
        }
    }

    /// <summary>
    /// Ends a marquee, selecting everything it swept.
    /// </summary>
    /// <remarks>
    /// A sweep that caught nothing leaves the selection alone rather than emptying it, as
    /// macshot's does (<c>OverlayView.swift:6424-6434</c>): the gesture that clears a
    /// selection is a plain click on empty space, and having the modified one clear it too
    /// would make a mis-aimed Ctrl+drag cost the user their group.
    /// </remarks>
    private void TakeLassoed()
    {
        var swept = CaptureRegion.FromPoints(_lassoFrom.X, _lassoFrom.Y, _lassoTo.X, _lassoTo.Y);
        _lassoing = false;

        if (swept.Width <= MinimumLasso || swept.Height <= MinimumLasso)
        {
            return;
        }

        var caught = _document.Annotations
            .Where(annotation => annotation.IsMovable && !annotation.BoundingRect.Intersect(swept).IsEmpty)
            .ToList();

        if (caught.Count == 0)
        {
            return;
        }

        _selected.Clear();
        _selected.AddRange(caught);
    }

    /// <summary>
    /// The marks this drag actually changed: the one it had hold of, and everything that
    /// travelled with it. Empty for a press that never moved, which is what tells a click
    /// on a mark from a drag of it.
    /// </summary>
    private List<Annotation> Edits(Annotation draft, Annotation dragTarget)
    {
        var edits = new List<Annotation>(_movingWith.Count + 1);

        if (AnnotationHandles.Differ(draft, dragTarget))
        {
            edits.Add(draft);
        }

        foreach (var companion in _movingWith)
        {
            if (_movedWith.TryGetValue(companion.Id, out var moved)
                && AnnotationHandles.Differ(moved, companion))
            {
                edits.Add(moved);
            }
        }

        return edits;
    }

    /// <summary>
    /// Whether this press draws over the marks under the pointer instead of taking hold of
    /// them. The pointer tool is exempt: interacting with marks is the whole of what it
    /// does, so a modifier that turned that off would leave it with nothing, and macOS
    /// exempts it for the same reason (<c>OverlayView.swift:8211-8215</c>).
    /// </summary>
    private static bool DrawsThrough(AnnotationTool tool, EditorModifiers modifiers) =>
        tool != AnnotationTool.Select && modifiers.HasFlag(EditorModifiers.DrawThrough);

    /// <summary>
    /// The two tools whose every press draws — macshot's <c>isPencilOrMarker</c>. A tap
    /// leaves a deliberate dot and a drag leaves a stroke, so there is no click left over
    /// to mean "take hold of this"; holding still does it instead
    /// (<see cref="SelectsByHolding"/>).
    /// </summary>
    private static bool DrawsOnPress(AnnotationTool tool) =>
        tool is AnnotationTool.Pencil or AnnotationTool.Marker;

    /// <summary>
    /// Whether a press with the tool in hand takes hold of what is already on the canvas.
    /// </summary>
    /// <remarks>
    /// False for the freehand tools, which are used to draw over marks often enough that
    /// grabbing would be wrong more often than right, and false for any tool drawing
    /// through. The two ways back in are macOS's (<c>OverlayView.swift:8247-8253</c>):
    /// Ctrl says the press is about the selection rather than about ink, and a group
    /// already selected can be dragged from any member without a modifier at all —
    /// otherwise moving one would mean putting the pencil down first.
    /// </remarks>
    private bool GrabsExistingMarks(EditorModifiers modifiers) =>
        _tool != AnnotationTool.ColorSampler
        && !DrawsThrough(_tool, modifiers)
        && (!DrawsOnPress(_tool)
            || modifiers.HasFlag(EditorModifiers.Extend)
            || _selected.Count > 1);

    private bool IsSelected(Annotation annotation) =>
        _selected.Exists(selected => selected.Id == annotation.Id);

    private void Deselect(Annotation annotation)
    {
        _selected.RemoveAll(selected => selected.Id == annotation.Id);
    }

    /// <summary>Swaps the selected copy of a mark a drag has just edited for the edit.</summary>
    private void Reselect(Annotation annotation)
    {
        var index = _selected.FindIndex(selected => selected.Id == annotation.Id);
        if (index >= 0)
        {
            _selected[index] = annotation;
        }
    }

    /// <summary>Keeps a selection from pointing at annotations undo has removed.</summary>
    private void DropStaleSelection()
    {
        if (_selected.Count == 0)
        {
            return;
        }

        var surviving = _selected
            .Select(selected => _document.Annotations.FirstOrDefault(existing => existing.Id == selected.Id))
            .OfType<Annotation>()
            .ToList();

        _selected.Clear();
        _selected.AddRange(surviving);
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
