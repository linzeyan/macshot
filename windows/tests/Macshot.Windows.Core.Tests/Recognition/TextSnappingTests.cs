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
    /// to be on, the mark would either miss half the glyphs or swallow the line above —
    /// and the line's height stored as the width, rather than a sixth of it, is drawn six
    /// times over and swallows several.
    /// </summary>
    [TestMethod]
    public void SnapToText_ThickensTheStrokeToCoverTheText()
    {
        var line = Line(("text", new CaptureRegion(100, 200, 300, 20)));
        var stroke = Stroke(from: (110, 208), to: (380, 208), strokeWidth: 3);

        var snapped = TextSnapping.SnapToText(stroke, [line]);

        Assert.AreEqual(24, snapped.InkWidth, 1e-9, "the line's 20 and its padding, as drawn");
    }

    /// <summary>
    /// The hand-drawn samples have to go with the ends. Left behind, the marker would draw
    /// its original wobbly path and the snap would appear to have done nothing at all —
    /// which is exactly how this failed before the samples were replaced. Replaced by the
    /// new ends rather than emptied, so the result is still a freehand stroke and offers
    /// no grip that would reshape it into something else.
    /// </summary>
    [TestMethod]
    public void SnapToText_ReplacesTheHandDrawnPathWithTheStraightOne()
    {
        var line = Line(("text", new CaptureRegion(100, 200, 300, 20)));
        var stroke = Stroke(from: (110, 204), to: (380, 214));

        var snapped = TextSnapping.SnapToText(stroke, [line]);

        CollectionAssert.AreEqual(new[] { snapped.Start, snapped.End }, snapped.Points.ToArray());
        Assert.AreEqual(0, AnnotationHandles.For(snapped).Count);
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

    /// <summary>
    /// The padding over the text is macshot's 4 points, so on a 200% screen it is 8 frame
    /// pixels — the band clears the ascenders there by as much as it does at 100%.
    /// </summary>
    [TestMethod]
    public void SnapToText_PadsTheTextInPointsOfTheSurfaceDrawnOn()
    {
        var line = Line(("text", new CaptureRegion(100, 200, 300, 20)));
        var stroke = Stroke(from: (110, 208), to: (380, 208));
        stroke = stroke with { Style = stroke.Style with { PixelsPerPoint = 2 } };

        var snapped = TextSnapping.SnapToText(stroke, [line]);

        Assert.AreEqual(28, snapped.InkWidth, 1e-9);
    }

    /// <summary>
    /// A press is aimed at a word, so the line nearest it is the one meant, even with
    /// another close by; and a press in clear space has no line to be sized to, which has
    /// to say so rather than pick one, or the highlighter would take the height of text
    /// across the screen.
    /// </summary>
    [TestMethod]
    public void StrokeWidthAt_TakesTheLineNearestThePress()
    {
        IReadOnlyList<RecognizedLine> lines =
        [
            Line(("upper", new CaptureRegion(100, 200, 300, 12))),
            Line(("lower", new CaptureRegion(100, 214, 300, 32))),
        ];

        Assert.AreEqual(6, TextSnapping.StrokeWidthAt(new CapturePoint(150, 236), lines, 1) ?? 0, 1e-9);
        Assert.AreEqual(16d / 6, TextSnapping.StrokeWidthAt(new CapturePoint(95, 205), lines, 1) ?? 0, 1e-9);
        Assert.IsNull(TextSnapping.StrokeWidthAt(new CapturePoint(150, 400), lines, 1));
        Assert.IsNull(TextSnapping.StrokeWidthAt(new CapturePoint(50, 205), lines, 1));
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
