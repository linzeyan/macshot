using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Annotations;

[TestClass]
public sealed class AnnotationToolOptionsTests
{
    [TestMethod]
    public void EveryToolTheToolbarOffers_TakesTheSizeControl()
    {
        // The one control that means something for all of them, which is why it is the
        // one that is never hidden.
        foreach (var tool in AnnotationRasterizer.SupportedTools)
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
    public void RegionEffects_TakeNoColourBecauseTheyRewriteWhatIsUnderThem()
    {
        Assert.IsFalse(AnnotationToolOptions.UsesColor(AnnotationTool.Pixelate));
        Assert.IsFalse(AnnotationToolOptions.UsesColor(AnnotationTool.Blur));
        Assert.IsTrue(AnnotationToolOptions.UsesSize(AnnotationTool.Blur), "the radius is the size");
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
        Assert.IsFalse(AnnotationToolOptions.UsesLineStyle(AnnotationTool.FilledRectangle));
        Assert.IsFalse(AnnotationToolOptions.UsesLineStyle(AnnotationTool.Pixelate));
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
            AnnotationToolOptions.UsesCornerRadius(AnnotationTool.FilledRectangle),
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
        Assert.AreEqual(AnnotationSizeMeaning.Strength, AnnotationToolOptions.SizeMeaning(AnnotationTool.Pixelate));
    }
}
