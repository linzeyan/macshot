namespace Macshot.Windows.Core.Capture;

/// <summary>
/// Which row of the effects band each pill goes on.
/// </summary>
/// <remarks>
/// macshot's <c>layoutRows</c>: greedy interval-graph colouring, which is a long name for
/// "put each pill on the lowest row nothing already there collides with". A band with one
/// effect on it is one row tall, and it only grows when two effects genuinely overlap in
/// time — which is the only case where stacking tells the user anything.
/// </remarks>
public static class VideoBandRows
{
    /// <summary>The row for each span, in the order the spans were given.</summary>
    /// <remarks>
    /// Row 0 is the bottom one, as it is on macOS. Packed in start order rather than in
    /// list order so that the assignment depends on where the pills are rather than on
    /// which was added first; without it, adding an effect at the beginning of the
    /// recording would push everything already placed up a row.
    /// </remarks>
    public static IReadOnlyList<int> Assign(IReadOnlyList<VideoTimeRange> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        var rows = new int[spans.Count];
        var taken = new List<List<VideoTimeRange>>();

        foreach (var index in Enumerable.Range(0, spans.Count).OrderBy(i => spans[i].Start))
        {
            var span = spans[index];
            var placed = -1;

            for (var row = 0; row < taken.Count; row++)
            {
                if (!taken[row].Any(other => span.Start < other.End && span.End > other.Start))
                {
                    placed = row;
                    break;
                }
            }

            if (placed < 0)
            {
                taken.Add([]);
                placed = taken.Count - 1;
            }

            taken[placed].Add(span);
            rows[index] = placed;
        }

        return rows;
    }

    /// <summary>How many rows an assignment needs. Never fewer than one.</summary>
    /// <remarks>
    /// One even with nothing on the band, because a band that collapses to no height when
    /// the last effect is deleted takes the place a user is about to click on with it.
    /// </remarks>
    public static int RowCount(IReadOnlyList<int> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows.Count == 0 ? 1 : rows.Max() + 1;
    }
}
