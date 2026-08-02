using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class UrlSchemeCommandsTests
{
    [TestMethod]
    public void Parse_ReadsACommandThatTakesNothing()
    {
        var command = UrlSchemeCommands.Parse("macshot://capture");

        Assert.IsNotNull(command);
        Assert.AreEqual(UrlSchemeAction.Capture, command.Action);
        Assert.IsNull(command.Argument);
    }

    [TestMethod]
    public void Parse_ReadsTheWholeTable()
    {
        // Every command the settings window lists has to be one the app answers, or the
        // list is documenting something that does nothing.
        foreach (var entry in UrlSchemeCommands.All)
        {
            var command = UrlSchemeCommands.Parse(entry.Text);

            Assert.IsNotNull(command, entry.Text);
            Assert.AreEqual(entry.Action, command.Action, entry.Text);
        }
    }

    [TestMethod]
    public void Parse_KeepsAWindowsPathWhole()
    {
        // The one argument most likely to arrive: a path with a drive letter and
        // backslashes, which is what "Open an image file in the editor" is given.
        var command = UrlSchemeCommands.Parse(@"macshot://open?file=C:\Users\me\shot 1.png");

        Assert.IsNotNull(command);
        Assert.AreEqual(UrlSchemeAction.Open, command.Action);
        Assert.AreEqual(@"C:\Users\me\shot 1.png", command.Argument);
    }

    [TestMethod]
    public void Parse_UnescapesAnEncodedPath()
    {
        // Whoever writes the URL is entitled to escape it, and a launcher that does will
        // send %20 for the space rather than the space.
        var command = UrlSchemeCommands.Parse("macshot://open?file=C%3A%5CUsers%5Cme%5Cshot%201.png");

        Assert.IsNotNull(command);
        Assert.AreEqual(@"C:\Users\me\shot 1.png", command.Argument);
    }

    [TestMethod]
    public void Parse_TakesTheLanguageOffATranslateCommand()
    {
        var command = UrlSchemeCommands.Parse("macshot://ocr-translate?target=zh-CN");

        Assert.IsNotNull(command);
        Assert.AreEqual(UrlSchemeAction.OcrTranslate, command.Action);
        Assert.AreEqual("zh-CN", command.Argument);
    }

    [TestMethod]
    public void Parse_TreatsAnEmptyArgumentAsNoneAtAll()
    {
        // macshot's own reading of ?target=: blank means the saved default language,
        // not a language with no name.
        var command = UrlSchemeCommands.Parse("macshot://ocr-translate?target=");

        Assert.IsNotNull(command);
        Assert.IsNull(command.Argument);
    }

    [TestMethod]
    public void Parse_IgnoresAParameterTheCommandDoesNotRead()
    {
        var command = UrlSchemeCommands.Parse("macshot://capture?file=C:\\x.png");

        Assert.IsNotNull(command);
        Assert.IsNull(command.Argument);
    }

    [TestMethod]
    public void Parse_AnswersNothingToACommandThisBuildDoesNotHave()
    {
        // A URL written for a later version. Ignored rather than guessed at, which is
        // what stops "macshot://teleport" from starting an ordinary capture.
        Assert.IsNull(UrlSchemeCommands.Parse("macshot://teleport"));
    }

    [TestMethod]
    public void Parse_AnswersNothingToWhatIsNotOneOfTheseUrls()
    {
        Assert.IsNull(UrlSchemeCommands.Parse(null));
        Assert.IsNull(UrlSchemeCommands.Parse(string.Empty));
        Assert.IsNull(UrlSchemeCommands.Parse(@"C:\Users\me\shot.png"));
        Assert.IsNull(UrlSchemeCommands.Parse("https://example.com/capture"));
    }

    [TestMethod]
    public void IsCommandUrl_SeparatesAMessengerFromASecondMacshot()
    {
        // What the launching process decides on: a URL is handed to the copy already
        // running, a file is not, and getting it wrong ends the launch with the file
        // unopened.
        Assert.IsTrue(UrlSchemeCommands.IsCommandUrl("macshot://capture"));
        Assert.IsTrue(UrlSchemeCommands.IsCommandUrl("MACSHOT://Capture"));
        Assert.IsFalse(UrlSchemeCommands.IsCommandUrl(@"C:\Users\me\macshot.png"));
        Assert.IsFalse(UrlSchemeCommands.IsCommandUrl(null));
    }

    [TestMethod]
    public void All_NamesEveryCommandOnceAndEveryActionOnce()
    {
        // The table is what the parser matches on and what the settings window lists.
        // A repeated host would make one of the two unreachable.
        Assert.AreEqual(
            UrlSchemeCommands.All.Count,
            UrlSchemeCommands.All.Select(entry => entry.Host).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.AreEqual(
            Enum.GetValues<UrlSchemeAction>().Length,
            UrlSchemeCommands.All.Select(entry => entry.Action).Distinct().Count());
    }

    [TestMethod]
    public void Text_IsSomethingThatCanBeTyped()
    {
        // The settings window shows these for copying, so each has to be a URL rather
        // than a description of one.
        foreach (var entry in UrlSchemeCommands.All)
        {
            Assert.IsTrue(entry.Text.StartsWith("macshot://", StringComparison.Ordinal), entry.Text);
        }
    }
}
