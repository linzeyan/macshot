namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// One group in the stamp picker: the emoji it holds, and the one drawn on its tab.
/// </summary>
/// <param name="Tab">The emoji that labels the group — macshot's own choice for each.</param>
/// <param name="Emoji">What choosing the tab shows.</param>
public sealed record StampCategory(string Tab, IReadOnlyList<string> Emoji);

/// <summary>
/// The emoji the stamp tool offers, and which of them the options row shows outright.
/// </summary>
/// <remarks>
/// Here rather than beside the code that rasterizes a glyph, because these are the same
/// facts on both platforms — macshot's <c>StampEmojis</c> — and because the row and the
/// picker both read them. Two copies would drift into offering different sets, and the
/// first sign of it would be a stamp on the row that the picker shows as unchosen.
/// </remarks>
public static class StampChoices
{
    /// <summary>
    /// The ones laid straight on the options row, in macshot's order — its
    /// <c>StampEmojis.common</c> (<c>StampToolHandler.swift:49-55</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Grouped by what each is for rather than by how often it is reached for: four that
    /// point at something, four that pass a verdict on it, four reactions, five marks of
    /// approval. Somebody scanning the row for "no" finds it among the other verdicts.
    /// </para>
    /// <para>
    /// On the row rather than behind the picker because a stamp is a one-click mark, and a
    /// tick that takes two clicks to reach is slower than drawing one by hand.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Quick { get; } =
    [
        // Point at things.
        "\U0001F446", "\U0001F447", "\U0001F448", "\U0001F449",

        // Approve, reject, warn, question.
        "✅", "❌", "⚠️", "❓",

        // Reactions: hot, bug, look here, idea.
        "\U0001F525", "\U0001F41B", "\U0001F440", "\U0001F4A1",

        // Bullseye, star, love, thumbs up, thumbs down.
        "\U0001F3AF", "⭐", "❤️", "\U0001F44D", "\U0001F44E",
    ];

    /// <summary>
    /// Everything the picker behind the row offers, in the five groups macshot sorts them
    /// into — its <c>StampEmojis.categories</c> (<c>StampToolHandler.swift:6-45</c>).
    /// </summary>
    /// <remarks>
    /// Grouped rather than in one list, because 104 emoji in one grid is a thing to hunt
    /// through rather than a thing to pick from. The tab is drawn as the first emoji of its
    /// own group, which is how macshot labels them: a picture of what is inside beats a
    /// word for it at this size, and it needs no translating.
    /// </remarks>
    public static IReadOnlyList<StampCategory> Categories { get; } =
    [
        new("\U0001F600",
        [
            "\U0001F600", "\U0001F602", "\U0001F923", "\U0001F60D", "\U0001F914", "\U0001F60E", "\U0001F92F", "\U0001F631",
            "\U0001F624", "\U0001F973", "\U0001F921", "\U0001F4A9", "\U0001F47B", "\U0001F916", "\U0001F47D", "\U0001F608",
            "\U0001F648", "\U0001F649", "\U0001F64A", "\U0001F4AA", "\U0001F44F", "\U0001F64C", "\U0001F91D", "\U0001FAE1",
        ]),
        new("\U0001F446",
        [
            "\U0001F446", "\U0001F447", "\U0001F448", "\U0001F449", "\U0001F44D", "\U0001F44E", "✊", "\U0001F44A",
            "\U0001F91E", "✌️", "\U0001F91F", "\U0001FAF5", "☝️", "\U0001F44B", "🖐️", "✋",
        ]),
        new("✅",
        [
            "✅", "❌", "⚠️", "❓", "❗", "⛔", "\U0001F6AB", "\U0001F4AF",
            "✏️", "🗑️", "\U0001F4CC", "\U0001F512", "\U0001F513", "🏷️", "\U0001F4CE", "\U0001F517",
            "⬆️", "⬇️", "⬅️", "➡️", "↩️", "\U0001F504", "➕", "➖",
        ]),
        new("\U0001F525",
        [
            "\U0001F525", "\U0001F4A1", "⭐", "❤️", "\U0001F480", "\U0001F41B", "\U0001F3AF", "\U0001F680",
            "\U0001F389", "\U0001F4A3", "\U0001F9E8", "⚡", "\U0001F4A5", "\U0001F514", "\U0001F4E2", "\U0001F3C6",
            "\U0001F6D1", "\U0001F6A7", "🏗️", "\U0001F9EA", "\U0001F52C", "\U0001F4BB", "\U0001F4F1", "🖥️",
        ]),
        new("\U0001F6A9",
        [
            "\U0001F6A9", "\U0001F3C1", "\U0001F4CD", "\U0001F4AC", "\U0001F4AD", "🗯️", "👁️", "\U0001F440",
            "\U0001F50D", "\U0001F50E", "\U0001F4DD", "\U0001F4CB", "\U0001F4CA", "\U0001F4C8", "\U0001F4C9", "🗂️",
        ]),
    ];

    /// <summary>
    /// Every emoji the picker offers, flattened — for anything that needs to ask whether a
    /// given one is on offer rather than to show them in groups.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
        [.. Categories.SelectMany(category => category.Emoji)];

    /// <summary>
    /// What the tool stamps until something else is picked. macshot takes the first of the
    /// quick set for this (<c>StampToolHandler.swift:96-99</c>) — the finger that points at
    /// the thing the screenshot was taken of.
    /// </summary>
    public static string Default => Quick[0];
}
