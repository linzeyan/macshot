using Macshot.Windows.Core.Recognition;

namespace Macshot.Windows.Core.Tests.Recognition;

[TestClass]
public sealed class TranslationTests
{
    [TestMethod]
    public void Read_TakesTheTranslationOutOfTheBody()
    {
        var outcome = TranslationResponse.Read(
            """{"data":{"translations":[{"translatedText":"Hello"}]}}""");

        Assert.IsTrue(outcome.Succeeded);
        Assert.AreEqual("Hello", outcome.Text);
    }

    [TestMethod]
    public void Read_DecodesTheEntitiesTheServiceEscapes()
    {
        // The v2 endpoint escapes quotes and ampersands even when asked for plain
        // text, so a line with an apostrophe in it arrives carrying &#39; and would
        // otherwise be pasted that way.
        var outcome = TranslationResponse.Read(
            """{"data":{"translations":[{"translatedText":"it&#39;s Tom &amp; Jerry"}]}}""");

        Assert.AreEqual("it's Tom & Jerry", outcome.Text);
    }

    [TestMethod]
    public void Read_PassesTheServiceSOwnMessageOn()
    {
        // Worth quoting verbatim: "API key not valid" sends the user to the right
        // place, where a generic failure would send them to the network settings.
        var outcome = TranslationResponse.Read(
            """{"error":{"code":400,"message":"API key not valid. Please pass a valid API key."}}""");

        Assert.IsFalse(outcome.Succeeded);
        Assert.AreEqual("API key not valid. Please pass a valid API key.", outcome.Failure);
    }

    [TestMethod]
    public void Read_SurvivesABodyThatIsNotTheResponseAtAll()
    {
        // An HTML error page from a proxy is the ordinary case here, and it must not
        // end as an exception over a capture the user already has.
        foreach (var body in new[] { null, "", "   ", "<html>gateway timeout</html>", "{}", """{"data":{"translations":[]}}""" })
        {
            var outcome = TranslationResponse.Read(body);

            Assert.IsFalse(outcome.Succeeded, body ?? "null");
            Assert.IsFalse(string.IsNullOrWhiteSpace(outcome.Failure), body ?? "null");
        }
    }

    [TestMethod]
    public void Normalize_KeepsALanguageThatIsOffered()
    {
        Assert.AreEqual("zh-TW", TranslationLanguages.Normalize("zh-tw"));
        Assert.AreEqual("ja", TranslationLanguages.Normalize(" ja "));
    }

    [TestMethod]
    public void Normalize_FallsBackRatherThanTranslatingIntoNothing()
    {
        // A code the table does not hold would be sent to the service and refused, so
        // the capture would produce an error instead of a translation.
        Assert.AreEqual("en", TranslationLanguages.Normalize("klingon"));
        Assert.AreEqual("en", TranslationLanguages.Normalize(null));
        Assert.AreEqual("en", TranslationLanguages.Normalize(""));
    }

    [TestMethod]
    public void IndexOf_PointsThePickerAtTheStoredLanguage()
    {
        var index = TranslationLanguages.IndexOf("de");

        Assert.AreEqual("de", TranslationLanguages.All[index].Code);
    }

    [TestMethod]
    public void Languages_DistinguishTheTwoWrittenChineses()
    {
        // Not a detail of one choice for anyone who reads one of them.
        Assert.IsTrue(TranslationLanguages.All.Any(language => language.Code == "zh-CN"));
        Assert.IsTrue(TranslationLanguages.All.Any(language => language.Code == "zh-TW"));
    }

    [TestMethod]
    public void ReadFree_JoinsEverySentenceTheServiceSplitTheTextInto()
    {
        // The keyless endpoint breaks a paragraph into sentences and answers with one
        // entry each. Taking only the first would truncate anything past a line, and
        // nothing about the shape of the response says so.
        const string Body = """
            [[["Hello there. ","Hallo da. ",null,null,10],["How are you?","Wie geht es dir?",null,null,3]],null,"de"]
            """;

        var outcome = TranslationResponse.ReadFree(Body);

        Assert.IsTrue(outcome.Succeeded);
        Assert.AreEqual("Hello there. How are you?", outcome.Text);
    }

    [TestMethod]
    public void ReadFree_SkipsTheTrailingEntriesThatAreNotTheTranslation()
    {
        const string Body = """
            [[["Cat","Katze",null,null,10],[null,null,null,null,null]],null,"de",null,null,[["Katze"]]]
            """;

        Assert.AreEqual("Cat", TranslationResponse.ReadFree(Body).Text);
    }

    [TestMethod]
    public void ReadFree_TurnsAnHtmlRefusalIntoASentence()
    {
        // What a rate-limited caller gets back from an undocumented endpoint: a page,
        // not JSON. It has to end as a message in the window rather than as a throw
        // over recognized text the user already has.
        var outcome = TranslationResponse.ReadFree("<html><body>429</body></html>");

        Assert.IsFalse(outcome.Succeeded);
        Assert.IsNotNull(outcome.Failure);
    }

    [TestMethod]
    public void ReadFree_AnswersFailureForAnEmptyTranslation()
    {
        Assert.IsFalse(TranslationResponse.ReadFree("[[],null,\"de\"]").Succeeded);
        Assert.IsFalse(TranslationResponse.ReadFree(null).Succeeded);
    }
}
