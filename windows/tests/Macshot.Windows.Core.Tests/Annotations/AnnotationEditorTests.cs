using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Recognition;

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

    /// <summary>
    /// The report this pins: with the rectangle tool in hand, a rectangle already drawn
    /// could be dragged by its edge, but the pointer stayed a crosshair over it, so nothing
    /// said where it could be picked up. macOS shows the hand over any mark for every tool
    /// that grabs one (OverlayView.swift:1388-1400).
    /// </summary>
    [TestMethod]
    public void CursorAt_ShowsTheGrabOverAMarkWhicheverDrawingToolIsInHand()
    {
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(90, 60));
        editor.PointerPressed(new CapturePoint(300, 300));
        editor.PointerReleased(new CapturePoint(300, 300));

        Assert.AreEqual(PointerCursor.Grab, editor.CursorAt(new CapturePoint(10, 30)), "on the edge");
        Assert.AreEqual(PointerCursor.Crosshair, editor.CursorAt(new CapturePoint(50, 35)), "inside a hollow one");
        Assert.AreEqual(PointerCursor.Crosshair, editor.CursorAt(new CapturePoint(200, 200)), "over nothing");
    }

    /// <summary>
    /// The cursor has to promise what the press does. A pencil draws over a mark rather
    /// than taking it, and Alt draws through whatever is underneath, so a grab shown there
    /// would be a promise the click breaks.
    /// </summary>
    [TestMethod]
    public void CursorAt_KeepsTheToolsCursorWhereThePressWouldDrawInstead()
    {
        var editor = NewEditor(AnnotationTool.Arrow);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(90, 10));
        var onIt = new CapturePoint(50, 10);

        Assert.AreEqual(PointerCursor.Crosshair, editor.CursorAt(onIt, EditorModifiers.DrawThrough));

        editor.Tool = AnnotationTool.Pencil;
        Assert.AreEqual(PointerCursor.Brush, editor.CursorAt(onIt));
        Assert.AreEqual(PointerCursor.Grab, editor.CursorAt(onIt, EditorModifiers.Extend), "Ctrl grabs even with a pencil");

        editor.Tool = AnnotationTool.Select;
        Assert.AreEqual(PointerCursor.Grab, editor.CursorAt(onIt));
        Assert.AreEqual(PointerCursor.Arrow, editor.CursorAt(new CapturePoint(50, 80)));
    }

    /// <summary>
    /// A held mark keeps the grab wherever the pointer goes, the way macOS's hand stays
    /// closed until the release: a cursor that changed as the mark lagged a frame behind
    /// the pointer would read as the grip slipping. A resize keeps its arrow instead,
    /// because Windows draws the grab as the move arrow.
    /// </summary>
    [TestMethod]
    public void CursorAt_StaysOnWhatThePressTookHoldOfUntilTheRelease()
    {
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(90, 60));

        editor.PointerPressed(new CapturePoint(10, 30));
        editor.PointerMoved(new CapturePoint(400, 400));
        Assert.AreEqual(PointerCursor.Grab, editor.CursorAt(new CapturePoint(400, 400)));
        editor.PointerReleased(new CapturePoint(400, 400));

        var corner = editor.Selected!.BoundingRect;
        var bottomRight = new CapturePoint(corner.Right, corner.Bottom);
        Assert.AreEqual(PointerCursor.ResizeFalling, editor.CursorAt(bottomRight));
        editor.PointerPressed(bottomRight);
        editor.PointerMoved(new CapturePoint(900, 900));
        Assert.AreEqual(PointerCursor.ResizeFalling, editor.CursorAt(new CapturePoint(900, 900)));

        editor.Tool = AnnotationTool.Arrow;
        Drag(editor, new CapturePoint(600, 100), new CapturePoint(700, 100));
        editor.PointerPressed(new CapturePoint(800, 300));
        Assert.AreEqual(PointerCursor.Crosshair, editor.CursorAt(new CapturePoint(650, 100)), "drawing a new mark is not holding one");
    }

    /// <summary>
    /// A side moves along one axis only, and the pointer has to say which before the
    /// press: the grab there would promise a move, and a diagonal would promise the
    /// other axis too.
    /// </summary>
    [TestMethod]
    public void CursorAt_ShowsWhichWayTheSideOfASelectedShapeResizes()
    {
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(90, 60));
        editor.PointerPressed(new CapturePoint(10, 30));
        editor.PointerReleased(new CapturePoint(10, 30));

        Assert.AreEqual(PointerCursor.ResizeVertical, editor.CursorAt(new CapturePoint(50, 60)));
        Assert.AreEqual(PointerCursor.ResizeHorizontal, editor.CursorAt(new CapturePoint(90, 35)));
    }

    /// <summary>
    /// The freehand tools show the stroke a press would lay down under the pointer, so
    /// the width can be judged before any ink is spent. The dot has to be the stroke's
    /// size, and has to stay big enough to see at the thinnest setting.
    /// </summary>
    [TestMethod]
    public void BrushRadius_IsHalfTheStrokeTheToolWouldDrawButNeverTooSmallToSee()
    {
        var editor = NewEditor(AnnotationTool.Marker);
        editor.Style = editor.Style with { StrokeWidth = 1, MarkerStrokeWidth = 3 };

        Assert.AreEqual(PointerCursor.Brush, editor.CursorAt(new CapturePoint(50, 50)));
        Assert.AreEqual(9, editor.BrushRadius, 1e-9, "the marker's own width as drawn, six times over");

        editor.Tool = AnnotationTool.Pencil;
        Assert.AreEqual(2, editor.BrushRadius, 1e-9, "a one-pixel pencil still gets a visible dot");

        editor.Scale = 2;
        Assert.AreEqual(4, editor.BrushRadius, 1e-9, "two points, not two pixels");
    }

    /// <summary>
    /// macshot's row is in points: a 3 is as thick on a Retina screen as on any other,
    /// because a mark is multiplied out to pixels only where it is drawn. Taken as frame
    /// pixels, every stroke, corner and loupe came out smaller the higher the display's
    /// scaling — at 175%, little more than half of what the same number gives on the Mac.
    /// </summary>
    [TestMethod]
    public void PointerReleased_SizesAMarkInPointsOfTheSurfaceItIsDrawnOn()
    {
        var editor = NewEditor(AnnotationTool.Rectangle);
        editor.Scale = 1.75;
        editor.Style = editor.Style with { StrokeWidth = 4, CornerRadius = 8, LoupeSize = 120 };

        var rectangle = DragOut(editor, new CapturePoint(10, 10), new CapturePoint(100, 80));
        editor.Tool = AnnotationTool.Loupe;
        var loupe = DragOut(editor, new CapturePoint(300, 300), new CapturePoint(300, 300));

        Assert.AreEqual(7, rectangle?.Style.StrokeWidth ?? 0, 1e-9);
        Assert.AreEqual(14, rectangle?.Style.CornerRadius ?? 0, 1e-9);
        Assert.AreEqual(210, loupe?.BoundingRect.Width ?? 0, 1e-9);
        Assert.AreEqual(1.75, rectangle?.Style.PixelsPerPoint ?? 0, 1e-9, "so its fixed sizes can be drawn in points too");
        Assert.AreEqual(4, editor.Style.StrokeWidth, "while the row goes on showing the 4 it was set to");
    }

    /// <summary>
    /// A smart highlighter is laying a band over a line of text, which is always level:
    /// macshot holds it to the height it was pressed at (MarkerToolHandler.swift:70-73).
    /// Left free, the hand's sag would be drawn for the whole drag and only straightened
    /// when let go, which reads as the tool fighting the hand.
    /// </summary>
    [TestMethod]
    public void PointerMoved_SmartMarkerRunsLevelFromWhereItWasPressed()
    {
        var editor = NewEditor(AnnotationTool.Marker);
        editor.SmartMarker = true;

        editor.PointerPressed(new CapturePoint(10, 50));
        editor.PointerMoved(new CapturePoint(60, 70));
        editor.PointerMoved(new CapturePoint(100, 35));
        var stroke = editor.PointerReleased(new CapturePoint(100, 35));

        Assert.IsNotNull(stroke);
        Assert.IsTrue(stroke.Points.All(point => point.Y == 50), "the band left the height it was pressed at");
        Assert.AreEqual(100, stroke.Points[^1].X, "and still went as far as the hand did");
    }

    /// <summary>
    /// The band is as tall as the text it is about to cover from the moment it is pressed,
    /// as macshot sizes it (MarkerToolHandler.swift:48-53) — not the row's width for the
    /// drag and the line's only once the snap lands, which would show the user a band that
    /// fits nothing until they let go. In points of the surface, like every other size.
    /// </summary>
    [TestMethod]
    public void PointerPressed_SmartMarkerIsAsThickAsTheLineUnderThePress()
    {
        var editor = NewEditor(AnnotationTool.Marker);
        editor.Scale = 2;
        editor.SmartMarker = true;
        editor.RecognizedText =
        [
            new RecognizedLine([new RecognizedWord("above", new CaptureRegion(0, 0, 200, 16))]),
            new RecognizedLine([new RecognizedWord("pressed", new CaptureRegion(0, 40, 200, 20))]),
        ];

        editor.PointerPressed(new CapturePoint(20, 52));

        Assert.AreEqual(28, editor.Draft?.InkWidth ?? 0, 1e-9, "the line's 20 and 4 points of padding at 2x");
    }

    /// <summary>
    /// macshot's highlighter is a freehand tool that keeps its samples as drawn: no
    /// smoothing, which macshot offers only on the pencil, and no pressure, which it never
    /// records for the marker. Drawn as a straight line between press and release, it
    /// could not follow a line of text that is not perfectly level, nor circle anything.
    /// </summary>
    [TestMethod]
    public void PointerReleased_MarkerKeepsTheSamplesTheHandMade()
    {
        var editor = NewEditor(AnnotationTool.Marker);
        editor.Smoothing = PencilSmoothing.Smooth;
        editor.PenPressure = true;

        editor.PointerPressed(new CapturePoint(10, 10), pressure: 0.5);
        editor.PointerMoved(new CapturePoint(50, 40), pressure: 0.5);
        var marker = editor.PointerReleased(new CapturePoint(90, 10), pressure: 0.5);

        Assert.IsNotNull(marker);
        CollectionAssert.Contains(marker.Points.ToArray(), new CapturePoint(50, 40), "the turn, exactly where it was made");
        Assert.AreEqual(0, marker.Pressures.Count);
    }

    /// <summary>
    /// Shift holds a freehand stroke to the axis it sets off along from where Shift went
    /// down — macshot's <c>freeformShiftDirection</c>, on the pencil and the highlighter
    /// alike. Without it there is no way to rule a highlight along a line of text by hand.
    /// </summary>
    [TestMethod]
    [DataRow(AnnotationTool.Pencil)]
    [DataRow(AnnotationTool.Marker)]
    public void PointerMoved_ShiftHoldsAFreehandStrokeToTheAxisItSetsOffAlong(AnnotationTool tool)
    {
        var editor = NewEditor(tool);
        editor.Smoothing = PencilSmoothing.None;

        editor.PointerPressed(new CapturePoint(10, 10));
        editor.PointerMoved(new CapturePoint(30, 12));
        editor.PointerMoved(new CapturePoint(31, 13), EditorModifiers.Constrain);
        editor.PointerMoved(new CapturePoint(60, 20), EditorModifiers.Constrain);
        editor.PointerMoved(new CapturePoint(80, 40), EditorModifiers.Constrain);
        editor.PointerMoved(new CapturePoint(85, 70));
        var stroke = editor.PointerReleased(new CapturePoint(85, 70));

        Assert.IsNotNull(stroke);
        CollectionAssert.AreEqual(
            new[]
            {
                new CapturePoint(10, 10),
                new CapturePoint(30, 12),
                new CapturePoint(30, 12),
                new CapturePoint(60, 12),
                new CapturePoint(80, 12),
                new CapturePoint(85, 70),
                new CapturePoint(85, 70),
            },
            stroke.Points.ToArray(),
            "held still until the direction is plain, then level with where Shift went down, then free again");
    }

    /// <summary>
    /// macOS's eight-point reach is in points. Measured in frame pixels it shrank with the
    /// display's scaling, so at 175% a line had to be hit almost exactly.
    /// </summary>
    [TestMethod]
    public void MarkAt_ReachesAsFarOnAHighDpiDisplayAsAHandDoesAt100Percent()
    {
        var editor = NewEditor(AnnotationTool.Line);
        Drag(editor, new CapturePoint(0, 100), new CapturePoint(400, 100));
        var nearby = new CapturePoint(200, 112);

        Assert.IsNull(editor.MarkAt(nearby), "12 pixels off is out of reach at 100%");

        editor.Scale = 2;
        Assert.IsNotNull(editor.MarkAt(nearby), "and within it at 200%, where it is six points");
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

    [TestMethod]
    public void CtrlPressOnTheSelectedMark_BendsItRatherThanStartingAGesture()
    {
        // The modifier is a command, not a drag. Left to start a gesture as well, the same
        // press would draw a second mark on top of the one it just bent.
        var editor = NewEditor(AnnotationTool.Line);
        Drag(editor, new CapturePoint(0, 0), new CapturePoint(100, 0));
        Select(editor, new CapturePoint(50, 0));

        var grabbed = editor.PointerPressed(new CapturePoint(50, 0), EditorModifiers.Extend);
        editor.PointerReleased(new CapturePoint(50, 0), EditorModifiers.Extend);

        Assert.IsTrue(grabbed, "the press must read as taking hold of a mark, so nothing is placed under it");
        Assert.IsNull(editor.Draft);
        Assert.AreEqual(1, editor.Document.Annotations.Count);
        Assert.AreEqual(1, editor.Document.Annotations[0].Waypoints.Count);
    }

    [TestMethod]
    public void CtrlPressOnAnUnselectedMark_AddsItToTheSelectionRatherThanBendingIt()
    {
        // The one key means both things, and this is the line between them: macOS bends
        // only the mark that is already selected (OverlayView.swift:5491-5497) and reserves
        // every other Ctrl+press for the selection. Bending whatever was under the pointer
        // would make a line the one kind of mark that cannot be multi-selected.
        var editor = NewEditor(AnnotationTool.Line);
        Drag(editor, new CapturePoint(0, 0), new CapturePoint(100, 0));
        Drag(editor, new CapturePoint(0, 60), new CapturePoint(100, 60));
        Select(editor, new CapturePoint(50, 0));

        editor.PointerPressed(new CapturePoint(50, 60), EditorModifiers.Extend);
        editor.PointerReleased(new CapturePoint(50, 60), EditorModifiers.Extend);

        Assert.AreEqual(2, editor.SelectedAnnotations.Count);
        Assert.IsFalse(
            editor.Document.Annotations.Any(annotation => annotation.HasWaypoints),
            "neither line may have been bent");
    }

    [TestMethod]
    public void BendingAMark_OffersAGripForTheAnchorItAdded()
    {
        // The anchor is worth nothing until it can be moved, and only a selected mark's
        // handles are offered.
        var editor = NewEditor(AnnotationTool.Arrow);
        Drag(editor, new CapturePoint(0, 0), new CapturePoint(100, 0));
        Select(editor, new CapturePoint(40, 0));

        editor.PointerPressed(new CapturePoint(40, 0), EditorModifiers.Extend);
        editor.PointerReleased(new CapturePoint(40, 0), EditorModifiers.Extend);

        Assert.AreEqual(
            1,
            editor.Handles.Count(handle => handle.Kind == AnnotationHandleKind.Waypoint));
    }

    [TestMethod]
    public void BendingAMark_IsOneUndoStep()
    {
        // Ctrl+Z has to take the anchor back off. Amended in place it would be
        // unreachable, and the user's only way back would be deleting the whole mark.
        var editor = NewEditor(AnnotationTool.Line);
        Drag(editor, new CapturePoint(0, 0), new CapturePoint(100, 0));
        Select(editor, new CapturePoint(50, 0));

        editor.PointerPressed(new CapturePoint(50, 0), EditorModifiers.Extend);
        editor.PointerReleased(new CapturePoint(50, 0), EditorModifiers.Extend);
        editor.Undo();

        Assert.IsFalse(editor.Document.Annotations[0].HasWaypoints);
    }

    [TestMethod]
    public void DraggingAnAnchorGrip_ReshapesTheMarkAndCommitsOnce()
    {
        // The whole path from press to release: the grip has to be found by index, dragged,
        // and the result kept. Any of the three missing leaves the anchor visible and
        // immovable.
        var editor = NewEditor(AnnotationTool.Line);
        Drag(editor, new CapturePoint(0, 0), new CapturePoint(100, 0));
        Select(editor, new CapturePoint(50, 0));
        editor.PointerPressed(new CapturePoint(50, 0), EditorModifiers.Extend);
        editor.PointerReleased(new CapturePoint(50, 0), EditorModifiers.Extend);

        var grip = editor.Handles.Single(handle => handle.Kind == AnnotationHandleKind.Waypoint).Position;
        Drag(editor, grip, new CapturePoint(50, 40));

        Assert.AreEqual(new CapturePoint(50, 40), editor.Document.Annotations[0].Waypoints[0]);
    }

    [TestMethod]
    public void SwitchingTools_KeepsTheSelectionSoDeleteStillRemovesTheMark()
    {
        // macshot's handleToolbarAction changes the tool and nothing else
        // (OverlayView.swift:7887-7898). Clearing here meant picking up a different tool
        // silently disarmed Delete, Backspace and every restyle on the mark the user had
        // just chosen — with the chrome still drawn around it saying otherwise.
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 40));

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(10, 10));
        editor.Tool = AnnotationTool.Pencil;

        Assert.IsNotNull(editor.Selected);
        Assert.IsTrue(editor.DeleteSelected());
        Assert.AreEqual(0, editor.Document.Annotations.Count);
    }

    [TestMethod]
    public void SelectedHandles_AnswerToThePencilThoughItNeverSelectsByClicking()
    {
        // The handles are drawn from the selection rather than from the tool, so once a
        // selection survives into the pencil they are on screen. A handle that is drawn
        // and cannot be grabbed is worse than one that is not drawn at all: the press
        // lands on it and lays down ink instead.
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 40));

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(10, 10));
        editor.Tool = AnnotationTool.Pencil;

        Assert.AreNotEqual(0, editor.Handles.Count);
        Drag(editor, new CapturePoint(60, 40), new CapturePoint(90, 70));

        Assert.AreEqual(1, editor.Document.Annotations.Count, "the press must reshape, not draw");
        Assert.AreEqual(90, editor.Document.Annotations[0].BoundingRect.Right, 1e-9);
    }

    [TestMethod]
    public void SelectedHandles_AreLeftAloneWhileDrawThroughIsHeld()
    {
        // Draw-through is how a mark is deliberately laid over another one, and a
        // selection's handles float outside its bounds — so without this exemption the
        // one escape from grabbing would still be blocked wherever a handle happened to
        // be. macOS gates the same check on the same flag (OverlayView.swift:8232-8237).
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 40));

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(10, 10));

        editor.Tool = AnnotationTool.Censor;
        Drag(
            editor,
            new CapturePoint(60, 40),
            new CapturePoint(120, 100),
            EditorModifiers.DrawThrough);

        Assert.AreEqual(2, editor.Document.Annotations.Count);
        Assert.AreEqual(60, editor.Document.Annotations[0].BoundingRect.Right, 1e-9);
    }

    [TestMethod]
    public void CtrlClick_AddsTheMarkToTheSelectionInsteadOfReplacingIt()
    {
        var editor = TwoMarks();

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(10, 10));
        Select(editor, new CapturePoint(210, 10), EditorModifiers.Extend);

        Assert.AreEqual(2, editor.SelectedAnnotations.Count);

        // And nothing offers to reshape one of a group: the drag moves all of them, so a
        // corner handle would be a promise the gesture does not keep.
        Assert.IsNull(editor.Selected);
        Assert.AreEqual(0, editor.Handles.Count);
    }

    [TestMethod]
    public void CtrlClickOnASelectedMark_TakesItBackOutAtTheRelease()
    {
        var editor = TwoMarks();

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(10, 10));
        Select(editor, new CapturePoint(210, 10), EditorModifiers.Extend);
        Select(editor, new CapturePoint(210, 10), EditorModifiers.Extend);

        Assert.AreEqual(1, editor.SelectedAnnotations.Count);
        Assert.AreEqual(editor.Document.Annotations[0].Id, editor.SelectedAnnotations[0].Id);
    }

    [TestMethod]
    public void CtrlDragFromASelectedMark_MovesTheGroupRatherThanDeselectingIt()
    {
        // The deselect waits for the release for exactly this: the press that would undo
        // one member's selection is also the press that starts dragging the group, and
        // removing it up front would drop that member out from under the drag.
        var editor = TwoMarks();

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(10, 10));
        Select(editor, new CapturePoint(210, 10), EditorModifiers.Extend);

        Drag(editor, new CapturePoint(210, 10), new CapturePoint(230, 30), EditorModifiers.Extend);

        Assert.AreEqual(2, editor.SelectedAnnotations.Count, "the drag must not have deselected it");
        Assert.AreEqual(30, editor.Document.Annotations[0].Start.X, 1e-9);
        Assert.AreEqual(230, editor.Document.Annotations[1].Start.X, 1e-9);
    }

    [TestMethod]
    public void DraggingOneOfAGroup_MovesAllOfThemAsOneUndoStep()
    {
        // One gesture, one step. Undoing a group move one mark at a time would walk the
        // selection back through arrangements nobody ever made.
        var editor = TwoMarks();

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(10, 10));
        Select(editor, new CapturePoint(210, 10), EditorModifiers.Extend);

        Drag(editor, new CapturePoint(10, 10), new CapturePoint(40, 10));

        Assert.AreEqual(40, editor.Document.Annotations[0].Start.X, 1e-9);
        Assert.AreEqual(240, editor.Document.Annotations[1].Start.X, 1e-9);

        editor.Undo();

        Assert.AreEqual(10, editor.Document.Annotations[0].Start.X, 1e-9);
        Assert.AreEqual(210, editor.Document.Annotations[1].Start.X, 1e-9);
    }

    [TestMethod]
    public void DraggingAGroup_ShowsEveryMemberWhereItIsGoingRatherThanWhereItWas()
    {
        // The whole selection has to travel on screen during the drag: a group where only
        // the mark under the pointer moves reads as the drag having lost the rest of it.
        var editor = TwoMarks();

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(10, 10));
        Select(editor, new CapturePoint(210, 10), EditorModifiers.Extend);

        editor.PointerPressed(new CapturePoint(10, 10));
        editor.PointerMoved(new CapturePoint(40, 10));

        var shown = editor.SelectedAsShown.ToList();
        Assert.AreEqual(40, shown[0].Start.X, 1e-9);
        Assert.AreEqual(240, shown[1].Start.X, 1e-9);
        CollectionAssert.AreEquivalent(
            shown.Select(mark => mark.Start.X).ToArray(),
            editor.VisibleAnnotations.Select(mark => mark.Start.X).ToArray(),
            "the marks and the chrome round them must be drawn at the same places");
    }

    [TestMethod]
    public void MultiSelectionBounds_CoverEveryMemberSoItsDeleteButtonHangsFromTheGroup()
    {
        // With more than one mark selected nothing is drawn but the outlines, so this is
        // the only thing on screen saying the group can be removed — macOS hangs one
        // consolidated button off the same union (OverlayView.swift:4863-4894).
        var editor = TwoMarks();

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(10, 10));

        Assert.IsNull(editor.MultiSelectionBounds, "one mark has handles, and no button of its own");

        Select(editor, new CapturePoint(210, 10), EditorModifiers.Extend);

        var bounds = editor.MultiSelectionBounds;
        Assert.IsNotNull(bounds);
        Assert.AreEqual(10, bounds.Value.X, 1e-9);
        Assert.AreEqual(260, bounds.Value.Right, 1e-9);
    }

    [TestMethod]
    public void DeleteSelected_RemovesEveryMarkOfAGroupAsOneUndoStep()
    {
        var editor = TwoMarks();

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(10, 10));
        Select(editor, new CapturePoint(210, 10), EditorModifiers.Extend);

        Assert.IsTrue(editor.DeleteSelected());
        Assert.AreEqual(0, editor.Document.Annotations.Count);
        Assert.AreEqual(0, editor.SelectedAnnotations.Count);

        editor.Undo();

        Assert.AreEqual(2, editor.Document.Annotations.Count, "one keystroke must cost one undo");
    }

    [TestMethod]
    public void CtrlDragOnEmptySpace_SelectsEverythingTheMarqueeSwept()
    {
        var editor = TwoMarks();

        editor.Tool = AnnotationTool.Rectangle;
        editor.PointerPressed(new CapturePoint(5, 150), EditorModifiers.Extend);
        editor.PointerMoved(new CapturePoint(300, 5), EditorModifiers.Extend);

        // Drawn while the drag is live, or there is nothing on screen saying what the
        // release is about to take.
        Assert.IsNotNull(editor.Lasso);
        Assert.AreEqual(2, editor.Document.Annotations.Count, "the marquee must not have drawn a rectangle");

        editor.PointerReleased(new CapturePoint(300, 5), EditorModifiers.Extend);

        Assert.AreEqual(2, editor.SelectedAnnotations.Count);
        Assert.IsNull(editor.Lasso);
        Assert.AreEqual(2, editor.Document.Annotations.Count);
    }

    [TestMethod]
    public void AMarqueeThatCaughtNothing_LeavesTheSelectionStanding()
    {
        // Clearing here would make a Ctrl+drag that missed cost the user the group they
        // had just built up, which is the one thing the modifier is for. macshot keeps it
        // too (OverlayView.swift:6427-6432).
        var editor = TwoMarks();

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(10, 10));
        Select(editor, new CapturePoint(210, 10), EditorModifiers.Extend);

        Drag(editor, new CapturePoint(500, 500), new CapturePoint(600, 600), EditorModifiers.Extend);

        Assert.AreEqual(2, editor.SelectedAnnotations.Count);
    }

    [TestMethod]
    public void HoldingAPencilStillOverAMark_TakesHoldOfItAndDropsTheStroke()
    {
        // A tap and a drag both draw with the pencil — a single dot is a deliberate mark
        // — so there is no click left to mean "pick this up". Holding does it instead
        // (OverlayView.swift:8309-8347), and the ink laid down before the hold expired is
        // thrown away rather than committed beside the mark now being dragged.
        var editor = TwoMarks();
        editor.Tool = AnnotationTool.Pencil;

        editor.PointerPressed(new CapturePoint(30, 30));
        Assert.IsTrue(editor.SelectsByHolding());
        Assert.IsTrue(editor.LongPressed(new CapturePoint(30, 30)));

        editor.PointerMoved(new CapturePoint(60, 30));
        editor.PointerReleased(new CapturePoint(60, 30));

        Assert.AreEqual(2, editor.Document.Annotations.Count, "no stroke may be left behind");
        Assert.AreEqual(40, editor.Document.Annotations[0].Start.X, 1e-9);
    }

    [TestMethod]
    public void HoldingAPencilStillOverNothing_LeavesTheStrokeToCarryOn()
    {
        var editor = NewEditor(AnnotationTool.Pencil);

        editor.PointerPressed(new CapturePoint(400, 400));
        Assert.IsFalse(editor.LongPressed(new CapturePoint(400, 400)));

        editor.PointerMoved(new CapturePoint(430, 400));
        editor.PointerReleased(new CapturePoint(430, 400));

        Assert.AreEqual(1, editor.Document.Annotations.Count);
        Assert.AreEqual(AnnotationTool.Pencil, editor.Document.Annotations[0].Tool);
    }

    [TestMethod]
    public void APencilPressWithAModifierOnIt_IsNotOneToHold()
    {
        // Both modifiers have already told the press what it means: Ctrl that it is about
        // the selection, draw-through that what is underneath is to be ignored. Arming
        // the timer would put a third meaning on the same gesture.
        var editor = NewEditor(AnnotationTool.Pencil);

        Assert.IsFalse(editor.SelectsByHolding(EditorModifiers.Extend));
        Assert.IsFalse(editor.SelectsByHolding(EditorModifiers.DrawThrough));

        editor.Tool = AnnotationTool.Rectangle;
        Assert.IsFalse(editor.SelectsByHolding(), "a tool that selects on the click has nothing to wait for");
    }

    [TestMethod]
    public void APencilWithAGroupSelected_DragsItWithoutAModifier()
    {
        // Otherwise the only way to move a group the user has just built is to put the
        // pencil down and pick the pointer up first. macOS makes the same exception
        // (OverlayView.swift:8247, 8253).
        var editor = TwoMarks();

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(10, 10));
        Select(editor, new CapturePoint(210, 10), EditorModifiers.Extend);

        editor.Tool = AnnotationTool.Pencil;
        Drag(editor, new CapturePoint(30, 30), new CapturePoint(60, 30));

        Assert.AreEqual(2, editor.Document.Annotations.Count, "the press must move, not draw");
        Assert.AreEqual(40, editor.Document.Annotations[0].Start.X, 1e-9);
        Assert.AreEqual(240, editor.Document.Annotations[1].Start.X, 1e-9);
    }

    /// <summary>Two censors far enough apart to be pressed and swept independently.</summary>
    private static AnnotationEditor TwoMarks()
    {
        var editor = NewEditor(AnnotationTool.Censor);
        Drag(editor, new CapturePoint(10, 10), new CapturePoint(60, 60));
        Drag(editor, new CapturePoint(210, 10), new CapturePoint(260, 60));
        return editor;
    }

    /// <summary>Clicks a mark, which is what arms its handles.</summary>
    [TestMethod]
    public void Reposition_MovesTheShapeBeingDrawnAndLeavesItsSizeAlone()
    {
        // The size a drag has reached is the part that was right; where its first corner
        // landed is the part that was not. Space is what saves the first without losing the
        // second, and a reposition that resized anything would defeat the whole gesture.
        var editor = NewEditor(AnnotationTool.Rectangle);

        editor.PointerPressed(new CapturePoint(10, 10));
        editor.PointerMoved(new CapturePoint(50, 40));

        // The first sample with the key down only says where the pointer was: there is
        // nothing yet to measure the move against.
        editor.PointerMoved(new CapturePoint(50, 40), EditorModifiers.Reposition);
        editor.PointerMoved(new CapturePoint(70, 60), EditorModifiers.Reposition);

        var draft = editor.Draft;
        Assert.IsNotNull(draft);
        Assert.AreEqual(new CapturePoint(30, 30), draft.Start);
        Assert.AreEqual(new CapturePoint(70, 60), draft.End);
        Assert.AreEqual(40, draft.BoundingRect.Width, 1e-9);
        Assert.AreEqual(30, draft.BoundingRect.Height, 1e-9);
    }

    [TestMethod]
    public void Reposition_MovesAMarkBeingResizedRatherThanResizingIt()
    {
        // The same key over a grip. Without this, the only way to put a mark that was
        // reshaped into the wrong place back is to let go and drag it again, which is the
        // reshape thrown away.
        var editor = NewEditor(AnnotationTool.Rectangle);
        Drag(editor, new CapturePoint(0, 0), new CapturePoint(40, 30));

        editor.Tool = AnnotationTool.Select;
        Select(editor, new CapturePoint(0, 0));

        var grip = editor.Handles
            .Single(handle => handle.Kind == AnnotationHandleKind.BottomRight)
            .Position;

        editor.PointerPressed(grip);
        editor.PointerMoved(grip, EditorModifiers.Reposition);
        editor.PointerMoved(new CapturePoint(grip.X + 10, grip.Y + 15), EditorModifiers.Reposition);
        editor.PointerReleased(
            new CapturePoint(grip.X + 10, grip.Y + 15),
            EditorModifiers.Reposition);

        var moved = editor.Document.Annotations[0].BoundingRect;
        Assert.AreEqual(40, moved.Width, 1e-9, "a reposition may not resize the mark");
        Assert.AreEqual(30, moved.Height, 1e-9, "a reposition may not resize the mark");
        Assert.AreEqual(10, moved.X, 1e-9);
        Assert.AreEqual(15, moved.Y, 1e-9);
    }

    [TestMethod]
    public void Reposition_LeavesFreehandInkAlone()
    {
        // A pencil stroke has no misplaced corner to rescue, only ink, and shifting the
        // trail halfway along would break it where the key went down. macOS excludes the
        // freehand tools by name (OverlayView.swift:8964).
        var editor = NewEditor(AnnotationTool.Pencil);

        editor.PointerPressed(new CapturePoint(10, 10));
        editor.PointerMoved(new CapturePoint(20, 20));
        editor.PointerMoved(new CapturePoint(20, 20), EditorModifiers.Reposition);
        editor.PointerMoved(new CapturePoint(40, 40), EditorModifiers.Reposition);

        var draft = editor.Draft;
        Assert.IsNotNull(draft);
        Assert.AreEqual(
            new CapturePoint(10, 10),
            draft.Points[0],
            "the stroke must still start where the pointer went down");
    }

    [TestMethod]
    public void CanReposition_IsOnlyTrueMidDragSoSpaceStaysAShortcutOtherwise()
    {
        // Space is bound to Move Selection out of the box, and the window asks this before
        // it lets the key through to that binding. Answering yes when no drag is in flight
        // would swallow the shortcut for good; answering no mid-drag would let the shortcut
        // fire and the reposition could never happen (OverlayView.swift:8946-8985).
        var editor = NewEditor(AnnotationTool.Rectangle);
        Assert.IsFalse(editor.CanReposition, "an idle editor has nothing to reposition");

        editor.PointerPressed(new CapturePoint(10, 10));
        editor.PointerMoved(new CapturePoint(50, 40));
        Assert.IsTrue(editor.CanReposition, "a shape being dragged out is what Space moves");

        editor.PointerReleased(new CapturePoint(50, 40));
        Assert.IsFalse(editor.CanReposition, "the drag is over, so Space is the shortcut again");
    }

    private static void Select(
        AnnotationEditor editor,
        CapturePoint point,
        EditorModifiers modifiers = EditorModifiers.None)
    {
        editor.PointerPressed(point, modifiers);
        editor.PointerReleased(point, modifiers);
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

    private static Annotation? DragOut(AnnotationEditor editor, CapturePoint from, CapturePoint to)
    {
        editor.PointerPressed(from);
        editor.PointerMoved(to);
        return editor.PointerReleased(to);
    }
}
