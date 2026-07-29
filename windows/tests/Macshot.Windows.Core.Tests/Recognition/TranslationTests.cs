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
}
