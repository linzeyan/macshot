using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Output;

/// <summary>
/// How long ago a capture was taken, as the history panel says it.
/// </summary>
[TestClass]
public sealed class TimeAgoTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 14, 5, 0, TimeSpan.Zero);

    [TestMethod]
    public void TheFirstFewSecondsAreJustNow()
    {
        Assert.AreEqual("just now", Phrase(TimeSpan.FromSeconds(0)).Template);
        Assert.AreEqual("just now", Phrase(TimeSpan.FromSeconds(4)).Template);
    }

    [TestMethod]
    public void EachUnitStepsUpAsTheOneBelowItRunsOut()
    {
        Assert.AreEqual(("%ds ago", 30), Phrase(TimeSpan.FromSeconds(30)));
        Assert.AreEqual(("%dm ago", 5), Phrase(TimeSpan.FromMinutes(5)));
        Assert.AreEqual(("%dh ago", 3), Phrase(TimeSpan.FromHours(3)));
    }

    /// <summary>
    /// macshot stops counting after a day, and it is right to: "31h ago" is arithmetic
    /// the reader has to do.
    /// </summary>
    [TestMethod]
    public void PastADayItGivesTheDateInstead()
    {
        Assert.AreEqual(string.Empty, Phrase(TimeSpan.FromHours(24)).Template);
        Assert.AreEqual(string.Empty, Phrase(TimeSpan.FromDays(9)).Template);
    }

    /// <summary>
    /// A clock that jumped backwards — a machine waking, a time zone changing — makes a
    /// capture look like it was taken in the future. "-4s ago" would be the only wrong
    /// answer available.
    /// </summary>
    [TestMethod]
    public void ACaptureFromTheFutureReadsAsJustNow()
    {
        Assert.AreEqual("just now", Phrase(TimeSpan.FromHours(-2)).Template);
    }

    /// <summary>
    /// The template keeps its placeholder so it can be looked up: "5m ago" is not a key
    /// in any translation and "%dm ago" is.
    /// </summary>
    [TestMethod]
    public void TheNumberComesBackApartFromThePhrase()
    {
        var (template, count) = Phrase(TimeSpan.FromMinutes(42));

        StringAssert.Contains(template, "%d");
        Assert.AreEqual(42, count);
    }

    private static (string Template, int Count) Phrase(TimeSpan ago) => TimeAgo.Phrase(Now - ago, Now);
}
