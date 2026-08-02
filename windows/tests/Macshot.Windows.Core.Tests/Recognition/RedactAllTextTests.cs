using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Recognition;

namespace Macshot.Windows.Core.Tests.Recognition;

[TestClass]
public sealed class RedactAllTextTests
{
    private static readonly CaptureRegion Everything = new(0, 0, 1000, 1000);

    /// <summary>
    /// The difference the option exists for: blacking out a panel loses the layout that
    /// says what was there, and blacking out the words on it keeps the shape while making
    /// it unreadable.
    /// </summary>
    [TestMethod]
    public void RedactAllText_CoversEveryLineAndNothingBetweenThem()
    {
        var first = Line(("first", new CaptureRegion(100, 100, 200, 20)));
        var second = Line(("second", new CaptureRegion(100, 200, 200, 20)));

        var boxes = AutoRedactor.RedactAllText([first, second], Everything);

        Assert.AreEqual(2, boxes.Count);
        Assert.IsTrue(
            boxes[0].BoundingRect.Bottom < boxes[1].BoundingRect.Y,
            "The two redactions ran together into one block.");
    }

    /// <summary>
    /// The region the user dragged is their statement about how far they meant to go. A
    /// line outside it was not part of the gesture, and covering it would be redacting
    /// something they did not ask about.
    /// </summary>
    [TestMethod]
    public void RedactAllText_SkipsTextOutsideTheRegion()
    {
        var inside = Line(("inside", new CaptureRegion(110, 110, 100, 20)));
        var outside = Line(("outside", new CaptureRegion(500, 500, 100, 20)));

        var boxes = AutoRedactor.RedactAllText([inside, outside], new CaptureRegion(100, 100, 200, 200));

        Assert.AreEqual(1, boxes.Count);
        Assert.IsTrue(boxes[0].BoundingRect.X < 200, "The wrong line was covered.");
    }

    /// <summary>
    /// A box that covered half a sentence is not a redaction, it is a redaction with the
    /// rest of the sentence beside it.
    /// </summary>
    [TestMethod]
    public void RedactAllText_SkipsALineTheRegionOnlyHalfCovers()
    {
        var straddling = Line(("straddling", new CaptureRegion(150, 110, 400, 20)));

        var boxes = AutoRedactor.RedactAllText([straddling], new CaptureRegion(100, 100, 200, 200));

        Assert.AreEqual(0, boxes.Count);
    }

    /// <summary>
    /// OCR boxes hug the glyphs. An exact box leaves ascenders and antialiased edges
    /// showing, which is enough to read a short word back off a redaction.
    /// </summary>
    [TestMethod]
    public void RedactAllText_CoversMoreThanTheGlyphsThemselves()
    {
        var line = Line(("secret", new CaptureRegion(100, 100, 200, 20)));

        var bounds = AutoRedactor.RedactAllText([line], Everything)[0].BoundingRect;

        Assert.IsTrue(bounds.Y < 100 && bounds.Bottom > 120, "The redaction does not clear the glyphs.");
    }

    /// <summary>
    /// One drag produced them, so one Ctrl+Z has to take them all away. Without a shared
    /// group the user undoes a redaction one line at a time, and a half-undone one is
    /// worse than either state.
    /// </summary>
    [TestMethod]
    public void RedactAllText_GroupsTheBoxesSoOneUndoTakesThemAll()
    {
        var first = Line(("first", new CaptureRegion(100, 100, 200, 20)));
        var second = Line(("second", new CaptureRegion(100, 200, 200, 20)));

        var boxes = AutoRedactor.RedactAllText([first, second], Everything);

        Assert.IsNotNull(boxes[0].GroupId);
        Assert.AreEqual(boxes[0].GroupId, boxes[1].GroupId);
    }

    /// <summary>
    /// The style comes from the drag that asked for it, so a user who chose Pixelate gets
    /// pixelated text rather than the solid black the unattended auto-redact uses.
    /// </summary>
    [TestMethod]
    public void RedactAllText_KeepsTheModeTheUserWasDrawingWith()
    {
        var line = Line(("secret", new CaptureRegion(100, 100, 200, 20)));
        var style = AnnotationStyle.Default with { CensorMode = CensorMode.Pixelate };

        var boxes = AutoRedactor.RedactAllText([line], Everything, style);

        Assert.AreEqual(CensorMode.Pixelate, boxes[0].Style.CensorMode);
        Assert.AreEqual(AnnotationTool.Censor, boxes[0].Tool);
    }

    private static RecognizedLine Line(params (string Text, CaptureRegion Bounds)[] words) =>
        new(words.Select(word => new RecognizedWord(word.Text, word.Bounds)));
}
