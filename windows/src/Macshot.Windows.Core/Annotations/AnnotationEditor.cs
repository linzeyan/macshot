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
    private bool _isPressed;

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

    /// <summary>The annotation currently being drawn or dragged, for live preview.</summary>
    public Annotation? Draft { get; private set; }

    public Annotation? Selected { get; private set; }

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

    public void PointerPressed(CapturePoint point, EditorModifiers modifiers = EditorModifiers.None)
    {
        Cancel();
        _isPressed = true;
        _origin = point;

        if (_tool == AnnotationTool.Select)
        {
            BeginSelection(point);
            return;
        }

        if (IsFreeform(_tool))
        {
            _freeformSamples = [point];
            Draft = Annotation.CreateFreeform(_tool, _freeformSamples, Style);
            return;
        }

        Draft = Annotation.Create(_tool, point, point, Style);
    }

    public void PointerMoved(CapturePoint point, EditorModifiers modifiers = EditorModifiers.None)
    {
        if (!_isPressed || Draft is null)
        {
            return;
        }

        if (_dragTarget is not null)
        {
            Draft = _dragTarget.Translate(point.X - _origin.X, point.Y - _origin.Y);
            return;
        }

        if (_freeformSamples is not null)
        {
            _freeformSamples.Add(point);
            Draft = Annotation.CreateFreeform(_tool, _freeformSamples, Style);
            return;
        }

        Draft = Draft with { End = Constrain(_tool, _origin, point, modifiers) };
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
        Draft = null;
        _dragTarget = null;
        _freeformSamples = null;

        if (draft is null)
        {
            return;
        }

        if (dragTarget is not null)
        {
            // The whole drag is one undo step. Committing every intermediate
            // position would make Ctrl+Z replay the mouse path.
            if (draft.Start != dragTarget.Start || draft.End != dragTarget.End)
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

        _document.Add(draft);
    }

    /// <summary>Abandons an in-flight gesture. Returns whether there was one.</summary>
    public bool Cancel()
    {
        if (Draft is null && !_isPressed)
        {
            return false;
        }

        Draft = null;
        _dragTarget = null;
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

    private void BeginSelection(CapturePoint point)
    {
        var hit = _document.HitTest(point);
        Selected = hit;
        if (hit is null || !hit.IsMovable)
        {
            return;
        }

        _dragTarget = hit;
        Draft = hit;
    }

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

    private static bool IsFreeform(AnnotationTool tool) => tool is AnnotationTool.Pencil;

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

        // Constraining a line means an angle; constraining an area means a square.
        return tool is AnnotationTool.Line or AnnotationTool.Arrow or AnnotationTool.Marker
            or AnnotationTool.Highlight or AnnotationTool.Measure
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
