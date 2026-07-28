using Macshot.Windows.Core.Recognition;

namespace Macshot.Windows.Core.Tests.Recognition;

[TestClass]
public sealed class PiiDetectorTests
{
    [TestMethod]
    public void Detect_FindsAnEmailAddress()
    {
        AssertFinds(PiiKind.Email, "contact bob@example.com now");
    }

    [TestMethod]
    public void Detect_FindsAnAwsAccessKey()
    {
        AssertFinds(PiiKind.AwsAccessKey, "AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE");
    }

    [TestMethod]
    public void Detect_FindsABearerToken()
    {
        AssertFinds(PiiKind.BearerToken, "Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.abc");
    }

    [TestMethod]
    public void Detect_FindsAnIpAddress()
    {
        AssertFinds(PiiKind.IpAddress, "host 192.168.10.24 is up");
    }

    [TestMethod]
    public void Detect_FindsASocialSecurityNumber()
    {
        AssertFinds(PiiKind.SocialSecurityNumber, "SSN 123-45-6789");
    }

    [TestMethod]
    public void Detect_FindsAPhoneNumber()
    {
        AssertFinds(PiiKind.Phone, "call +1 (555) 123-4567");
    }

    /// <summary>
    /// The digits are what makes this expensive to get wrong in both directions: a
    /// real card left visible is a leak, and any long number treated as a card
    /// blacks out order ids and timestamps until the user turns the feature off.
    /// </summary>
    [TestMethod]
    public void Detect_AcceptsACardThatPassesLuhn()
    {
        AssertFinds(PiiKind.CreditCard, "card 4539 1488 0343 6467");
    }

    [TestMethod]
    public void Detect_RejectsALongNumberThatIsNotACard()
    {
        // The same number with the last digit changed, so only the checksum tells
        // the two apart.
        var matches = PiiDetector.Detect("order 4539 1488 0343 6468");

        Assert.IsFalse(matches.Any(match => match.Kind == PiiKind.CreditCard));
    }

    [TestMethod]
    public void Detect_ReturnsNothingForTextWithoutSecrets()
    {
        Assert.AreEqual(0, PiiDetector.Detect("the quick brown fox").Count);
        Assert.AreEqual(0, PiiDetector.Detect("   ").Count);
        Assert.AreEqual(0, PiiDetector.Detect(null).Count);
    }

    [TestMethod]
    public void Detect_ReportsMatchesInReadingOrder()
    {
        var matches = PiiDetector.Detect("a@b.co then 10.0.0.1");

        CollectionAssert.AreEqual(
            matches.Select(match => match.Start).OrderBy(start => start).ToArray(),
            matches.Select(match => match.Start).ToArray());
    }

    /// <summary>
    /// Asserts the kind is found and that the match actually spans the secret, so a
    /// pattern that matched one stray character would still fail.
    /// </summary>
    private static void AssertFinds(PiiKind kind, string text)
    {
        var matches = PiiDetector.Detect(text);
        var match = matches.FirstOrDefault(candidate => candidate.Kind == kind);

        Assert.AreEqual(kind, match.Kind, $"No {kind} found in \"{text}\".");
        Assert.IsTrue(match.Length >= 7, $"{kind} matched only \"{match.Value}\".");
        Assert.AreEqual(match.Value, text.Substring(match.Start, match.Length));
    }
}
