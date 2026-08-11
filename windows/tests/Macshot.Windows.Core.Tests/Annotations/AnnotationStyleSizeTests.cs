using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Annotations;

/// <summary>
/// The five widths behind the toolbar's one size slider, and which tool reads which.
/// </summary>
/// <remarks>
/// These matter because the sizes wanted are orders apart. A highlighter is set to the
/// height of a line of text and a loupe to the width of a circle; an arrow is a hairline.
/// One shared number meant that a trip through the highlighter was the last thing that
/// had happened to the next arrow, which macshot does not do
/// (<c>OverlayView.swift:9718</c>).
/// </remarks>
[TestClass]
public sealed class AnnotationStyleSizeTests
{
    [TestMethod]
    public void SizeFor_GivesEachToolItsOwnNumberRatherThanTheSharedStroke()
    {
        var style = AnnotationStyle.Default with
        {
            StrokeWidth = 3,
            MarkerStrokeWidth = 18,
            NumberStrokeWidth = 7,
            LoupeSize = 120,
            StampSize = 64,
        };

        Assert.AreEqual(18, style.SizeFor(AnnotationTool.Marker));
        Assert.AreEqual(7, style.SizeFor(AnnotationTool.Number));
        Assert.AreEqual(120, style.SizeFor(AnnotationTool.Loupe));
        Assert.AreEqual(64, style.SizeFor(AnnotationTool.Stamp));
        Assert.AreEqual(3, style.SizeFor(AnnotationTool.Arrow));
        Assert.AreEqual(3, style.SizeFor(AnnotationTool.Rectangle));
    }

    [TestMethod]
    public void WithSizeFor_LeavesEveryOtherWidthWhereItWas()
    {
        // The whole point of the split: setting one must not be readable as having set
        // another, or the slider is back to being one number wearing five hats.
        var fattened = AnnotationStyle.Default.WithSizeFor(AnnotationTool.Marker, 24);

        Assert.AreEqual(24, fattened.MarkerStrokeWidth);
        Assert.AreEqual(AnnotationStyle.Default.StrokeWidth, fattened.StrokeWidth);
        Assert.AreEqual(AnnotationStyle.Default.NumberStrokeWidth, fattened.NumberStrokeWidth);
        Assert.AreEqual(AnnotationStyle.Default.LoupeSize, fattened.LoupeSize);
        Assert.AreEqual(AnnotationStyle.Default.StampSize, fattened.StampSize);
    }

    [TestMethod]
    public void ForTool_MovesTheRememberedWidthWhereTheRasterizerReadsIt()
    {
        var style = AnnotationStyle.Default with { StrokeWidth = 3, MarkerStrokeWidth = 18 };

        // A placed mark carries one width, so nothing downstream has to ask which tool
        // made it — the resolution happens once, here.
        Assert.AreEqual(18, style.ForTool(AnnotationTool.Marker).StrokeWidth);
        Assert.AreEqual(3, style.ForTool(AnnotationTool.Arrow).StrokeWidth);
    }

    [TestMethod]
    public void ForTool_LeavesTheLoupeAndStampAlone()
    {
        // Their sizes are read from members of their own. Moved into the stroke width, a
        // loupe 120 across would be drawn with a ring 120 pixels thick.
        var style = AnnotationStyle.Default with { StrokeWidth = 3, LoupeSize = 120, StampSize = 64 };

        Assert.AreEqual(3, style.ForTool(AnnotationTool.Loupe).StrokeWidth);
        Assert.AreEqual(3, style.ForTool(AnnotationTool.Stamp).StrokeWidth);
    }

    [TestMethod]
    public void HighlighterDrawnFat_LeavesTheNextArrowThin()
    {
        // The behaviour the split exists for, end to end through the editor.
        var editor = new AnnotationEditor(new AnnotationDocument())
        {
            Style = AnnotationStyle.Default with { StrokeWidth = 3, MarkerStrokeWidth = 18 },
            Tool = AnnotationTool.Marker,
        };

        editor.PointerPressed(new CapturePoint(10, 10));
        editor.PointerMoved(new CapturePoint(60, 10));
        var marker = editor.PointerReleased(new CapturePoint(60, 10));

        editor.Tool = AnnotationTool.Arrow;
        editor.PointerPressed(new CapturePoint(10, 40));
        editor.PointerMoved(new CapturePoint(60, 40));
        var arrow = editor.PointerReleased(new CapturePoint(60, 40));

        Assert.AreEqual(18, marker?.Style.StrokeWidth, "the highlighter draws at its own width");
        Assert.AreEqual(3, arrow?.Style.StrokeWidth, "and leaves the arrow at the shared one");
    }
}
