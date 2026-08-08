using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Annotations;

[TestClass]
public sealed class AnnotationDocumentTests
{
    [TestMethod]
    public void Undo_RestoresTheStateBeforeTheLastEdit()
    {
        var document = new AnnotationDocument();
        document.Add(NewLine());

        Assert.IsTrue(document.Undo());
        Assert.AreEqual(0, document.Annotations.Count);
        Assert.IsTrue(document.Redo());
        Assert.AreEqual(1, document.Annotations.Count);
    }

    [TestMethod]
    public void Undo_TreatsOneBatchAsOneStep()
    {
        // Auto-redact produces many annotations from a single user action; undoing
        // it one rectangle at a time would be unusable.
        var document = new AnnotationDocument();
        var group = Guid.NewGuid();
        document.AddRange([NewLine() with { GroupId = group }, NewLine() with { GroupId = group }]);

        document.Undo();

        Assert.AreEqual(0, document.Annotations.Count);
    }

    [TestMethod]
    public void Edit_DiscardsForwardHistory()
    {
        var document = new AnnotationDocument();
        document.Add(NewLine());
        document.Undo();

        document.Add(NewLine());

        Assert.IsFalse(document.CanRedo, "redoing into an overwritten branch would resurrect discarded work");
    }

    [TestMethod]
    public void FailedEdit_LeavesHistoryUntouched()
    {
        // A no-op must not consume an undo step, or Ctrl+Z appears to do nothing.
        var document = new AnnotationDocument();

        Assert.IsFalse(document.Remove(Guid.NewGuid()));
        Assert.IsFalse(document.Clear());
        Assert.IsFalse(document.CanUndo);
    }

    [TestMethod]
    public void Replace_SwapsTheEditedCopyInPlaceAndIsUndoable()
    {
        var document = new AnnotationDocument();
        var original = NewLine();
        document.Add(original);

        var moved = original.Translate(10, 10);

        Assert.IsTrue(document.Replace(moved));
        Assert.AreEqual(1, document.Annotations.Count);
        Assert.AreEqual(moved.Start, document.Annotations[0].Start);
        Assert.IsTrue(document.Undo());
        Assert.AreEqual(original.Start, document.Annotations[0].Start);
    }

    [TestMethod]
    public void ReplaceRange_MovesAWholeSelectionInOneUndoStep()
    {
        // Dragging a multi-selection is one gesture, so it has to be one step. Replacing
        // the marks one at a time would leave Ctrl+Z walking the group back to where it
        // started a member at a time, and the half-moved states in between are
        // arrangements the user never made.
        var document = new AnnotationDocument();
        var first = NewLine();
        var second = NewCensor();
        document.AddRange([first, second]);

        Assert.IsTrue(document.ReplaceRange([first.Translate(10, 10), second.Translate(10, 10)]));
        Assert.AreEqual(10, document.Annotations[0].Start.X, 1e-9);
        Assert.AreEqual(20, document.Annotations[1].Start.X, 1e-9);

        document.Undo();

        Assert.AreEqual(first.Start.X, document.Annotations[0].Start.X, 1e-9);
        Assert.AreEqual(second.Start.X, document.Annotations[1].Start.X, 1e-9);
    }

    [TestMethod]
    public void ReplaceRange_RefusesABatchWithAStrangerInItRatherThanApplyingHalfOfIt()
    {
        // Half a move is a state no hand could have produced, and it would cost an undo
        // step to get back out of. A batch aimed at a mark the document has moved on from
        // means the caller's selection is stale, which is worth failing on.
        var document = new AnnotationDocument();
        var present = NewLine();
        document.Add(present);

        Assert.IsFalse(document.ReplaceRange([present.Translate(5, 5), NewCensor()]));
        Assert.AreEqual(present.Start.X, document.Annotations[0].Start.X, 1e-9);
        Assert.AreEqual(1, document.UndoDepth, "a refused edit must not consume a step");
    }

    [TestMethod]
    public void RemoveRange_TakesAWholeSelectionOffInOneStep()
    {
        // Delete over a multi-selection is one keystroke. One step per mark would mean
        // pressing Ctrl+Z once per mark to get the group back.
        var document = new AnnotationDocument();
        var first = NewLine();
        var second = NewCensor();
        var bystander = NewLine();
        document.AddRange([first, second, bystander]);

        Assert.IsTrue(document.RemoveRange([first.Id, second.Id]));
        Assert.AreEqual(1, document.Annotations.Count);
        Assert.AreEqual(bystander.Id, document.Annotations[0].Id);

        document.Undo();

        Assert.AreEqual(3, document.Annotations.Count);
    }

