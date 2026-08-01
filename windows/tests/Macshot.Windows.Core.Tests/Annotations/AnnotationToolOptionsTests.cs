using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Annotations;

[TestClass]
public sealed class AnnotationToolOptionsTests
{
    [TestMethod]
    public void EveryDrawnTool_TakesTheSizeControl()
    {
        // Every tool that draws a mark, which is all of them but the censor: its two
        // strengths are chosen for the user rather than set.
        foreach (var tool in AnnotationRasterizer.SupportedTools.Where(tool => tool != AnnotationTool.Censor))
        {
            Assert.IsTrue(AnnotationToolOptions.UsesSize(tool), $"{tool} should take a size");
        }
    }

    [TestMethod]
    public void ThePointer_TakesNoneOfThem()
    {
        // It changes marks already drawn rather than making one, so every style control
        // would be setting something for a mark that is not coming.
        Assert.IsFalse(AnnotationToolOptions.UsesColor(AnnotationTool.Select));
        Assert.IsFalse(AnnotationToolOptions.UsesSize(AnnotationTool.Select));
        Assert.IsFalse(AnnotationToolOptions.UsesLineStyle(AnnotationTool.Select));
    }

    [TestMethod]
    public void TheCensorTool_TakesNoSizeBecauseNeitherOfItsStrengthsIsChosen()
    {
        // The point of the whole tool: a redaction whose strength follows a slider set
        // for something else is a redaction that is a different strength every time. The
        // cell is fixed and the blur radius comes from the region.
        Assert.IsFalse(AnnotationToolOptions.UsesSize(AnnotationTool.Censor));
        Assert.IsTrue(AnnotationToolOptions.UsesCensorMode(AnnotationTool.Censor));
        Assert.IsFalse(AnnotationToolOptions.UsesCensorMode(AnnotationTool.Rectangle));

        // It does take the colour, because one of the four modes paints in it.
        Assert.IsTrue(AnnotationToolOptions.UsesColor(AnnotationTool.Censor));
    }

    [TestMethod]
    public void SpriteTools_TakeAColourBecauseItIsBakedIntoTheGlyphs()
    {
        Assert.IsTrue(AnnotationToolOptions.UsesColor(AnnotationTool.Text));
        Assert.IsTrue(AnnotationToolOptions.UsesColor(AnnotationTool.Number));
    }

    [TestMethod]
    public void OnlyStrokeTools_TakeTheDashPattern()
    {
        // The dash comes from the stroke compositor, so a fill, an effect and a sprite
        // each ignore it however it is set.
        Assert.IsTrue(AnnotationToolOptions.UsesLineStyle(AnnotationTool.Rectangle));
        Assert.IsTrue(AnnotationToolOptions.UsesLineStyle(AnnotationTool.Loupe));
        Assert.IsFalse(AnnotationToolOptions.UsesLineStyle(AnnotationTool.Censor));
        Assert.IsFalse(AnnotationToolOptions.UsesLineStyle(AnnotationTool.Text));
    }

    [TestMethod]
    public void OnlyTheArrowTool_TakesTheEndsPicker()
    {
        // Nothing else in the toolbar has ends to choose between.
        Assert.IsTrue(AnnotationToolOptions.UsesArrowStyle(AnnotationTool.Arrow));
        Assert.IsFalse(AnnotationToolOptions.UsesArrowStyle(AnnotationTool.Line));
    }

    [TestMethod]
    public void OnlyTheOutlinedRectangle_TakesTheCornerControl()
    {
        Assert.IsTrue(AnnotationToolOptions.UsesCornerRadius(AnnotationTool.Rectangle));
        Assert.IsFalse(
            AnnotationToolOptions.UsesCornerRadius(AnnotationTool.Censor),
            "rounding the corners of a redaction uncovers what it was placed over");
    }

    [TestMethod]
    public void OnlyTheStampTool_TakesTheEmojiPicker()
    {
        Assert.IsTrue(AnnotationToolOptions.UsesStamp(AnnotationTool.Stamp));
        Assert.IsFalse(AnnotationToolOptions.UsesStamp(AnnotationTool.Text));
    }

    [TestMethod]
    public void TheSizeControl_SaysWhatItChangesForTheToolInHand()
    {
        Assert.AreEqual(AnnotationSizeMeaning.Thickness, AnnotationToolOptions.SizeMeaning(AnnotationTool.Arrow));
        Assert.AreEqual(AnnotationSizeMeaning.Extent, AnnotationToolOptions.SizeMeaning(AnnotationTool.Number));
    }
}
