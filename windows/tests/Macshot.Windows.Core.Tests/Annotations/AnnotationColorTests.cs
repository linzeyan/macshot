using Macshot.Windows.Core.Annotations;

namespace Macshot.Windows.Core.Tests.Annotations;

[TestClass]
public sealed class AnnotationColorTests
{
    [TestMethod]
    public void ToHex_RoundTripsThroughTheParser()
    {
        var color = new AnnotationColor(76, 194, 255, 128);

        Assert.IsTrue(AnnotationColor.TryParseHex(color.ToHex(), out var parsed));
        Assert.AreEqual(color, parsed);
    }

    /// <summary>
    /// Six digits is what a person hand-editing the settings file will type, and
    /// dropping alpha has to mean opaque rather than invisible.
    /// </summary>
    [TestMethod]
    public void TryParseHex_TreatsAMissingAlphaAsOpaque()
    {
        Assert.IsTrue(AnnotationColor.TryParseHex("#4CC2FF", out var color));

        Assert.AreEqual(new AnnotationColor(76, 194, 255), color);
    }

    [TestMethod]
    public void TryParseHex_AcceptsTextWithoutTheHash()
    {
        Assert.IsTrue(AnnotationColor.TryParseHex("ff4cc2ff", out var color));

        Assert.AreEqual(new AnnotationColor(76, 194, 255), color);
    }

    [TestMethod]
    public void TryParseHex_RejectsAnythingItCannotRead()
    {
        Assert.IsFalse(AnnotationColor.TryParseHex(null, out _));
        Assert.IsFalse(AnnotationColor.TryParseHex("  ", out _));
        Assert.IsFalse(AnnotationColor.TryParseHex("#FFF", out _));
        Assert.IsFalse(AnnotationColor.TryParseHex("#GGGGGG", out _));
    }
}
