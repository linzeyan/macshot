using Macshot.Windows.Core.Recognition;

namespace Macshot.Windows.Core.Tests.Recognition;

[TestClass]
public sealed class TranslationTests
{
    [TestMethod]
    public void Read_SurvivesABodyThatIsNotTheResponseAtAll()
    {
        // An HTML page is the ordinary way this endpoint refuses a caller it thinks is
        // asking too often, and it must not end as an exception over a capture the user
        // already has.
        foreach (var body in new[] { null, "", "   ", "<html>gateway timeout</html>", "{}", "[]" })
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
    public void Read_JoinsEverySentenceTheServiceSplitTheTextInto()
    {
        // The keyless endpoint breaks a paragraph into sentences and answers with one
        // entry each. Taking only the first would truncate anything past a line, and
        // nothing about the shape of the response says so.
        const string Body = """
            [[["Hello there. ","Hallo da. ",null,null,10],["How are you?","Wie geht es dir?",null,null,3]],null,"de"]
            """;

        var outcome = TranslationResponse.Read(Body);

        Assert.IsTrue(outcome.Succeeded);
        Assert.AreEqual("Hello there. How are you?", outcome.Text);
    }

    [TestMethod]
    public void Read_SkipsTheTrailingEntriesThatAreNotTheTranslation()
    {
        const string Body = """
            [[["Cat","Katze",null,null,10],[null,null,null,null,null]],null,"de",null,null,[["Katze"]]]
            """;

        Assert.AreEqual("Cat", TranslationResponse.Read(Body).Text);
    }

    [TestMethod]
    public void Read_TurnsAnHtmlRefusalIntoASentence()
    {
        // What a rate-limited caller gets back from an undocumented endpoint: a page,
        // not JSON. It has to end as a message in the window rather than as a throw
        // over recognized text the user already has.
        var outcome = TranslationResponse.Read("<html><body>429</body></html>");

        Assert.IsFalse(outcome.Succeeded);
        Assert.IsNotNull(outcome.Failure);
    }

    [TestMethod]
    public void Read_AnswersFailureForAnEmptyTranslation()
    {
        Assert.IsFalse(TranslationResponse.Read("[[],null,\"de\"]").Succeeded);
        Assert.IsFalse(TranslationResponse.Read(null).Succeeded);
    }
}
