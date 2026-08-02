using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Annotations;

[Flags]
public enum EditorModifiers
{
    None = 0,

    /// <summary>Shift: snap lines to 45 degrees and force shapes square.</summary>
    Constrain = 1,
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

    private readonly AnnotationDocument _document;
    private AnnotationTool _tool = AnnotationTool.Arrow;
    private CapturePoint _origin;
    private List<CapturePoint>? _freeformSamples;
    private Annotation? _dragTarget;
    private AnnotationHandleKind? _handle;
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
    /// Where the mark in flight lined up, for the host to draw. Cleared the moment the
    /// gesture ends: a guide left on screen is a line the user has to work out the
    /// meaning of.
    /// </summary>
    public SnapResult Snap { get; private set; }

    /// <summary>The annotation currently being drawn or dragged, for live preview.</summary>
    public Annotation? Draft { get; private set; }

    public Annotation? Selected { get; private set; }

    /// <summary>
    /// The selected annotation as it stands right now: its in-flight copy while a handle
    /// or a move is being dragged, so the chrome drawn around it tracks the drag instead
    /// of staying where the shape used to be.
    /// </summary>
    public Annotation? SelectionShown => _dragTarget is not null && Draft is not null ? Draft : Selected;

    /// <summary>
    /// The handles the canvas should draw. Empty unless the select tool is active, because
    /// they are only grabbable then and chrome that cannot be used is chrome in the way.
    /// </summary>
    public IReadOnlyList<AnnotationHandle> Handles =>
        _tool == AnnotationTool.Select && SelectionShown is { } shown ? AnnotationHandles.For(shown, _scale) : [];

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
        }
    }

    /// <summary>
    /// Starts a gesture. Returns true when it took hold of a mark already drawn rather
    /// than starting a new one, which is what the caller needs to know before placing a
    /// label or a badge where the press landed.
    /// </summary>
    /// <remarks>
    /// A click on an existing mark grabs it whatever tool is in hand, so there is no
    /// pointer tool to switch to first — the same rule macOS uses. The exceptions are
    /// the tools where grabbing would take the gesture away from what it is for: a
    /// freehand stroke drawn over an earlier mark is a stroke, not a grab, and the
    /// text tool only grabs text, because a label placed beside a rectangle is far more
    /// common than a wish to move the rectangle.
    /// </remarks>
    public bool PointerPressed(CapturePoint point, EditorModifiers modifiers = EditorModifiers.None)
    {
        Cancel();
        _isPressed = true;
        _origin = point;

        if (GrabsExistingMarks(_tool) && BeginSelection(point))
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
            Draft = Annotation.CreateFreeform(_tool, _freeformSamples, Style);
            return false;
        }

        Draft = Annotation.Create(_tool, point, point, Style);
        return false;
    }

    public void PointerMoved(CapturePoint point, EditorModifiers modifiers = EditorModifiers.None)
    {
        if (!_isPressed || Draft is null)
        {
            return;
        }

        if (_dragTarget is not null)
        {
            if (_handle is { } handle)
            {
                Draft = AnnotationHandles.Drag(_dragTarget, handle, point, modifiers);
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
            Draft = Annotation.CreateFreeform(_tool, _freeformSamples, Style);
            return;
        }

        // Only the corner under the pointer, because the other one is where the press
        // landed and pulling that about would move ink the user already placed.
        var end = Constrain(_tool, _origin, point, modifiers);
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

    /// <summary>Ends the gesture, committing it to the document when it produced something.</summary>
    public void PointerReleased(CapturePoint point, EditorModifiers modifiers = EditorModifiers.None)
    {
        if (!_isPressed)
        {
            return;
        }

        PointerMoved(point, modifiers);
        _isPressed = false;

        var draft = Draft;
        var dragTarget = _dragTarget;
        Snap = SnapResult.None;
        Draft = null;
        _dragTarget = null;
        _handle = null;
        _freeformSamples = null;

        if (draft is null)
        {
            return;
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

            return;
        }

        if (!IsWorthKeeping(draft))
        {
            return;
        }

        _document.Add(Finished(draft));
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

            // Start and End follow the points, since a freeform mark's ends are its
            // first and last samples and everything from hit testing to the bounding
            // rectangle reads them.
            Start = smoothed[0],
            End = smoothed[^1],
        };
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
            _handle = handle.Kind;
            _dragTarget = selected;
            Draft = selected;
            return true;
        }

        var hit = _document.HitTest(point);
        if (hit is null || !Grabs(_tool, hit))
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
    /// would be wrong more often than right.
    /// </summary>
    private static bool GrabsExistingMarks(AnnotationTool tool) =>
        tool is not (AnnotationTool.Pencil or AnnotationTool.Marker or AnnotationTool.ColorSampler);

    /// <summary>
    /// Whether this tool grabs that particular mark. The text tool only grabs text: a
    /// label is usually placed beside a shape, not instead of moving it.
    /// </summary>
    private static bool Grabs(AnnotationTool tool, Annotation hit) =>
        tool != AnnotationTool.Text || hit.Tool == AnnotationTool.Text;

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
