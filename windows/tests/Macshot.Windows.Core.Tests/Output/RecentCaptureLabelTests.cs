using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Output;

[TestClass]
public sealed class RecentCaptureLabelTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 14, 30, 0, TimeSpan.FromHours(8));

    /// <summary>The English is the key, so an untranslated run reads it back unchanged.</summary>
    private static string Untranslated(string english) => english;

    /// <summary>
    /// The submenu's whole job is telling five captures apart. Listing only the time did
    /// not: two taken the same afternoon are two clock readings and nothing else, so the
    /// size has to be in the title the way macshot puts it there.
    /// </summary>
    [TestMethod]
    public void Of_NamesTheCaptureBySizeAndAgeSoTwoOfThemCanBeToldApart()
    {
        Assert.AreEqual(
            "800 × 500  —  5m ago",
            RecentCaptureLabel.Of(800, 500, Now.AddMinutes(-5), Now, Untranslated));
    }

    /// <summary>
    /// macshot's own thresholds. Each is the moment one unit stops making sense: seconds
    /// past a minute, minutes past an hour, and a count at all past a day — where the
    /// date is the more useful answer than "37h ago".
    /// </summary>
    [TestMethod]
    public void Age_ChangesUnitWhereTheSmallerOneStopsSayingAnything()
    {
        Assert.AreEqual("just now", RecentCaptureLabel.Age(Now.AddSeconds(-4), Now, Untranslated));
        Assert.AreEqual("5s ago", RecentCaptureLabel.Age(Now.AddSeconds(-5), Now, Untranslated));
        Assert.AreEqual("59s ago", RecentCaptureLabel.Age(Now.AddSeconds(-59), Now, Untranslated));
        Assert.AreEqual("1m ago", RecentCaptureLabel.Age(Now.AddSeconds(-60), Now, Untranslated));
        Assert.AreEqual("59m ago", RecentCaptureLabel.Age(Now.AddMinutes(-59), Now, Untranslated));
        Assert.AreEqual("1h ago", RecentCaptureLabel.Age(Now.AddMinutes(-60), Now, Untranslated));
        Assert.AreEqual("23h ago", RecentCaptureLabel.Age(Now.AddHours(-23), Now, Untranslated));

        // A day old is a date, not a count.
        StringAssert.Contains(
            RecentCaptureLabel.Age(Now.AddHours(-24), Now, Untranslated),
            ":",
            "a capture older than a day is named by date and time");
    }

    /// <summary>
    /// The translated strings carry macshot's printf placeholder, which no .NET formatter
    /// reads. A count left as "%d" reaches the user as literally that.
    /// </summary>
    [TestMethod]
    public void Age_FillsThePrintfPlaceholderTheTranslationsCarry()
    {
        var chinese = RecentCaptureLabel.Age(
            Now.AddMinutes(-7),
            Now,
            english => english == "%dm ago" ? "%d 分鐘前" : english);

        Assert.AreEqual("7 分鐘前", chinese);
    }

    /// <summary>
    /// A guest clock corrected forwards, or a file stamped in the future, must not read
    /// as a negative count — which is what subtracting without a floor would produce.
    /// </summary>
    [TestMethod]
    public void Age_TreatsACaptureFromTheFutureAsJustNow()
    {
        Assert.AreEqual("just now", RecentCaptureLabel.Age(Now.AddMinutes(5), Now, Untranslated));
    }
}
