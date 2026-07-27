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

    private static Annotation NewLine()
    {
        return Annotation.Create(AnnotationTool.Line, new CapturePoint(0, 0), new CapturePoint(10, 10));
    }

    private static Annotation NewFilledRectangle()
    {
        return Annotation.Create(AnnotationTool.FilledRectangle, new CapturePoint(10, 10), new CapturePoint(30, 30));
    }
}
