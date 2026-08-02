namespace Macshot.Windows.Core.Annotations;

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
    /// Everything the picker behind the row offers: the quick set and then the rest.
    /// </summary>
    /// <remarks>
    /// The quick set comes first and whole, so the picker never contradicts the row — an
    /// emoji reachable in one click that the picker did not know about would show as
    /// nothing chosen the moment the picker was opened.
    /// </remarks>
    public static IReadOnlyList<string> All { get; } =
    [
        .. Quick,
        "\U0001F389", "\U0001F914", "\U0001F44F", "\U0001F680", "\U0001F512",
    ];

    /// <summary>
    /// What the tool stamps until the user picks something else. Thumbs up: the mark most
    /// often wanted, and the one whose meaning survives being seen out of context.
    /// </summary>
    public static string Default => "\U0001F44D";
}
