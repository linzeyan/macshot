using Macshot.Windows.Core.Localization;

namespace Macshot.Windows.Core.Tests.Localization;

[TestClass]
public sealed class ChineseTextTests
{
    /// <summary>
    /// English stays English, whatever language the interface is in.
    /// </summary>
    /// <remarks>
    /// This is the rule the first attempt got wrong. The weight was decided by the app's
    /// language rather than by the string, so every English label in a Chinese window —
    /// "System Default" in the language list, the format names, the shortcut letters — came
    /// up in the Chinese weight. Nothing about a mixed row makes that right: the two faces
    /// are chosen per glyph, and the weight has to be chosen the same way.
    /// </remarks>
    [TestMethod]
    public void Latin_IsNotChinese()
    {
        foreach (var text in new[] { "System Default", "PNG", "100%", "B", "Cmd+Shift+X", "—", "·" })
        {
            Assert.IsFalse(ChineseText.Contains(text), $"{text} is not set in the Chinese face");
        }
    }

    /// <summary>
    /// Chinese is Chinese, including where it is only part of the line.
    /// </summary>
    /// <remarks>
    /// A translated string is rarely all Chinese — "PNG 品質", "延遲 3 秒" — and a rule that
    /// asked whether *every* glyph were Chinese would leave those at the Latin weight, which
    /// is the same bug from the other side.
    /// </remarks>
    [TestMethod]
    public void Chinese_IsChinese()
    {
        foreach (var text in new[] { "一般", "PNG 品質", "延遲 3 秒", "（選用）" })
        {
            Assert.IsTrue(ChineseText.Contains(text), $"{text} is set in the Chinese face");
        }
    }

    /// <summary>
    /// Nothing is not Chinese.
    /// </summary>
    /// <remarks>
    /// Labels are built before they are filled in often enough — an empty caption, a
    /// value that has not been read yet — and asking about one must not throw where the
    /// answer is plainly "no".
    /// </remarks>
    [TestMethod]
    public void Nothing_IsNotChinese()
    {
        Assert.IsFalse(ChineseText.Contains(null));
        Assert.IsFalse(ChineseText.Contains(string.Empty));
    }
}
