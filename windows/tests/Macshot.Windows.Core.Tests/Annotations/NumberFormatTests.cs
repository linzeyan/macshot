using Macshot.Windows.Core.Annotations;

namespace Macshot.Windows.Core.Tests.Annotations;

[TestClass]
public sealed class NumberFormatTests
{
    /// <summary>
    /// The point of the option: a screenshot whose callouts are keyed to lettered or
    /// Roman-numbered prose beside it has to carry the same sequence, not a second one.
    /// </summary>
    [TestMethod]
    public void Format_CountsInTheChosenNotation()
    {
        Assert.AreEqual("4", NumberFormat.Decimal.Format(4));
        Assert.AreEqual("IV", NumberFormat.Roman.Format(4));
        Assert.AreEqual("D", NumberFormat.Alpha.Format(4));
        Assert.AreEqual("d", NumberFormat.AlphaLower.Format(4));
    }

    /// <summary>
    /// The subtractive cases are the ones a naive loop gets wrong, and they are the first
    /// four badges of any figure numbered this way.
    /// </summary>
    [TestMethod]
    public void Format_WritesRomanNumeralsSubtractively()
    {
        Assert.AreEqual("I", NumberFormat.Roman.Format(1));
        Assert.AreEqual("IX", NumberFormat.Roman.Format(9));
        Assert.AreEqual("XL", NumberFormat.Roman.Format(40));
        Assert.AreEqual("MCMXCIV", NumberFormat.Roman.Format(1994));
    }

    /// <summary>
    /// A badge is placed by a click and has to show something. A format that returned an
    /// empty string for a number out of range would leave an empty circle on the capture
    /// with nothing to say what went wrong.
    /// </summary>
    [TestMethod]
    public void Format_NeverComesBackEmpty()
    {
        foreach (var format in Enum.GetValues<NumberFormat>())
        {
            foreach (var number in new[] { int.MinValue, -1, 0, 1, 4000, int.MaxValue })
            {
                Assert.AreNotEqual(
                    string.Empty,
                    format.Format(number),
                    $"{format} produced nothing for {number}.");
            }
        }
    }

    /// <summary>
    /// Letters wrap rather than carrying into AA, because the badge is a circle: macshot
    /// does the same, and the two products must not disagree about what the 27th badge of
    /// the same screenshot reads.
    /// </summary>
    [TestMethod]
    public void Format_WrapsLettersAfterZ()
    {
        Assert.AreEqual("Z", NumberFormat.Alpha.Format(26));
        Assert.AreEqual("A", NumberFormat.Alpha.Format(27));
    }
}
