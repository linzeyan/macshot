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
    public void Detect_FindsASocialSecurityNumberOcrReadWithSpaces()
    {
        // The pattern insisted on hyphens. OCR reads a hyphen as a space often
        // enough that this was a number left in the clear on a real screenshot.
        AssertFinds(PiiKind.SocialSecurityNumber, "SSN 123 45 6789");
    }

    /// <summary>
    /// The pattern the feature exists for. A developer screenshots a terminal, a
    /// <c>.env</c>, or a config page; none of the other patterns here match a line of
    /// one, so before this the redactor covered nothing on the capture most worth
    /// covering.
    /// </summary>
    [TestMethod]
    public void Detect_FindsASecretWrittenAsAnAssignment()
    {
        AssertFinds(PiiKind.SecretAssignment, "api_key = sk_live_9f2b7c1d");
        AssertFinds(PiiKind.SecretAssignment, "PASSWORD: hunter2please");
        AssertFinds(PiiKind.SecretAssignment, "private-key=MIIEvQIBADANBg");
    }

    [TestMethod]
    public void Detect_FindsALongRunOfHex()
    {
        AssertFinds(PiiKind.HexKey, "token 9f2b7c1d4e6a8b0c2d4e6f8a0b2c4d6e");
    }

    [TestMethod]
    public void Detect_LeavesShortHexAlone()
    {
        // A colour, a short id, a git prefix. Thirty-two is where a run stops
        // being something a person typed and starts being something a machine issued.
        Assert.IsFalse(PiiDetector.Detect("colour #1f1f1f and build ab12cd34")
            .Any(match => match.Kind == PiiKind.HexKey));
    }

    [TestMethod]
    public void Detect_FindsTheRestOfACard()
    {
        // A redacted card number with its security code and expiry still showing
        // gives back most of what covering the number was for.
        AssertFinds(PiiKind.CardVerificationValue, "CVV: 4821");
        AssertFinds(PiiKind.CardExpiry, "expires 09/2028");
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
