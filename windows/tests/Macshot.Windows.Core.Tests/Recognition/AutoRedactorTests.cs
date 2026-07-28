using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Recognition;

namespace Macshot.Windows.Core.Tests.Recognition;

[TestClass]
public sealed class AutoRedactorTests
{
    [TestMethod]
    public void Redact_CoversOnlyTheWordsTheSecretIsIn()
    {
        var line = Line(
            ("contact", new CaptureRegion(0, 0, 70, 20)),
            ("bob@example.com", new CaptureRegion(80, 0, 150, 20)),
            ("now", new CaptureRegion(240, 0, 40, 20)));

        var annotations = AutoRedactor.Redact([line]);

        Assert.AreEqual(1, annotations.Count);
        var bounds = annotations[0].BoundingRect;
        Assert.IsTrue(bounds.X < 80 && bounds.Right > 230, "The email is not fully covered.");
        Assert.IsTrue(bounds.X > 70, "The redaction spilled onto the preceding word.");
        Assert.IsTrue(bounds.Right < 240, "The redaction spilled onto the following word.");
    }

    /// <summary>
    /// OCR splits on spaces, so a phone number arrives as several words while the
    /// pattern only matches across the joined line. Covering one word would leave
    /// most of the number readable.
    /// </summary>
    [TestMethod]
    public void Redact_CoversEveryWordASecretSpans()
    {
        var line = Line(
            ("+1", new CaptureRegion(0, 0, 20, 20)),
            ("(555)", new CaptureRegion(30, 0, 50, 20)),
            ("123-4567", new CaptureRegion(90, 0, 90, 20)));

        var annotations = AutoRedactor.Redact([line]);

        Assert.AreEqual(1, annotations.Count);
        var bounds = annotations[0].BoundingRect;
        Assert.IsTrue(bounds.X < 0 && bounds.Right > 180, "The whole number is not covered.");
    }

    /// <summary>
    /// A redaction that leaves the tops of the letters showing is not a redaction,
    /// and OCR boxes hug the glyphs.
    /// </summary>
    [TestMethod]
    public void Redact_PadsBeyondTheRecognizedBox()
    {
        var line = Line(("bob@example.com", new CaptureRegion(100, 100, 150, 20)));

        var bounds = AutoRedactor.Redact([line])[0].BoundingRect;

        Assert.IsTrue(bounds.Y < 100 && bounds.Bottom > 120);
    }

    [TestMethod]
    public void Redact_MarksOneRunAsOneGroupSoItUndoesTogether()
    {
        var lines = new[]
        {
            Line(("bob@example.com", new CaptureRegion(0, 0, 150, 20))),
            Line(("10.0.0.1", new CaptureRegion(0, 40, 80, 20))),
        };

        var annotations = AutoRedactor.Redact(lines);

        Assert.AreEqual(2, annotations.Count);
        Assert.IsNotNull(annotations[0].GroupId);
        Assert.AreEqual(annotations[0].GroupId, annotations[1].GroupId);
    }

    /// <summary>
    /// The patterns overlap on purpose, so the same words can match twice. Stacking
    /// identical boxes would only cost undo steps.
    /// </summary>
    [TestMethod]
    public void Redact_DoesNotStackIdenticalBoxes()
    {
        var line = Line(("123-45-6789", new CaptureRegion(0, 0, 120, 20)));

        var annotations = AutoRedactor.Redact([line]);

        Assert.AreEqual(1, annotations.Count);
    }

    [TestMethod]
    public void Redact_ProducesNothingWhenThereIsNoSecret()
    {
        var line = Line(("the", new CaptureRegion(0, 0, 30, 20)), ("fox", new CaptureRegion(40, 0, 30, 20)));

        Assert.AreEqual(0, AutoRedactor.Redact([line]).Count);
    }

    /// <summary>
    /// A translucent drawing colour would produce boxes you can read through, so the
    /// redactor supplies its own style rather than inheriting the toolbar's.
    /// </summary>
    [TestMethod]
    public void DefaultStyle_IsOpaque()
    {
        Assert.AreEqual(byte.MaxValue, AutoRedactor.DefaultStyle.Color.Alpha);
        Assert.AreEqual(1, AutoRedactor.DefaultStyle.Opacity);
    }

    [TestMethod]
    public void Redact_UsesTheFilledRectangleToolSoItRendersLikeAnyOtherMark()
    {
        var line = Line(("bob@example.com", new CaptureRegion(0, 0, 150, 20)));

        Assert.AreEqual(AnnotationTool.FilledRectangle, AutoRedactor.Redact([line])[0].Tool);
    }

    private static RecognizedLine Line(params (string Text, CaptureRegion Bounds)[] words) =>
        new(words.Select(word => new RecognizedWord(word.Text, word.Bounds)));
}
