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
        document.Add(NewFilledRectangle());
        var above = NewFilledRectangle();
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
        document.Add(NewFilledRectangle());

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

        var restored = NewFilledRectangle();
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

    private static Annotation NewFilledRectangle()
    {
        return Annotation.Create(AnnotationTool.FilledRectangle, new CapturePoint(10, 10), new CapturePoint(30, 30));
    }
}