    [TestMethod]
    public void RemoveRange_SaysNoWhenNoneOfThemIsHereSoNoStepIsSpent()
    {
        var document = new AnnotationDocument();
        document.Add(NewLine());

        Assert.IsFalse(document.RemoveRange([Guid.NewGuid()]));
        Assert.IsFalse(document.RemoveRange([]));
        Assert.AreEqual(1, document.UndoDepth);
    }

    [TestMethod]
    public void RemoveGroup_RemovesEveryMemberAsOneStep()
    {
        var document = new AnnotationDocument();
        var group = Guid.NewGuid();
        document.AddRange([NewLine() with { GroupId = group }, NewLine() with { GroupId = group }, NewLine()]);

        Assert.IsTrue(document.RemoveGroup(group));
        Assert.AreEqual(1, document.Annotations.Count);
        Assert.IsFalse(document.RemoveGroup(group));
    }

    [TestMethod]
    public void HitTest_ReturnsTheTopmostAnnotation()
    {
        // Later annotations are drawn on top, so they must also be picked first.
        var document = new AnnotationDocument();
        document.Add(NewCensor());
        var above = NewCensor();
        document.Add(above);

        Assert.AreEqual(above.Id, document.HitTest(new CapturePoint(20, 20))?.Id);
    }

    [TestMethod]
    public void History_IsBoundedSoLongSessionsDoNotGrowWithoutLimit()
    {
        var document = new AnnotationDocument();
        for (var index = 0; index < AnnotationDocument.MaxHistoryDepth + 25; index++)
        {
            document.Add(NewLine());
        }

        var undone = 0;
        while (document.Undo())
        {
            undone++;
        }

        Assert.AreEqual(AnnotationDocument.MaxHistoryDepth, undone);
    }

    [TestMethod]
    public void Reset_LeavesNothingToUndoInto()
    {
        var document = new AnnotationDocument();
        document.Add(NewLine());
        document.Add(NewCensor());

        document.Reset();

        // The point of Reset over Clear: the marks have become part of a new image, so
        // every earlier state describes pixels that no longer exist. Offering to undo
        // into one would put marks back at coordinates that have moved.
        Assert.AreEqual(0, document.Annotations.Count);
        Assert.IsFalse(document.CanUndo);
        Assert.IsFalse(document.CanRedo);
    }

    [TestMethod]
    public void Reset_TakesTheAnnotationsThatBelongToTheNewImage()
    {
        var document = new AnnotationDocument();
        document.Add(NewLine());
        document.Undo();

        var restored = NewCensor();
        document.Reset([restored]);

        // Restoring a state on an image operation's behalf must not itself become a step,
        // or undoing the operation would need a second press to keep the marks.
        Assert.AreEqual(1, document.Annotations.Count);
        Assert.AreEqual(restored.Id, document.Annotations[0].Id);
        Assert.IsFalse(document.CanUndo);
        Assert.IsFalse(document.CanRedo);
    }

    private static Annotation NewLine()
    {
        return Annotation.Create(AnnotationTool.Line, new CapturePoint(0, 0), new CapturePoint(10, 10));
    }

    private static Annotation NewCensor()
    {
        return Annotation.Create(AnnotationTool.Censor, new CapturePoint(10, 10), new CapturePoint(30, 30));
    }

    [TestMethod]
    public void Amend_ChangesTheMarkWithoutAddingAnUndoStep()
    {
        // A ruler's reading is rendered after the drag that drew the ruler. If amending
        // it recorded a step, the first Ctrl+Z would strip the number off and leave the
        // ruler standing there unlabelled.
        var document = new AnnotationDocument();
        var ruler = Annotation.Create(AnnotationTool.Measure, new CapturePoint(0, 0), new CapturePoint(40, 0));
        document.Add(ruler);

        document.Amend(ruler with { Text = "40 px" });
        document.Undo();

        Assert.AreEqual(0, document.Annotations.Count, "undo must take back the drag, not the label");
    }

    [TestMethod]
    public void Amend_SaysNoForAMarkThatIsNoLongerThere()
    {
        var document = new AnnotationDocument();
        var ruler = Annotation.Create(AnnotationTool.Measure, new CapturePoint(0, 0), new CapturePoint(40, 0));

        Assert.IsFalse(document.Amend(ruler));
    }

    [TestMethod]
    public void Amend_TellsListenersSoTheCanvasRedraws()
    {
        var document = new AnnotationDocument();
        var ruler = Annotation.Create(AnnotationTool.Measure, new CapturePoint(0, 0), new CapturePoint(40, 0));
        document.Add(ruler);

        var raised = 0;
        document.Changed += (_, _) => raised++;
        document.Amend(ruler with { Text = "40 px" });

        Assert.AreEqual(1, raised);
    }
}
