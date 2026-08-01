using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Annotations;

[TestClass]
public sealed class AnnotationEditorTests
{
    [TestMethod]
    public void Drag_CommitsOneAnnotation()
    {
        var editor = NewEditor(AnnotationTool.Arrow);

        Drag(editor, new CapturePoint(10, 10), new CapturePoint(90, 60));

        Assert.AreEqual(1, editor.Document.Annotations.Count);
        Assert.AreEqual(new CapturePoint(90, 60), editor.Document.Annotations[0].End);
        Assert.IsNull(editor.Draft, "the draft must be cleared once it is committed");
    }

    [TestMethod]
    public void Click_DoesNotLeaveAZeroSizeAnnotationBehind()
    {
        // A stray click would otherwise add an invisible annotation that still
        // consumes an undo step and still answers hit tests.
        var editor = NewEditor(AnnotationTool.Rectangle);

        Drag(editor, new CapturePoint(40, 40), new CapturePoint(41, 40));

        Assert.AreEqual(0, editor.Document.Annotations.Count);
        Assert.IsFalse(editor.Document.CanUndo);
    }

    [TestMethod]
    public void Click_KeepsAPencilDotBecauseItIsADeliberateMark()
    {
        var editor = NewEditor(AnnotationTool.Pencil);

        Drag(editor, new CapturePoint(40, 40), new CapturePoint(40, 40));

        Assert.AreEqual(1, editor.Document.Annotations.Count);
    }

    [TestMethod]
    public void ConstrainedLine_SnapsToTheNearest45DegreesAndKeepsTheDragLength()
    {
        var editor = NewEditor(AnnotationTool.Line);

        Drag(editor, new CapturePoint(0, 0), new CapturePoint(100, 10), EditorModifiers.Constrain);

        var end = editor.Document.Annotations[0].End;
        Assert.AreEqual(0, end.Y, 1e-9, "a shallow drag must flatten onto the horizontal");
        Assert.AreEqual(Math.Sqrt(100 * 100 + 10 * 10), end.X, 1e-9, "snapping must not change the length");
    }

    [TestMethod]
    public void ConstrainedRectangle_BecomesSquare()
    {
        var editor = NewEditor(AnnotationTool.Rectangle);

        Drag(editor, new CapturePoint(0, 0), new CapturePoint(100, 40), EditorModifiers.Constrain);

        var bounds = editor.Document.Annotations[0].BoundingRect;
        Assert.AreEqual(bounds.Width, bounds.Height, 1e-9);
        Assert.AreEqual(100, bounds.Width, 1e-9, "the longer axis wins so the shape keeps up with the pointer");
    }

    [TestMethod]
    public void ConstrainedRectangle_KeepsTheDragDirection()
    {
        // Dragging up and to the left must stay up and to the left, not flip across
        // the origin into the opposite quadrant.
        var editor = NewEditor(AnnotationTool.Rectangle);

        Drag(editor, new CapturePoint(100, 100), new CapturePoint(20, 60), EditorModifiers.Constrain);

        Assert.AreEqual(new CapturePoint(20, 20), editor.Document.Annotations[0].End);
    }

