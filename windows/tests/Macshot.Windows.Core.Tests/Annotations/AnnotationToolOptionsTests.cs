using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Annotations;

[TestClass]
public sealed class AnnotationToolOptionsTests
{
    [TestMethod]
    public void EveryDrawnTool_TakesTheSizeControl()
    {
        // Every tool that draws a mark. The censor is out because its two strengths are
        // chosen for the user rather than set, and the spotlight because what it puts on
        // the capture is a hairline round a region — the region is what was dragged, and
        // the hairline is the same width whatever the slider says.
        var drawn = AnnotationRasterizer.SupportedTools
            .Where(tool => tool is not (AnnotationTool.Censor or AnnotationTool.Highlight));

        foreach (var tool in drawn)
        {
            Assert.IsTrue(AnnotationToolOptions.UsesSize(tool), $"{tool} should take a size");
        }
    }

    [TestMethod]
    public void TheSpotlight_TakesNeitherAColourNorASize()
    {
        // Its whole mark is decided for it: black outside, a white hairline round the
        // light, both at the strengths macshot draws them. A colour swatch and a width
        // slider on this tool would be two controls that change nothing at all.
        Assert.IsFalse(AnnotationToolOptions.UsesColor(AnnotationTool.Highlight));
        Assert.IsFalse(AnnotationToolOptions.UsesSize(AnnotationTool.Highlight));

        // The one thing about that hairline the user does choose, as macshot lets them —
        // through the spotlight's own two-way control rather than the general dash picker,
        // which offers a dotted ring the tool has no use for.
        Assert.IsTrue(AnnotationToolOptions.UsesSpotlightBorder(AnnotationTool.Highlight));
        Assert.IsFalse(AnnotationToolOptions.UsesLineStyle(AnnotationTool.Highlight));
    }

    /// <summary>
    /// The dim belongs to the spotlight and to nothing else. It is the strength of a layer
    /// laid over the whole capture, so a second tool offering it would be a second control
    /// for the same number — and the row would show it while holding a pencil, which draws
    /// nothing that dims anything.
    /// </summary>
    [TestMethod]
    public void TheDimSlider_BelongsToTheSpotlightAlone()
    {
        Assert.IsTrue(AnnotationToolOptions.UsesDimStrength(AnnotationTool.Highlight));

        foreach (var tool in AnnotationRasterizer.SupportedTools
            .Where(tool => tool != AnnotationTool.Highlight))
        {
            Assert.IsFalse(
                AnnotationToolOptions.UsesDimStrength(tool),
                $"{tool} should not take the dim slider");
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
