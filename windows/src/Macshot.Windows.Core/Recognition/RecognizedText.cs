using System.Text;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Recognition;

/// <summary>One word an OCR engine found, with where it sits in frame space.</summary>
public sealed record RecognizedWord(string Text, CaptureRegion Bounds);

/// <summary>
/// A line of recognized text, kept as words rather than as a string.
/// </summary>
/// <remarks>
/// Detection has to run over the whole line — an email address is several words to
/// an OCR engine — but redaction has to come back out as boxes, and only the words
/// carry boxes. This type owns that mapping: it builds the line text and remembers
/// where each word starts in it, so a match found by character offset can be turned
/// back into the words it covers.
/// </remarks>
public sealed class RecognizedLine
{
    private readonly int[] _wordOffsets;

    public RecognizedLine(IEnumerable<RecognizedWord> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        var collected = words.ToArray();
        if (collected.Length == 0)
        {
            throw new ArgumentException("A recognized line needs at least one word.", nameof(words));
        }

        var builder = new StringBuilder();
        _wordOffsets = new int[collected.Length];
        for (var index = 0; index < collected.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(' ');
            }

            _wordOffsets[index] = builder.Length;
            builder.Append(collected[index].Text);
        }

        Words = collected;
        Text = builder.ToString();
    }

    public IReadOnlyList<RecognizedWord> Words { get; }

    /// <summary>The words joined by single spaces, which is what detection reads.</summary>
    public string Text { get; }

    /// <summary>
    /// The box the whole line occupies, in frame space.
    /// </summary>
    /// <remarks>
    /// Built from the words rather than reported by the engine, so it is the box the
    /// glyphs actually sit in. It is what anything working with a line as a thing on the
    /// screen needs — a highlighter snapping to it, a redaction covering it — where the
    /// per-word boxes answer the different question of which words a match covered.
    /// </remarks>
    public CaptureRegion Bounds =>
        Words.Aggregate(default(CaptureRegion), (box, word) => box.Union(word.Bounds));

    /// <summary>
    /// The words touched by a character range. A partial overlap counts: half an
    /// email address left visible is not a redaction.
    /// </summary>
    public IEnumerable<RecognizedWord> WordsOverlapping(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        var end = start + length;
        for (var index = 0; index < Words.Count; index++)
        {
            var wordStart = _wordOffsets[index];
            var wordEnd = wordStart + Words[index].Text.Length;
            if (wordStart < end && start < wordEnd)
            {
                yield return Words[index];
            }
        }
    }
}
