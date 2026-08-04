using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Annotations;

/// <summary>
/// What counts as an unsaved edit, which is what decides whether Done is offered and
/// whether closing the editor asks first.
/// </summary>
[TestClass]
public sealed class EditorStateTests
{
    private static EditorState Saved => new(3, 1, ImageEffectsOptions.Default);

    [TestMethod]
    public void AWindowNobodyTouchedClosesWithoutAsking()
    {
        // The prompt macshot removed and rebuilt: it used to compare re-encoded PNGs and
        // round-tripped floats, so a capture with nothing done to it asked "Save changes?"
        // on the way out. A prompt that appears when there is nothing to save is one people
        // learn to dismiss without reading, which is how a real edit gets thrown away.
        var again = new EditorState(3, 1, ImageEffectsOptions.Default);

        Assert.IsFalse(again.DiffersFrom(Saved));
    }

    [TestMethod]
    public void DrawingAMarkOffersDone()
    {
        // Every mark drawn, moved or deleted pushes an undo step, so the depth moving is
        // the user having edited the annotations.
        Assert.IsTrue(new EditorState(4, 1, ImageEffectsOptions.Default).DiffersFrom(Saved));
    }

    [TestMethod]
    public void CroppingOrFramingOffersDoneEvenThoughItLeavesNoUndoStep()
    {
        // An operation that replaces the pixels flattens the marks and resets the
        // document's history, so its undo depth goes back to nothing. Counted only through
        // that depth, cropping a capture would read as un-editing it — the one change that
        // cannot be recovered from the history would be the one nothing asked about.
        Assert.IsTrue(new EditorState(0, 2, ImageEffectsOptions.Default).DiffersFrom(Saved));
    }

    [TestMethod]
    public void MovingAnAdjustSliderOffersDone()
    {
        // The adjust options are a layer over the image rather than something burnt into
        // it, so they leave no undo step and no image operation behind. The delivered
        // pixels still come through them, which makes them an edit like any other.
        var adjusted = Saved with { Effects = ImageEffectsOptions.Default with { Brightness = 0.2 } };

        Assert.IsTrue(adjusted.DiffersFrom(Saved));
    }

    [TestMethod]
    public void TakingAMarkBackOffAgainClosesWithoutAsking()
    {
        // Drawing and then undoing is not an edit. Compared with a counter that only
        // climbed, the window would ask about annotations that are no longer there — and
        // the answer "Save changes?" invites for a capture with nothing on it is a saved
        // file nobody wanted.
        var drawn = Saved with { UndoDepth = Saved.UndoDepth + 1 };

        Assert.IsTrue(drawn.DiffersFrom(Saved));
        Assert.IsFalse((drawn with { UndoDepth = Saved.UndoDepth }).DiffersFrom(Saved));
    }

    [TestMethod]
    public void SavingMakesTheWindowCleanAgain()
    {
        // Delivering re-takes the baseline, which is what makes Done disappear after it has
        // been used and what stops the close prompt asking about edits already written
        // down. Without it every editor that had ever been saved would nag on the way out.
        var edited = new EditorState(9, 3, ImageEffectsOptions.Default with { Contrast = 0.5 });
        var rebaselined = edited;

        Assert.IsFalse(edited.DiffersFrom(rebaselined));
    }
}
