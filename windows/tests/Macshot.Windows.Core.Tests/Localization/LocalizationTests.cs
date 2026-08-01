using Macshot.Windows.Core.Localization;

namespace Macshot.Windows.Core.Tests.Localization;

[TestClass]
public sealed class StringTableTests
{
    [TestMethod]
    public void Parse_ReadsAnEntry()
    {
        var table = StringTable.Parse("""
            "Capture area" = "擷取區域";
            """);

        Assert.AreEqual("擷取區域", table.Get("Capture area"));
    }

    [TestMethod]
    public void Parse_SkipsTheHeaderEveryTranslationFileCarries()
    {
        // Every one of macshot's forty files opens with a block comment telling a
        // contributor how to add a language.
        var table = StringTable.Parse("""
            /* 繁體中文 — macshot localization
             *
             * Keys use the English text.
             */

            // A line comment, which the format also allows.
            "Copy" = "複製";
            """);

        Assert.AreEqual(1, table.Count);
        Assert.AreEqual("複製", table.Get("Copy"));
    }

    [TestMethod]
    public void Parse_ResolvesTheEscapesTheFormatUses()
    {
        var table = StringTable.Parse("""
            "quote" = "say \"hi\"";
            "line" = "one\ntwo";
            "path" = "C:\\shots";
            """);

        Assert.AreEqual("say \"hi\"", table.Get("quote"));
        Assert.AreEqual("one\ntwo", table.Get("line"));
        Assert.AreEqual(@"C:\shots", table.Get("path"));
    }

    [TestMethod]
    public void Get_AnswersEnglishWhenThereIsNoTranslation()
    {
        // The property that matters most: keys are the English text, so a missing entry
        // shows English. Nothing in the product can go blank for want of a string.
        var table = StringTable.Parse("""
            "Copy" = "複製";
            "Paste" = "";
            """);

        Assert.AreEqual("Save as...", table.Get("Save as..."));
        Assert.AreEqual("Paste", table.Get("Paste"), "an empty value means not translated yet");
        Assert.AreEqual("Close", StringTable.Empty.Get("Close"));
    }

    [TestMethod]
    public void Parse_KeepsTheEntriesAroundALineItCannotRead()
    {
        // A translation file is a contribution from someone who does not build the app.
        // One bad line must not cost the language.
        var table = StringTable.Parse("""
            "Copy" = "複製";
            this line is not an entry at all
            "Cut" = "剪下";
            """);

        Assert.AreEqual("複製", table.Get("Copy"));
        Assert.AreEqual("剪下", table.Get("Cut"));
    }

    [TestMethod]
    public void Parse_SurvivesAFileThatEndsMidEntry()
    {
        foreach (var text in new string?[] { null, "", "   ", "\"unterminated", "\"key\" =", "\"key\" = \"" })
        {
            Assert.AreEqual("x", StringTable.Parse(text).Get("x"), text ?? "null");
        }
    }
}

[TestClass]
public sealed class AppLanguagesTests
{
    [TestMethod]
    public void All_IsMacshotSListInMacshotSOrder()
    {
        Assert.AreEqual(41, AppLanguages.All.Count, "forty languages and System Default");
        Assert.AreEqual("system", AppLanguages.All[0].Code);
        Assert.AreEqual("繁體中文", AppLanguages.All[^1].Name);
        Assert.AreEqual(40, AppLanguages.Codes.Count);
        CollectionAssert.DoesNotContain(AppLanguages.Codes.ToArray(), "system");
    }

    [TestMethod]
    public void Resolve_HonoursAChosenLanguageOverTheSystemS()
    {
        Assert.AreEqual("ja", AppLanguages.Resolve("ja", ["de-DE"]));
    }

    [TestMethod]
    public void Resolve_PrefersTheFullCodeSoTheTwoChinesesStayApart()
    {
        // The case the ordering exists for: answering zh-Hant with zh-Hans would show a
        // reader of traditional Chinese the simplified translation.
        Assert.AreEqual("zh-Hant", AppLanguages.Resolve(null, ["zh-Hant"]));
        Assert.AreEqual("zh-Hans", AppLanguages.Resolve(null, ["zh-Hans"]));
    }

    [TestMethod]
    public void Resolve_FallsBackThroughScriptThenBareLanguage()
    {
        // zh-Hant-TW is not in the list; zh-Hant is.
        Assert.AreEqual("zh-Hant", AppLanguages.Resolve("system", ["zh-Hant-TW"]));

        // de-AT is not; de is.
        Assert.AreEqual("de", AppLanguages.Resolve("system", ["de-AT"]));

        // Underscores as well as hyphens, since the same rule reads both platforms.
        Assert.AreEqual("pt-BR", AppLanguages.Resolve("system", ["pt_BR"]));
    }

    [TestMethod]
    public void Resolve_TakesTheFirstPreferredLanguageItHas()
    {
        Assert.AreEqual("fr", AppLanguages.Resolve(null, ["kl-GL", "fr-CA", "de"]));
    }

    [TestMethod]
    public void Resolve_AnswersEnglishRatherThanNothing()
    {
        Assert.AreEqual("en", AppLanguages.Resolve(null, ["kl-GL"]));
        Assert.AreEqual("en", AppLanguages.Resolve(null, []));
        Assert.AreEqual("en", AppLanguages.Resolve("system", null));

        // A code no build carries is not honoured — it would show English anyway, and
        // this keeps the resolution the same everywhere.
        Assert.AreEqual("en", AppLanguages.Resolve("klingon", ["de"]));
    }
}
