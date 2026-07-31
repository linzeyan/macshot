using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Output;

[TestClass]
public sealed class FilenameTemplateTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 7, 27, 22, 45, 3, TimeSpan.Zero);

    [TestMethod]
    public void Resolve_ExpandsEveryDateToken()
    {
        var name = FilenameTemplate.Resolve("{yyyy}-{MM}-{dd} {HH}{mm}{ss}", Timestamp);

        Assert.AreEqual("2026-07-27 224503", name);
    }

    /// <summary>
    /// The tokens a template is actually written in, and the ones this understood
    /// none of. The two apps share a settings vocabulary or they do not: a template
    /// written on a Mac has to mean the same thing here.
    /// </summary>
    [TestMethod]
    public void Resolve_ExpandsMacshotsOwnTokens()
    {
        Assert.AreEqual("2026-07-27", FilenameTemplate.Resolve("{date}", Timestamp));
        Assert.AreEqual("22-45-03", FilenameTemplate.Resolve("{time}", Timestamp));
        Assert.AreEqual("2026-07-27_22-45-03", FilenameTemplate.Resolve("{timestamp}", Timestamp));
        Assert.AreEqual(
            Timestamp.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            FilenameTemplate.Resolve("{unix}", Timestamp));
    }

    [TestMethod]
    public void Resolve_NamesTheCaptureAfterTheWindowItCameFrom()
    {
        // The token that makes a folder of captures readable a week later.
        var name = FilenameTemplate.Resolve(
            "{window} {date}",
            Timestamp,
            new FilenameContext(WindowTitle: "Ledger"));

        Assert.AreEqual("Ledger 2026-07-27", name);
    }

    /// <summary>
    /// A full-screen capture has no window and a single capture has no index. Both
    /// resolve to nothing rather than to a placeholder, which is macshot's rule: the
    /// name gets shorter, it does not acquire the word "null".
    /// </summary>
    [TestMethod]
    public void Resolve_LeavesOutWhatTheCaptureDoesNotHave()
    {
        Assert.AreEqual("shot", FilenameTemplate.Resolve("shot{window}{index}", Timestamp));
    }

    [TestMethod]
    public void Resolve_NumbersACaptureWithinARun()
    {
        Assert.AreEqual(
            "shot-3",
            FilenameTemplate.Resolve("shot-{index}", Timestamp, new FilenameContext(Index: 3)));
    }

    /// <summary>
    /// Fresh on every occurrence rather than once per name. Two of them in one
    /// template is a request for two different runs — there is no other reason to
    /// write the token twice.
    /// </summary>
    [TestMethod]
    public void Resolve_GivesEachRandomTokenItsOwnValue()
    {
        var name = FilenameTemplate.Resolve("{random}-{random}", Timestamp);
        var parts = name.Split('-');

        Assert.AreEqual(2, parts.Length);
        Assert.AreEqual(8, parts[0].Length);
        Assert.AreEqual(8, parts[1].Length);
        Assert.AreNotEqual(parts[0], parts[1]);
        Assert.IsTrue(parts.All(part => part.All(char.IsAsciiLetterOrDigit)));
    }

    /// <summary>
    /// A recording named by the screenshot template lands in the same folder under a
    /// name that says "Screenshot", and the two cannot be told apart by name.
    /// </summary>
    [TestMethod]
    public void DefaultRecording_IsNotTheScreenshotDefault()
    {
        Assert.AreNotEqual(FilenameTemplate.Default, FilenameTemplate.DefaultRecording);
        StringAssert.StartsWith(FilenameTemplate.Resolve(FilenameTemplate.DefaultRecording, Timestamp), "Recording");
    }

    [TestMethod]
    public void Resolve_KeepsUnknownTokensVerbatimSoTyposAreVisible()
    {
        var name = FilenameTemplate.Resolve("shot-{nope}-{dd}", Timestamp);

        Assert.AreEqual("shot-{nope}-27", name);
    }

    /// <summary>
    /// A template is free text, so it can carry characters that would either make
    /// the write fail or, worse, redirect it into another directory.
    /// </summary>
    [TestMethod]
    public void Resolve_ReplacesCharactersWindowsRejects()
    {
        var name = FilenameTemplate.Resolve(@"..\..\etc:shot?", Timestamp);

        Assert.AreEqual("..-..-etc-shot-", name);
    }

    /// <summary>
    /// An empty field means "I have no preference", so it gets the default naming
    /// rather than a literal constant that every capture would then collide on.
    /// </summary>
    [TestMethod]
    public void Resolve_UsesTheDefaultTemplateWhenNoneIsGiven()
    {
        var expected = FilenameTemplate.Resolve(FilenameTemplate.Default, Timestamp);

        Assert.AreEqual(expected, FilenameTemplate.Resolve(null, Timestamp));
        Assert.AreEqual(expected, FilenameTemplate.Resolve("   ", Timestamp));
    }

    /// <summary>
    /// A template made entirely of characters Windows strips leaves nothing to name
    /// the file with, and a capture must never be lost to that.
    /// </summary>
    [TestMethod]
    public void Resolve_FallsBackWhenTheTemplateResolvesToNothing()
    {
        Assert.AreEqual("Macshot", FilenameTemplate.Resolve("...", Timestamp));
    }

    /// <summary>
    /// Windows drops trailing dots and spaces when creating a file, so a name that
    /// ended in one would not be the name the user asked for.
    /// </summary>
    [TestMethod]
    public void Resolve_TrimsTrailingDotsAndSpaces()
    {
        Assert.AreEqual("shot", FilenameTemplate.Resolve("shot. ", Timestamp));
    }

    [TestMethod]
    public void ResolveUnique_LeavesTheNameAloneWhenItIsFree()
    {
        var name = FilenameTemplate.ResolveUnique("shot", Timestamp, ".png", _ => false);

        Assert.AreEqual("shot.png", name);
    }

    /// <summary>
    /// Two captures inside the same second resolve to the same template output, and
    /// silently overwriting the first one would lose a capture.
    /// </summary>
    [TestMethod]
    public void ResolveUnique_SuffixesUntilTheNameIsFree()
    {
        var taken = new HashSet<string> { "shot.png", "shot-2.png" };

        var name = FilenameTemplate.ResolveUnique("shot", Timestamp, ".png", taken.Contains);

        Assert.AreEqual("shot-3.png", name);
    }
}