    [TestMethod]
    public void SelectDrag_IsASingleUndoStep()
    {
        // Committing every intermediate position would make Ctrl+Z replay the mouse
        // path one move at a time.
        var editor = NewEditor(AnnotationTool.Censor);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 60));
        var original = editor.Document.Annotations[0];

        editor.Tool = AnnotationTool.Select;
        editor.PointerPressed(new CapturePoint(30, 30));
        editor.PointerMoved(new CapturePoint(35, 33));
        editor.PointerMoved(new CapturePoint(45, 40));
        editor.PointerReleased(new CapturePoint(50, 40));

        Assert.AreEqual(new CapturePoint(30, 20), editor.Document.Annotations[0].Start);

        Assert.IsTrue(editor.Undo());
        Assert.AreEqual(original.Start, editor.Document.Annotations[0].Start, "one undo must restore the whole move");
    }

    [TestMethod]
    public void SelectClickWithoutMoving_DoesNotConsumeAnUndoStep()
    {
        var editor = NewEditor(AnnotationTool.Censor);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 60));

        editor.Tool = AnnotationTool.Select;
        editor.PointerPressed(new CapturePoint(30, 30));
        editor.PointerReleased(new CapturePoint(30, 30));

        editor.Undo();
        Assert.AreEqual(0, editor.Document.Annotations.Count, "the only undo step should be the annotation itself");
    }

    [TestMethod]
    public void VisibleAnnotations_ShowsTheDraggedCopyInsteadOfBothPositions()
    {
        var editor = NewEditor(AnnotationTool.Censor);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 60));

        editor.Tool = AnnotationTool.Select;
        editor.PointerPressed(new CapturePoint(30, 30));
        editor.PointerMoved(new CapturePoint(50, 30));

        var visible = editor.VisibleAnnotations.ToArray();
        Assert.AreEqual(1, visible.Length, "the annotation must not be drawn at both its old and new position");
        Assert.AreEqual(new CapturePoint(30, 10), visible[0].Start);
    }

    [TestMethod]
    public void VisibleAnnotations_IncludesTheAnnotationBeingDrawn()
    {
        var editor = NewEditor(AnnotationTool.Ellipse);
        editor.PointerPressed(new CapturePoint(10, 10));
        editor.PointerMoved(new CapturePoint(80, 40));

        Assert.AreEqual(1, editor.VisibleAnnotations.Count());
        Assert.AreEqual(0, editor.Document.Annotations.Count, "an in-flight draft must not be in the document yet");
    }

    [TestMethod]
    public void Cancel_DiscardsTheDraftWithoutTouchingTheDocument()
    {
        var editor = NewEditor(AnnotationTool.Rectangle);
        editor.PointerPressed(new CapturePoint(10, 10));
        editor.PointerMoved(new CapturePoint(80, 40));

        Assert.IsTrue(editor.Cancel());

        editor.PointerReleased(new CapturePoint(80, 40));
        Assert.AreEqual(0, editor.Document.Annotations.Count);
        Assert.IsFalse(editor.Document.CanUndo);
    }

    [TestMethod]
    public void ChangingTool_AbandonsAnythingInFlight()
    {
        // Finishing a rectangle with the ellipse tool's semantics would be worse
        // than dropping the gesture.
        var editor = NewEditor(AnnotationTool.Rectangle);
        editor.PointerPressed(new CapturePoint(10, 10));
        editor.PointerMoved(new CapturePoint(80, 40));

        editor.Tool = AnnotationTool.Ellipse;
        editor.PointerReleased(new CapturePoint(80, 40));

        Assert.AreEqual(0, editor.Document.Annotations.Count);
        Assert.IsNull(editor.Draft);
    }

    [TestMethod]
    public void DeleteSelected_RemovesTheAnnotationAndClearsTheSelection()
    {
        var editor = NewEditor(AnnotationTool.Censor);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 60));

        editor.Tool = AnnotationTool.Select;
        editor.PointerPressed(new CapturePoint(30, 30));
        editor.PointerReleased(new CapturePoint(30, 30));

        Assert.IsTrue(editor.DeleteSelected());
        Assert.AreEqual(0, editor.Document.Annotations.Count);
        Assert.IsNull(editor.Selected);
        Assert.IsFalse(editor.DeleteSelected());
    }

    [TestMethod]
    public void Undo_DropsASelectionThatNoLongerExists()
    {
        // A selection surviving undo would let the next delete or drag act on an
        // annotation the document no longer contains.
        var editor = NewEditor(AnnotationTool.Censor);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 60));

        editor.Tool = AnnotationTool.Select;
        editor.PointerPressed(new CapturePoint(30, 30));
        editor.PointerReleased(new CapturePoint(30, 30));
        Assert.IsNotNull(editor.Selected);

        editor.Undo();

        Assert.IsNull(editor.Selected);
    }

    [TestMethod]
    public void SelectClickOnEmptySpace_ClearsTheSelection()
    {
        var editor = NewEditor(AnnotationTool.Censor);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 60));

        editor.Tool = AnnotationTool.Select;
        editor.PointerPressed(new CapturePoint(30, 30));
        editor.PointerReleased(new CapturePoint(30, 30));
        editor.PointerPressed(new CapturePoint(500, 500));
        editor.PointerReleased(new CapturePoint(500, 500));

        Assert.IsNull(editor.Selected);
    }

    [TestMethod]
    public void Handles_AreOfferedOnlyWhileTheSelectToolIsActive()
    {
        // They are chrome the user cannot grab with a drawing tool armed, and chrome that
        // cannot be used is chrome in the way of the mark being drawn.
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 40));

        editor.Tool = AnnotationTool.Select;
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(10, 10));
        Assert.AreNotEqual(0, editor.Handles.Count);

        editor.Tool = AnnotationTool.Arrow;

        Assert.AreEqual(0, editor.Handles.Count);
    }

    [TestMethod]
    public void GrabbingAHandle_ReshapesTheMarkInsteadOfMovingIt()
    {
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 40));

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(10, 10));
        Drag(editor, new CapturePoint(60, 40), new CapturePoint(100, 80));

        var bounds = editor.Document.Annotations[0].BoundingRect;
        Assert.AreEqual(10, bounds.X, 1e-9, "the anchored corner must not move");
        Assert.AreEqual(100, bounds.Right, 1e-9);
        Assert.AreEqual(80, bounds.Bottom, 1e-9);
    }

    [TestMethod]
    public void GrabbingAHandle_KeepsTheWholeReshapeToOneUndoStep()
    {
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 40));

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(10, 10));
        editor.PointerPressed(new CapturePoint(60, 40));
        editor.PointerMoved(new CapturePoint(70, 50));
        editor.PointerMoved(new CapturePoint(80, 60));
        editor.PointerReleased(new CapturePoint(80, 60));

        editor.Undo();

        Assert.AreEqual(60, editor.Document.Annotations[0].BoundingRect.Right, 1e-9);
    }

    [TestMethod]
    public void RotatingWithAHandle_IsCommittedEvenThoughNoPointMoved()
    {
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(0, 0), new CapturePoint(40, 40));

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(0, 0));
        var rotate = editor.Handles.Single(handle => handle.Kind == AnnotationHandleKind.Rotate).Position;
        Drag(editor, rotate, new CapturePoint(100, 20));

        Assert.AreEqual(Math.PI / 2, editor.Document.Annotations[0].Rotation, 1e-9);
    }

    [TestMethod]
    public void AHandleUnderALaterMark_IsStillGrabbable()
    {
        // Otherwise reshaping the rectangle beneath a stamp would mean moving the stamp
        // out of the way first, and putting it back afterwards.
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 40));
        editor.Tool = AnnotationTool.Censor;
        Drag(editor, new CapturePoint(50, 30), new CapturePoint(90, 70));

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(10, 10));
        Drag(editor, new CapturePoint(60, 40), new CapturePoint(60, 60));

        Assert.AreEqual(60, editor.Document.Annotations[0].BoundingRect.Bottom, 1e-9);
        Assert.AreEqual(
            AnnotationTool.Rectangle,
            editor.Selected?.Tool,
            "the press must not have selected the mark drawn over the handle");
    }

    [TestMethod]
    public void SelectionShown_TracksTheDragRatherThanWhereTheMarkWas()
    {
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 40));

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(10, 10));
        editor.PointerPressed(new CapturePoint(60, 40));
        editor.PointerMoved(new CapturePoint(90, 70));

        Assert.AreEqual(90, editor.SelectionShown?.BoundingRect.Right ?? 0, 1e-9);
    }

    /// <summary>Clicks a mark with the select tool, which is what arms its handles.</summary>
    private static void Select(AnnotationEditor editor, CapturePoint point)
    {
        editor.PointerPressed(point);
        editor.PointerReleased(point);
    }

    private static AnnotationEditor NewEditor(AnnotationTool tool)
    {
        return new AnnotationEditor(new AnnotationDocument()) { Tool = tool };
    }

    private static void Drag(
        AnnotationEditor editor,
        CapturePoint from,
        CapturePoint to,
        EditorModifiers modifiers = EditorModifiers.None)
    {
        editor.PointerPressed(from, modifiers);
        editor.PointerMoved(to, modifiers);
        editor.PointerReleased(to, modifiers);
    }
}
