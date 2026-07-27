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
