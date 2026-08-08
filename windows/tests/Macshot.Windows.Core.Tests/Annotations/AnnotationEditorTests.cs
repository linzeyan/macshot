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
    public void ProposeSpan_DrawsTheRulerWithoutPuttingItInTheDocument()
    {
        // The whole point of the auto-measure offer: it is visible, so the user can see
        // what they would be committing to, and it is not in the document, so letting go
        // of the key leaves nothing behind and costs no undo step.
        var editor = NewEditor(AnnotationTool.Measure);

        editor.ProposeSpan(new CapturePoint(30, 10), new CapturePoint(30, 90));

        Assert.AreEqual(0, editor.Document.Annotations.Count);
        CollectionAssert.Contains(editor.VisibleAnnotations.ToList(), editor.AutoSpan);

        Assert.IsTrue(editor.ClearSpan());
        Assert.IsNull(editor.AutoSpan);
        Assert.AreEqual(0, editor.VisibleAnnotations.Count());
        Assert.IsFalse(editor.ClearSpan(), "there was nothing left to take back");
    }

    [TestMethod]
    public void CommitSpan_TakesTheOfferOnceAndHandsBackWhatItAdded()
    {
        // Committed and cleared in one step. Left showing, the offer would be drawn on top
        // of the ruler it just became and would slide off it at the next mouse move.
        var editor = NewEditor(AnnotationTool.Measure);
        editor.ProposeSpan(new CapturePoint(30, 10), new CapturePoint(30, 90));

        var taken = editor.CommitSpan();

        Assert.IsNotNull(taken);
        Assert.AreEqual(AnnotationTool.Measure, taken.Tool);
        Assert.AreEqual(new CapturePoint(30, 90), taken.End);
        Assert.IsNull(editor.AutoSpan);
        CollectionAssert.AreEqual(new[] { taken }, editor.Document.Annotations.ToArray());
        Assert.IsNull(editor.CommitSpan(), "a second click must not commit the same ruler twice");
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
    public void ConstrainedSpotlight_BecomesSquare()
    {
        // It is dragged out as the region that stays lit, not as a stroke, so holding
        // the modifier means a square the way it does for a rectangle. It used to snap
        // to 45 degrees, which on an area tool moves one corner and leaves the shape a
        // rectangle of whatever proportions the angle happened to give.
        var editor = NewEditor(AnnotationTool.Highlight);

        Drag(editor, new CapturePoint(0, 0), new CapturePoint(100, 40), EditorModifiers.Constrain);

        var bounds = editor.Document.Annotations[0].BoundingRect;
        Assert.AreEqual(bounds.Width, bounds.Height, 1e-9);
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
    public void Handles_AreOfferedForWhateverToolIsInHand()
    {
        // A press near a handle reshapes the selected mark whatever tool is armed, so
        // offering the handles only under the pointer tool left every other tool with
        // chrome that worked and could not be seen.
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 40));

        // Grabbed with the drawing tool still in hand, which is how a mark comes to be
        // selected under anything but the pointer.
        Select(editor, new CapturePoint(10, 10));

        Assert.AreNotEqual(0, editor.Handles.Count);
    }

    [TestMethod]
    public void PressOnAMarkWithASpriteTool_GrabsItSoTheHostPlacesNothing()
    {
        // The answer is what the host places a label or a badge on: without it, a text,
        // a number and a stamp could never be picked up again, because the press that
        // would pick one up would drop another on top of it instead.
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 40));

        editor.Tool = AnnotationTool.Stamp;

        Assert.IsTrue(editor.PointerPressed(new CapturePoint(10, 10)));
        Assert.IsNotNull(editor.Selected);
    }

    [TestMethod]
    public void PressOnAShapeWithTheTextTool_GrabsIt()
    {
        // macOS selects whatever movable mark is under the pointer with the text tool in
        // hand (OverlayView.swift:8248-8252) and reserves Option for placing a label on
        // top of one, which is the escape this used to be missing.
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 40));

        editor.Tool = AnnotationTool.Text;

        Assert.IsTrue(editor.PointerPressed(new CapturePoint(10, 10)));

        editor.PointerReleased(new CapturePoint(10, 10));
        Assert.IsFalse(editor.PointerPressed(
            new CapturePoint(10, 10),
            EditorModifiers.DrawThrough));
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

    /// <summary>
    /// A spotlight is drawn with a dashed ring whatever the dash picker was left on, and
    /// takes its own border once that is changed.
    /// </summary>
    /// <remarks>
    /// The two are different choices spelled with the same enum. A spotlight that came out
    /// solid because the last arrow was drawn solid would read as a rectangle somebody drew
    /// on the capture rather than as the edge of a light — and the row would offer no way
    /// back to the look the tool is supposed to have.
    /// </remarks>
    [TestMethod]
    public void TheSpotlight_TakesItsOwnBorderRatherThanTheRowsDash()
    {
        var editor = NewEditor(AnnotationTool.Highlight);
        editor.Style = editor.Style with { LineStyle = LineStyle.Dotted };

        Drag(editor, new CapturePoint(10, 10), new CapturePoint(90, 60));

        Assert.AreEqual(LineStyle.Dashed, editor.Document.Annotations[0].Style.LineStyle);

        // Clear of the first one: a press inside a mark already there grabs it to be
        // moved, and this drag has to draw rather than drag.
        editor.SpotlightBorder = LineStyle.Solid;
        Drag(editor, new CapturePoint(200, 200), new CapturePoint(260, 250));

        Assert.AreEqual(LineStyle.Solid, editor.Document.Annotations[1].Style.LineStyle);

        // And nothing else was rerouted: a tool that does take the row's dash still gets it.
        editor.Tool = AnnotationTool.Line;
        Drag(editor, new CapturePoint(400, 400), new CapturePoint(460, 460));

        Assert.AreEqual(LineStyle.Dotted, editor.Document.Annotations[2].Style.LineStyle);
    }

    /// <summary>
    /// The loupe is placed at the width the row is set to, whatever the pointer does.
    /// </summary>
    /// <remarks>
    /// It was dragged out like a rectangle, which meant its width was set twice — once on
    /// the slider and again with the mouse, the mouse silently winning — and the slider was
    /// therefore a control that did nothing. macshot places it with a click at
    /// <c>loupeSize</c> and lets a drag decide only where it lands, which is the only
    /// arrangement in which the number on the row means anything.
    /// </remarks>
    [TestMethod]
    public void TheLoupe_IsPlacedAtTheWidthTheRowSetsRatherThanDraggedOut()
    {
        var editor = NewEditor(AnnotationTool.Loupe);
        editor.Style = editor.Style with { LoupeSize = 120 };

        editor.PointerPressed(new CapturePoint(200, 200));
        editor.PointerReleased(new CapturePoint(200, 200));

        var bounds = editor.Document.Annotations[0].BoundingRect;
        Assert.AreEqual(120, bounds.Width, 1e-9);
        Assert.AreEqual(120, bounds.Height, 1e-9);
        Assert.AreEqual(200, bounds.X + (bounds.Width / 2), 1e-9, "the click point is the centre");
        Assert.AreEqual(200, bounds.Y + (bounds.Height / 2), 1e-9);
    }

    /// <summary>
    /// Dragging one moves it. The gesture places the circle somewhere else rather than
    /// stretching it, so a hand that wanders on the way to letting go cannot resize it.
    /// </summary>
    [TestMethod]
    public void DraggingALoupe_MovesItWithoutChangingItsWidth()
    {
        var editor = NewEditor(AnnotationTool.Loupe);
        editor.Style = editor.Style with { LoupeSize = 80 };

        Drag(editor, new CapturePoint(100, 100), new CapturePoint(400, 300));

        var bounds = editor.Document.Annotations[0].BoundingRect;
        Assert.AreEqual(80, bounds.Width, 1e-9);
        Assert.AreEqual(80, bounds.Height, 1e-9);
        Assert.AreEqual(400, bounds.X + (bounds.Width / 2), 1e-9, "it follows the pointer");
        Assert.AreEqual(300, bounds.Y + (bounds.Height / 2), 1e-9);
    }

    /// <summary>
    /// A ruler dragged past the edge stops at it, so the number it writes is about
    /// something in the picture.
    /// </summary>
    /// <remarks>
    /// This is the whole point of the option. The pointer keeps going past the region —
    /// the overlay covers the display, not the selection — and a rule that followed it
    /// would report a span partly over pixels that get cropped out of the file. The
    /// reading is a claim about the capture, and a claim about pixels the capture does not
    /// contain is simply wrong.
    /// </remarks>
    [TestMethod]
    public void ARulerHeldToTheRegion_StopsAtTheEdgeRatherThanFollowingThePointer()
    {
        var editor = NewEditor(AnnotationTool.Measure);
        editor.SnapRegion = new CaptureRegion(0, 0, 200, 100);

        Drag(editor, new CapturePoint(10, 50), new CapturePoint(500, 50));

        Assert.AreEqual(200, editor.Document.Annotations[0].End.X, 1e-9);
        Assert.AreEqual(190, editor.Document.Annotations[0].Span, 1e-9);
    }

    /// <summary>
    /// Both ends, not only the one being dragged: a rule rooted outside the region measures
    /// from a place the capture does not show.
    /// </summary>
    [TestMethod]
    public void ARulerHeldToTheRegion_StartsInsideItEvenWhenThePressLandedOutside()
    {
        var editor = NewEditor(AnnotationTool.Measure);
        editor.SnapRegion = new CaptureRegion(0, 0, 200, 100);

        Drag(editor, new CapturePoint(-40, 50), new CapturePoint(100, 50));

        Assert.AreEqual(0, editor.Document.Annotations[0].Start.X, 1e-9);
    }

    /// <summary>
    /// Held at an angle, the rule is shortened along that angle rather than clamped one
    /// axis at a time.
    /// </summary>
    /// <remarks>
    /// Clamping x and y apart would bend the rule where it crosses the edge: the user asked
    /// for 45 degrees, and what they would get is a line at some other angle carrying a
    /// reading for a distance they never drew. Shortening keeps the angle and gives up the
    /// length, which is the half of the gesture that was about to leave the picture anyway.
    /// </remarks>
    [TestMethod]
    public void AConstrainedRulerHeldToTheRegion_KeepsItsAngleAndGivesUpItsLength()
    {
        var editor = NewEditor(AnnotationTool.Measure);
        editor.SnapRegion = new CaptureRegion(0, 0, 200, 100);

        Drag(editor, new CapturePoint(0, 0), new CapturePoint(400, 400), EditorModifiers.Constrain);

        var ruler = editor.Document.Annotations[0];
        Assert.AreEqual(100, ruler.End.X, 1e-9);
        Assert.AreEqual(100, ruler.End.Y, 1e-9, "the 45 degrees the modifier asked for must survive the clamp");
    }

    /// <summary>
    /// Switched off, the rule goes wherever it is dragged — and nothing else was ever
    /// held to the region, whatever the switch says.
    /// </summary>
    [TestMethod]
    public void TheRegionHoldsOnlyTheRuler_AndOnlyWhenItIsAskedTo()
    {
        var editor = NewEditor(AnnotationTool.Measure);
        editor.SnapRegion = new CaptureRegion(0, 0, 200, 100);
        editor.ClampRulerToRegion = false;

        Drag(editor, new CapturePoint(10, 50), new CapturePoint(500, 50));

        Assert.AreEqual(500, editor.Document.Annotations[0].End.X, 1e-9);

        // An arrow off the edge is simply cropped there, so holding it back would be
        // refusing a mark the user can plainly see the point of.
        editor.Tool = AnnotationTool.Arrow;
        editor.ClampRulerToRegion = true;
        Drag(editor, new CapturePoint(10, 90), new CapturePoint(500, 90));

        Assert.AreEqual(500, editor.Document.Annotations[1].End.X, 1e-9);
    }

    [TestMethod]
    public void PressOnAMark_GrabsItRatherThanStartingANewOne()
    {
        // The behaviour draw-through exists to escape, pinned here so that the escape
        // cannot be read as the normal case: a press inside a mark is a grab, which is
        // what lets any tool move what is already drawn without switching to the pointer.
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 60));

        editor.Tool = AnnotationTool.Censor;

        Assert.IsTrue(editor.PointerPressed(new CapturePoint(10, 35)));
        Assert.AreSame(editor.Document.Annotations[0], editor.Selected);
    }

    [TestMethod]
    public void DrawThrough_LetsACensorBeDrawnOverAMarkInsteadOfDraggingIt()
    {
        // A censor's whole job is to cover what is under it, macshot's own marks included.
        // Without this the gesture is swallowed by the shape it was aimed at: the shape
        // slides across the capture and no censor is drawn at all.
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 60));
        var shape = editor.Document.Annotations[0];

        editor.Tool = AnnotationTool.Censor;
        Drag(
            editor,
            new CapturePoint(10, 35),
            new CapturePoint(80, 90),
            EditorModifiers.DrawThrough);

        Assert.AreEqual(2, editor.Document.Annotations.Count);
        Assert.AreEqual(AnnotationTool.Censor, editor.Document.Annotations[1].Tool);

        // And the shape it was drawn over stayed where it was.
        Assert.AreEqual(shape.Start.X, editor.Document.Annotations[0].Start.X, 1e-9);
        Assert.AreEqual(shape.Start.Y, editor.Document.Annotations[0].Start.Y, 1e-9);
    }

    [TestMethod]
    public void DrawThrough_LeavesThePointerToolAbleToGrab()
    {
        // Interacting with marks is all the pointer tool does, so a modifier that turned
        // that off would leave it with nothing to do. macOS exempts it for the same reason.
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 60));

        editor.Tool = AnnotationTool.Select;
        editor.PointerPressed(new CapturePoint(10, 35), EditorModifiers.DrawThrough);

        Assert.AreSame(editor.Document.Annotations[0], editor.Selected);
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
