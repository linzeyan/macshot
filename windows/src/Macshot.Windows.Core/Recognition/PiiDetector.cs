using System.Text.RegularExpressions;

namespace Macshot.Windows.Core.Recognition;

public enum PiiKind
{
    Email,
    Phone,
    SocialSecurityNumber,
    CreditCard,
    IpAddress,
    AwsAccessKey,
    BearerToken,
}

public readonly record struct PiiMatch(PiiKind Kind, int Start, int Length, string Value);

/// <summary>
/// Finds things in recognized text that should not be published, the portable half
/// of the macOS <c>AutoRedactor</c>.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately regex rather than a model. This runs over OCR output on a
/// screenshot the user is about to share, so a wrong answer is either a leak or a
/// black box over something harmless, and both are better served by rules the user
/// can reason about than by a confidence score.
/// </para>
/// <para>
/// The patterns lean towards over-matching, with one exception: card numbers are
/// Luhn-checked. "Any sixteen digits" would black out order numbers, build ids, and
/// timestamps on most screenshots, and a redactor that covers the wrong things is
/// one the user turns off.
/// </para>
/// </remarks>
public static partial class PiiDetector
{
    /// <summary>
    /// A bound on backtracking. OCR output is not adversarial, but it is garbled,
    /// and a pathological line must not hang the capture.
    /// </summary>
    private const int MatchTimeoutMilliseconds = 200;

    private static readonly (PiiKind Kind, Regex Pattern)[] Patterns =
    [
        (PiiKind.Email, EmailPattern()),
        (PiiKind.AwsAccessKey, AwsAccessKeyPattern()),
        (PiiKind.BearerToken, BearerTokenPattern()),
        (PiiKind.SocialSecurityNumber, SocialSecurityPattern()),
        (PiiKind.IpAddress, IpAddressPattern()),
        (PiiKind.CreditCard, CreditCardPattern()),
        (PiiKind.Phone, PhonePattern()),
    ];

    /// <summary>
    /// Every match in the text, ordered by position. Overlapping matches from
    /// different patterns are all reported: they redact to the same boxes anyway,
    /// and suppressing one would mean ranking which kind of secret matters more.
    /// </summary>
    public static IReadOnlyList<PiiMatch> Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var matches = new List<PiiMatch>();
        foreach (var (kind, pattern) in Patterns)
        {
            foreach (Match match in pattern.Matches(text))
            {
                if (kind == PiiKind.CreditCard && !PassesLuhn(match.Value))
                {
                    continue;
                }

                matches.Add(new PiiMatch(kind, match.Index, match.Length, match.Value));
            }
        }

        matches.Sort((first, second) => first.Start.CompareTo(second.Start));
        return matches;
    }

    /// <summary>
    /// The checksum every issued card number carries. Without it the pattern matches
    /// any long run of digits, which on a screenshot is usually an identifier.
    /// </summary>
    private static bool PassesLuhn(string value)
    {
        var sum = 0;
        var digits = 0;
        var doubling = false;

        for (var index = value.Length - 1; index >= 0; index--)
        {
            var character = value[index];
            if (!char.IsAsciiDigit(character))
            {
                continue;
            }

            var digit = character - '0';
            if (doubling)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            digits++;
            doubling = !doubling;
        }

        return digits is >= 13 and <= 19 && sum % 10 == 0;
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.None, MatchTimeoutMilliseconds)]
    private static partial Regex EmailPattern();

    // Phone numbers vary too much between countries to describe exactly, so this
    // asks for a plausible run of digits and separators rather than a grammar.
    [GeneratedRegex(@"(?<![\w.])\+?\d[\d ().\-]{7,16}\d(?![\w.])", RegexOptions.None, MatchTimeoutMilliseconds)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.None, MatchTimeoutMilliseconds)]
    private static partial Regex SocialSecurityPattern();

    [GeneratedRegex(@"\b\d(?:[ \-]?\d){12,18}\b", RegexOptions.None, MatchTimeoutMilliseconds)]
    private static partial Regex CreditCardPattern();

    [GeneratedRegex(
        @"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b",
        RegexOptions.None,
        MatchTimeoutMilliseconds)]
    private static partial Regex IpAddressPattern();

    [GeneratedRegex(
        @"\b(?:AKIA|ASIA|AGPA|AIDA|AROA|ANPA|ANVA)[0-9A-Z]{16}\b",
        RegexOptions.None,
        MatchTimeoutMilliseconds)]
    private static partial Regex AwsAccessKeyPattern();

    [GeneratedRegex(
        @"\bBearer\s+[A-Za-z0-9\-._~+/]{8,}={0,2}",
        RegexOptions.IgnoreCase,
        MatchTimeoutMilliseconds)]
    private static partial Regex BearerTokenPattern();
}
