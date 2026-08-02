using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Recognition;

namespace Macshot.Windows.Core.Tests.Recognition;

[TestClass]
public sealed class TextSnappingTests
{
    /// <summary>
    /// The whole point of the option: a stroke drawn by hand across a line of text sags
    /// and wanders, and what the user meant was a highlight sitting squarely on the line.
    /// </summary>
    [TestMethod]
    public void SnapToText_LevelsTheStrokeOntoTheLineItCrossed()
    {
        var line = Line(("highlight", new CaptureRegion(100, 200, 300, 20)));
        var stroke = Stroke(from: (110, 204), to: (380, 214));

        var snapped = TextSnapping.SnapToText(stroke, [line]);

        Assert.AreEqual(snapped.Start.Y, snapped.End.Y, "The snapped stroke is not level.");
        Assert.IsTrue(
            snapped.Start.Y > 200 && snapped.Start.Y < 220,
            "The stroke did not land on the line of text.");
    }

    /// <summary>
    /// Where the stroke starts and stops is the one thing the hand aimed at accurately, so
    /// it is the one thing the snap must not take away — highlighting three words of a
    /// line means three words, not the line.
    /// </summary>
    [TestMethod]
    public void SnapToText_KeepsTheSpanTheHandDrew()
    {
        var line = Line(("a whole long line of text", new CaptureRegion(100, 200, 300, 20)));
        var stroke = Stroke(from: (150, 208), to: (250, 208));

        var snapped = TextSnapping.SnapToText(stroke, [line]);

        Assert.AreEqual(150, snapped.Start.X);
        Assert.AreEqual(250, snapped.End.X);
    }

    /// <summary>
    /// Covering the text is what a highlighter does. Left at the width the slider happened
    /// to be on, the mark would either miss half the glyphs or swallow the line above.
    /// </summary>
    [TestMethod]
    public void SnapToText_ThickensTheStrokeToCoverTheText()
    {
        var line = Line(("text", new CaptureRegion(100, 200, 300, 20)));
        var stroke = Stroke(from: (110, 208), to: (380, 208), strokeWidth: 3);

        var snapped = TextSnapping.SnapToText(stroke, [line]);

        Assert.IsTrue(
            snapped.Style.StrokeWidth >= 20,
            $"A {snapped.Style.StrokeWidth} stroke does not cover a 20-tall line.");
    }

    /// <summary>
    /// The hand-drawn samples have to go with the ends. Left behind, the marker would draw
    /// its original wobbly path and the snap would appear to have done nothing at all —
    /// which is exactly how this failed before the samples were cleared.
    /// </summary>
    [TestMethod]
    public void SnapToText_DropsTheHandDrawnPath()
    {
        var line = Line(("text", new CaptureRegion(100, 200, 300, 20)));
        var stroke = Stroke(from: (110, 204), to: (380, 214));

        var snapped = TextSnapping.SnapToText(stroke, [line]);

        Assert.AreEqual(0, snapped.Points.Count);
    }

    /// <summary>
    /// A stroke somewhere else on the screen is a stroke somewhere else on the screen. A
    /// marker that jumped to the nearest text would be moving a mark the user placed
    /// deliberately.
    /// </summary>
    [TestMethod]
    public void SnapToText_LeavesAStrokeThatCrossedNoTextAlone()
    {
        var line = Line(("text", new CaptureRegion(100, 200, 300, 20)));
        var stroke = Stroke(from: (110, 600), to: (380, 600));

        Assert.AreSame(stroke, TextSnapping.SnapToText(stroke, [line]));
    }

    /// <summary>
    /// A tick beside a word is not a highlight of the line it happens to be level with.
    /// Without a floor on the overlap, the smallest twitch of the mouse would be stretched
    /// across a paragraph.
    /// </summary>
    [TestMethod]
    public void SnapToText_IgnoresAStrokeThatBarelyGrazesTheLine()
    {
        var line = Line(("text", new CaptureRegion(100, 200, 300, 20)));
        var stroke = Stroke(from: (96, 208), to: (102, 208));

        Assert.AreSame(stroke, TextSnapping.SnapToText(stroke, [line]));
    }

    /// <summary>
    /// Two lines are level with nothing between them but a few pixels, and the vertical
    /// slack that makes the option usable reaches into both. The one the stroke actually
    /// runs along has to win.
    /// </summary>
    [TestMethod]
    public void SnapToText_PicksTheLineTheStrokeRunsAlong()
    {
        var above = Line(("above", new CaptureRegion(100, 180, 300, 20)));
        var below = Line(("below", new CaptureRegion(100, 205, 300, 20)));
        var stroke = Stroke(from: (110, 214), to: (380, 214));

        var snapped = TextSnapping.SnapToText(stroke, [above, below]);

        Assert.IsTrue(
            snapped.Start.Y > 205,
            "The stroke snapped to the line above the one it was drawn on.");
    }

    private static Annotation Stroke((double X, double Y) from, (double X, double Y) to, double strokeWidth = 6) =>
        Annotation.Create(
            AnnotationTool.Marker,
            new CapturePoint(from.X, from.Y),
            new CapturePoint(to.X, to.Y),
            AnnotationStyle.Default with { StrokeWidth = strokeWidth }) with
        {
            Points = [new CapturePoint(from.X, from.Y), new CapturePoint(to.X, to.Y)],
        };

    private static RecognizedLine Line(params (string Text, CaptureRegion Bounds)[] words) =>
        new(words.Select(word => new RecognizedWord(word.Text, word.Bounds)));
}
