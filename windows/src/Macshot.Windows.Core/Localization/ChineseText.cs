namespace Macshot.Windows.Core.Localization;

/// <summary>
/// Whether a string is one the Chinese face draws.
/// </summary>
/// <remarks>
/// The interface names two families and lets DirectWrite resolve them per glyph, so what
/// face a label is set in is decided by the label rather than by the language the app is
/// running in. Anything that has to follow the face — the weight, above all — has to ask
/// the same question of the same string, which is why the question is asked in one place.
/// </remarks>
public static class ChineseText
{
    /// <summary>
    /// Whether any of <paramref name="text"/> falls to the Chinese face.
    /// </summary>
    public static bool Contains(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var glyph in text)
        {
            if (IsChinese(glyph))
            {
                return true;
            }
        }

        return false;
    }

    /// <remarks>
    /// The CJK ideographs and the blocks either side of them, the compatibility ideographs,
    /// and the fullwidth forms that are set with them. Kana is deliberately absent: this
    /// asks which face draws the glyph, not which language wrote it.
    /// </remarks>
    private static bool IsChinese(char glyph) =>
        glyph is >= '⺀' and <= '鿿'
            or >= '豈' and <= '﫿'
            or >= '！' and <= '｠';
}
