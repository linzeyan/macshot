using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Recognition;

namespace Macshot.Windows.Core.Tests.Recognition;

/// <summary>
/// Matching a translation back to the words it replaces, and sizing it to fit them.
/// </summary>
[TestClass]
public sealed class TranslationOverlayTests
{
    [TestMethod]
    public void Ask_SendsOneRequestWithTheLinesInReadingOrder()
    {
        var (lines, request) = TranslationOverlay.Ask([Line("first", 0), Line("second", 20)]);

        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual("first\nsecond", request);
    }

    [TestMethod]
    public void Ask_DropsBlankLinesRatherThanSendingThem()
    {
        // A rule or a border comes back from OCR as a line with nothing in it. Sent, it
        // translates to nothing and shifts every line after it out of step with its
        // answer — which puts each translation over the wrong words.
        var (lines, request) = TranslationOverlay.Ask([Line("first", 0), Line("   ", 20), Line("third", 40)]);

        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual("first\nthird", request);
    }

    [TestMethod]
    public void Pair_PutsEachTranslationOverTheLineItCameFrom()
    {
        var asked = new[] { Line("cat", 0), Line("dog", 20) };

        var paired = TranslationOverlay.Pair(asked, "chat\nchien");

        Assert.IsNotNull(paired);
        Assert.AreEqual(2, paired.Count);
        Assert.AreEqual("chat", paired[0].Text);
        Assert.AreEqual("chien", paired[1].Text);
        Assert.AreEqual(20 - TranslationOverlay.Padding, paired[1].Bounds.Y, 1e-9);
    }

    [TestMethod]
    public void Pair_RefusesAnAnswerWithADifferentNumberOfLines()
    {
        // The only defence against silently placing every translation over the wrong
        // words: a service that merged two lines or broke one in half gives an answer
        // that looks perfectly fine and is wrong from that point down.
        var asked = new[] { Line("cat", 0), Line("dog", 20) };

        Assert.IsNull(TranslationOverlay.Pair(asked, "chat chien"));
        Assert.IsNull(TranslationOverlay.Pair(asked, "chat\nchien\nsouris"));
        Assert.IsNull(TranslationOverlay.Pair(asked, null));
    }

    [TestMethod]
    public void Pair_LeavesALineTheServiceHadNothingToSayAboutShowing()
    {
        // A blank box over the original is less use than the original.
        var asked = new[] { Line("cat", 0), Line("...", 20) };

        var paired = TranslationOverlay.Pair(asked, "chat\n   ");

        Assert.IsNotNull(paired);
        Assert.AreEqual(1, paired.Count);
        Assert.AreEqual("chat", paired[0].Text);
    }

    [TestMethod]
    public void TheBoxOverhangsTheWordsItCovers()
    {
        // Without the overhang the antialiased edge of the original text shows round the
        // replacement, which reads as the translation being slightly out of register.
        var paired = TranslationOverlay.Pair([Line("cat", 10)], "chat");

        Assert.IsNotNull(paired);
        var box = paired[0].Bounds;
        Assert.AreEqual(-TranslationOverlay.Padding, box.X, 1e-9);
        Assert.AreEqual(10 - TranslationOverlay.Padding, box.Y, 1e-9);
        Assert.AreEqual(40 + (TranslationOverlay.Padding * 2), box.Width, 1e-9);
        Assert.AreEqual(12 + (TranslationOverlay.Padding * 2), box.Height, 1e-9);
    }

    [TestMethod]
    public void TypeIsSetBelowTheBoxRatherThanFillingIt()
    {
        // An OCR box is drawn round the glyphs' extremes — an ascender on one word, a
        // descender on another — so type set to the full height stands taller than the
        // words it replaces.
        Assert.AreEqual(13, TranslationOverlay.FontSizeFor(20), 1e-9);

        // And never so small that the translation is a smudge hiding the original.
        Assert.AreEqual(TranslationOverlay.MinimumFontSize, TranslationOverlay.FontSizeFor(4), 1e-9);
    }

    /// <summary>One line of three words, 40 wide and 12 tall, at the given row.</summary>
    private static RecognizedLine Line(string text, double y) =>
        new([new RecognizedWord(text, new CaptureRegion(0, y, 40, 12))]);
}
