using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Annotations;

[TestClass]
public sealed class AnnotationFileTests
{
    private static AnnotationSprite Sprite(int width = 3, int height = 2)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index++)
        {
            pixels[index] = (byte)(index * 7 % 251);
        }

        return new AnnotationSprite(width, height, pixels);
    }

    [TestMethod]
    public void RoundTrip_KeepsEveryFieldAMarkIsMadeOf()
    {
        var group = Guid.NewGuid();
        var original = Annotation.Create(
            AnnotationTool.Arrow,
            new CapturePoint(12.5, 20),
            new CapturePoint(200, 33.25),
            new AnnotationStyle(
                new AnnotationColor(10, 20, 30, 128),
                7.5,
                LineStyle.Dashed,
                0.5,
                ArrowStyle.Double,
                12,
                CensorMode.Erase)
            {
                FontSize = 42,
                FontFamily = "Cascadia Code",
                Bold = true,
                Italic = true,
                Underline = true,
                Strikethrough = true,
                TextAlignment = LabelAlignment.Right,
                TextBackground = new AnnotationColor(1, 2, 3, 200),
                TextOutline = new AnnotationColor(4, 5, 6, 210),
                TextGlyphStroke = new AnnotationColor(7, 8, 9, 220),
                DimOpacity = 0.3,
                NumberFormat = NumberFormat.Roman,
                MeasureInPoints = true,
                LoupeMagnification = 3.5,
                LoupeSize = 180,
                StampSize = 96,
            }) with
        {
            Rotation = 0.75,
            Bend = -0.25,
            BendEnd = 0.4,
            GroupId = group,
            Text = "note",
            NumberValue = 4,
        };

        var restored = AnnotationFile.Read(AnnotationFile.Write([original]));

        // Every property, not a sample of them: a field left out of the writer is
        // exactly the failure this file exists to prevent, and it is invisible until
        // someone reopens a capture and finds a mark subtly wrong.
        Assert.AreEqual(1, restored.Count);
        Assert.AreEqual(original.Id, restored[0].Id);
        Assert.AreEqual(original.Tool, restored[0].Tool);
        Assert.AreEqual(original.Start, restored[0].Start);
        Assert.AreEqual(original.End, restored[0].End);
        Assert.AreEqual(original.Style.Color, restored[0].Style.Color);
        Assert.AreEqual(original.Style.StrokeWidth, restored[0].Style.StrokeWidth);
        Assert.AreEqual(original.Style.LineStyle, restored[0].Style.LineStyle);
        Assert.AreEqual(original.Style.Opacity, restored[0].Style.Opacity);
        Assert.AreEqual(original.Style.ArrowStyle, restored[0].Style.ArrowStyle);
        Assert.AreEqual(original.Style.CornerRadius, restored[0].Style.CornerRadius);
        Assert.AreEqual(original.Style.CensorMode, restored[0].Style.CensorMode);
        Assert.AreEqual(original.Style.FontSize, restored[0].Style.FontSize);
        Assert.AreEqual(original.Style.FontFamily, restored[0].Style.FontFamily);
        Assert.AreEqual(original.Style.Bold, restored[0].Style.Bold);
        Assert.AreEqual(original.Style.Italic, restored[0].Style.Italic);
        Assert.AreEqual(original.Style.Underline, restored[0].Style.Underline);
        Assert.AreEqual(original.Style.Strikethrough, restored[0].Style.Strikethrough);
        Assert.AreEqual(original.Style.TextAlignment, restored[0].Style.TextAlignment);
        Assert.AreEqual(original.Style.TextBackground, restored[0].Style.TextBackground);
        Assert.AreEqual(original.Style.TextOutline, restored[0].Style.TextOutline);
        Assert.AreEqual(original.Style.TextGlyphStroke, restored[0].Style.TextGlyphStroke);
        Assert.AreEqual(original.Style.DimOpacity, restored[0].Style.DimOpacity);
        Assert.AreEqual(original.Rotation, restored[0].Rotation);
        Assert.AreEqual(original.Bend, restored[0].Bend);
        Assert.AreEqual(original.BendEnd, restored[0].BendEnd);
        Assert.AreEqual(group, restored[0].GroupId);
        Assert.AreEqual("note", restored[0].Text);
        Assert.AreEqual(4, restored[0].NumberValue);
        Assert.AreEqual(original.Style.NumberFormat, restored[0].Style.NumberFormat);
        Assert.AreEqual(original.Style.MeasureInPoints, restored[0].Style.MeasureInPoints);
        Assert.AreEqual(original.Style.LoupeMagnification, restored[0].Style.LoupeMagnification);
        Assert.AreEqual(original.Style.LoupeSize, restored[0].Style.LoupeSize);
        Assert.AreEqual(original.Style.StampSize, restored[0].Style.StampSize);
    }

    [TestMethod]
    public void RoundTrip_KeepsEverySampleOfAFreeformStroke()
    {
        var points = Enumerable.Range(0, 64).Select(step => new CapturePoint(step, step * 1.5)).ToArray();
        var stroke = Annotation.CreateFreeform(AnnotationTool.Pencil, points);

        var restored = AnnotationFile.Read(AnnotationFile.Write([stroke]));

        CollectionAssert.AreEqual(points, restored[0].Points.ToArray());
    }

    /// <summary>
    /// Pressure is one number per sample, and a file that kept the samples but not the
    /// weights would reopen a pen stroke as an even line — the same silent loss the whole
    /// field-by-field test above exists to prevent.
    /// </summary>
    [TestMethod]
    public void RoundTrip_KeepsThePressureOfAPenStroke()
    {
        var points = Enumerable.Range(0, 8).Select(step => new CapturePoint(step, step)).ToArray();
        var weights = Enumerable.Range(0, 8).Select(step => (step + 1) / 8.0).ToArray();
        var stroke = Annotation.CreateFreeform(AnnotationTool.Pencil, points, pressures: weights);

        var restored = AnnotationFile.Read(AnnotationFile.Write([stroke]));

        CollectionAssert.AreEqual(weights, restored[0].Pressures.ToArray());
    }

    [TestMethod]
    public void RoundTrip_KeepsASpritesPixelsExactly()
    {
        var sprite = Sprite(5, 4);
        var badge = Annotation.CreateSprite(AnnotationTool.Number, new CapturePoint(4, 8), sprite);

        var restored = AnnotationFile.Read(AnnotationFile.Write([badge]));

        // The pixels are what the mark says. A sprite re-rasterized from the text would
        // depend on the fonts of whatever machine reopened it; these do not.
        var stored = restored[0].Sprite;
        Assert.IsNotNull(stored);
        Assert.AreEqual(sprite.Width, stored.Width);
        Assert.AreEqual(sprite.Height, stored.Height);
        CollectionAssert.AreEqual(sprite.Pixels.ToArray(), stored.Pixels.ToArray());
    }

    [TestMethod]
    public void Write_CompressesASpriteRatherThanStoringItsBytesOutright()
    {
        // A glyph sprite is mostly transparent, and the history keeps one file per
        // capture: uncompressed base64 would make the notes larger than the screenshot.
        var blank = new AnnotationSprite(64, 64, new byte[64 * 64 * 4]);
        var written = AnnotationFile.Write(
            [Annotation.CreateSprite(AnnotationTool.Stamp, new CapturePoint(0, 0), blank)]);

        Assert.IsTrue(written.Length < 64 * 64 * 4 / 4, $"the document is {written.Length} bytes");
    }

    [TestMethod]
    public void Read_KeepsTheShapeOfALineBentBeforeThereWereTwoBends()
    {
        // A lone stored bend meant the offset of the curve's own middle. Read as one of a
        // symmetric pair it would describe a curve an eighth deeper, so a capture reopened
        // from the history would come back with a mark subtly different from the one that
        // was saved — the exact failure this file exists to prevent.
        const string Document =
            """
            {"version":1,"annotations":[{"id":"00000000-0000-0000-0000-000000000001",
            "tool":"Line","startX":0,"startY":0,"endX":90,"endY":0,
            "color":"#FF000000","strokeWidth":3,"lineStyle":"Solid","opacity":1,
            "arrowStyle":"Filled","cornerRadius":0,"bend":0.45}]}
            """;

        var restored = AnnotationFile.Read(Document);

        Assert.AreEqual(1, restored.Count);
        Assert.AreEqual(restored[0].Bend, restored[0].BendEnd, 1e-12, "a lone bend was symmetric");

        // A symmetric pair carries the middle to 1.125 times each bend, so the middle is
        // back where the saved 0.45 of the length put it.
        Assert.AreEqual(0.45, restored[0].Bend * 1.125, 1e-12);
    }

    [TestMethod]
    public void Read_DropsAnAnnotationNamingAToolThisVersionDoesNotHave()
    {
        var document = AnnotationFile.Write(
            [Annotation.Create(AnnotationTool.Rectangle, new CapturePoint(0, 0), new CapturePoint(10, 10))]);

        var restored = AnnotationFile.Read(document.Replace("\"Rectangle\"", "\"Hyperbola\"", StringComparison.Ordinal));

        Assert.AreEqual(0, restored.Count);
    }

    [TestMethod]
    public void Read_DropsOnlyTheUnreadableMarkAndKeepsTheRest()
    {
        var document = AnnotationFile.Write(
        [
            Annotation.Create(AnnotationTool.Rectangle, new CapturePoint(0, 0), new CapturePoint(10, 10)),
            Annotation.Create(AnnotationTool.Ellipse, new CapturePoint(1, 1), new CapturePoint(9, 9)),
        ]);

        var restored = AnnotationFile.Read(document.Replace("\"Rectangle\"", "\"Hyperbola\"", StringComparison.Ordinal));

        Assert.AreEqual(1, restored.Count);
        Assert.AreEqual(AnnotationTool.Ellipse, restored[0].Tool);
    }

    [TestMethod]
    public void Read_DropsASpriteToolThatArrivedWithoutItsPixels()
    {
        // Reconstructed by hand: a text annotation with no sprite draws nothing but
        // still hit tests, which is a mark the user cannot see and cannot get rid of.
        const string Document =
            """
            {"version":1,"annotations":[{"id":"00000000-0000-0000-0000-000000000001",
            "tool":"Text","startX":0,"startY":0,"endX":10,"endY":10,
            "color":"#FF000000","strokeWidth":3,"lineStyle":"Solid","opacity":1,
            "arrowStyle":"Filled","cornerRadius":0}]}
            """;

        Assert.AreEqual(0, AnnotationFile.Read(Document).Count);
    }

    [TestMethod]
    public void Read_GivesASpotlightFromBeforeItHadAStrengthTheOneMacshotStartsWith()
    {
        // A file written before the spotlight existed says nothing about its dim, and
        // nothing reads back as zero — which is no dim at all, so the mark would reopen
        // as a bare rectangle rather than as the spotlight it was saved as.
        const string Document =
            """
            {"version":1,"annotations":[{"id":"00000000-0000-0000-0000-000000000001",
            "tool":"Highlight","startX":0,"startY":0,"endX":10,"endY":10,
            "color":"#FF000000","strokeWidth":3,"lineStyle":"Dashed","opacity":1,
            "arrowStyle":"Filled","cornerRadius":0}]}
            """;

        var restored = AnnotationFile.Read(Document);

        Assert.AreEqual(AnnotationStyle.DefaultDimOpacity, restored[0].Style.DimOpacity);
    }

    /// <summary>
    /// A label saved before the row could align one reopens hung from the edge it was
    /// typed at, rather than moving the moment it is reloaded.
    /// </summary>
    /// <remarks>
    /// The alignment is stored by name, so a file that is silent about it hands back an
    /// empty string. Parsed strictly that is not a member of the enum, and the fallback has
    /// to be the left edge specifically: it is where an unaligned label already sat, so the
    /// repair and what the user saved agree. Any other answer would shuffle the lines of
    /// every multi-line label in the history the first time it was opened.
    /// </remarks>
    [TestMethod]
    public void Read_LeavesALabelFromBeforeAlignmentWhereItWasTyped()
    {
        const string Document =
            """
            {"version":1,"annotations":[{"id":"00000000-0000-0000-0000-000000000001",
            "tool":"Rectangle","startX":0,"startY":0,"endX":10,"endY":10,
            "color":"#FF000000","strokeWidth":3,"lineStyle":"Solid","opacity":1,
            "arrowStyle":"Filled","cornerRadius":0}]}
            """;

        var restored = AnnotationFile.Read(Document);

        Assert.AreEqual(LabelAlignment.Left, restored[0].Style.TextAlignment);

        // And the line round the glyphs stays off rather than arriving white: a label that
        // grew an outline nobody asked for is a worse answer than one that lost it.
        Assert.IsNull(restored[0].Style.TextGlyphStroke);
    }

    [TestMethod]
    public void Read_AnswersNothingForADocumentFromALaterVersion()
    {
        var document = AnnotationFile.Write(
            [Annotation.Create(AnnotationTool.Line, new CapturePoint(0, 0), new CapturePoint(4, 4))]);

        // Better than a partial read: a later version's meaning for a field this one
        // understands is not knowable, and a mark placed wrongly is worse than none.
        Assert.AreEqual(0, AnnotationFile.Read(document.Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal)).Count);
    }

    [TestMethod]
    public void Read_AnswersNothingForRubbish()
    {
        Assert.AreEqual(0, AnnotationFile.Read(null).Count);
        Assert.AreEqual(0, AnnotationFile.Read(string.Empty).Count);
        Assert.AreEqual(0, AnnotationFile.Read("not json at all").Count);
        Assert.AreEqual(0, AnnotationFile.Read("{\"version\":1}").Count);
    }
}
