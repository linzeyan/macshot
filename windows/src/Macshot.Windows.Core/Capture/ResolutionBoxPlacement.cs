namespace Macshot.Windows.Core.Capture;

/// <summary>
/// Where the width × height box sits relative to the region being captured.
/// </summary>
/// <remarks>
/// <para>
/// It belongs to the selection rather than to the screen, so it reads as the size of the
/// thing under it and not as a status line about something elsewhere. That means it has to
/// find a place near an edge that is on screen and clear of the toolbar, which is what this
/// works out.
/// </para>
/// <para>
/// Top-left origin, like every other region in the port. The macOS original is written
/// against AppKit's bottom-left, so its "above" is arithmetic below and vice versa.
/// </para>
/// </remarks>
public static class ResolutionBoxPlacement
{
    /// <summary>
    /// How far off the selection's edge the box sits: clear of the resize grips, which
    /// straddle it.
    /// </summary>
    public const double EdgeGap = 8;

    /// <summary>How close to the screen's edge the box may come.</summary>
    private const double ScreenMargin = 2;

    /// <summary>How close to the selection's edge it may come when it sits inside.</summary>
    private const double InsideMargin = 2;

    /// <summary>
    /// The slack around something being avoided. A box that only just misses the toolbar
    /// reads as touching it.
    /// </summary>
    private const double AvoidanceSlack = 4;

    /// <summary>
    /// Places a box of <paramref name="size"/> against <paramref name="selection"/>.
    /// </summary>
    /// <param name="selection">The region being captured.</param>
    /// <param name="screen">What the box must stay inside.</param>
    /// <param name="size">How big the box is; its position is ignored.</param>
    /// <param name="avoid">What the box should not cover, the toolbar strips in practice.</param>
    /// <param name="dimensionsCenter">
    /// How far from the box's left edge the middle of the "W × H" reading is. The presets
    /// button hangs off the right, and centring the whole box would leave the numbers
    /// visibly off to one side of the region they describe.
    /// </param>
    public static CaptureRegion For(
        CaptureRegion selection,
        CaptureRegion screen,
        CaptureRegion size,
        IReadOnlyList<CaptureRegion>? avoid = null,
        double? dimensionsCenter = null)
    {
        var occupied = avoid ?? [];

        var left = Math.Clamp(
            selection.X + (selection.Width / 2) - (dimensionsCenter ?? (size.Width / 2)),
            screen.X + ScreenMargin,
            Math.Max(screen.X + ScreenMargin, screen.Right - size.Width - ScreenMargin));

        CaptureRegion At(double top) => new(left, top, size.Width, size.Height);

        bool OnScreen(CaptureRegion box) =>
            box.Y >= screen.Y + ScreenMargin && box.Bottom <= screen.Bottom - ScreenMargin;

        double Blocked(CaptureRegion box) => occupied.Sum(other => Overlap(box, other));

        var above = At(selection.Y - size.Height - EdgeGap);
        var below = At(selection.Bottom + EdgeGap);

        foreach (var outside in new[] { above, below })
        {
            if (OnScreen(outside) && Blocked(outside) == 0)
            {
                return outside;
            }
        }

        // Nowhere outside is clear, so the box goes inside the selection — at whichever
        // end it could not sit beside. A box pushed off the bottom of the screen belongs
        // just inside the top edge, not inside the bottom one it was already crowded out
        // of.
        var insideTop = At(selection.Y + EdgeGap);
        var insideBottom = At(selection.Bottom - size.Height - EdgeGap);
        var inside = !OnScreen(below) && OnScreen(above)
            ? new[] { insideBottom, insideTop }
            : [insideTop, insideBottom];

        var usableInside = inside
            .Where(box => OnScreen(box) && FitsInside(box, selection))
            .ToArray();

        foreach (var candidate in usableInside)
        {
            if (Blocked(candidate) == 0)
            {
                return candidate;
            }
        }

        // Nothing is clear anywhere, so the least covered place wins: half a box behind
        // the toolbar still says more than a box off the edge of the screen.
        var reachable = new[] { above, below }
            .Concat(usableInside)
            .Where(OnScreen)
            .ToArray();

        if (reachable.Length > 0)
        {
            return reachable.MinBy(Blocked);
        }

        // A selection taller than the screen it is on: the box goes as near the top edge
        // as the screen allows, which is the only place left.
        return At(Math.Clamp(
            above.Y,
            screen.Y + ScreenMargin,
            Math.Max(screen.Y + ScreenMargin, screen.Bottom - size.Height - ScreenMargin)));
    }

    private static bool FitsInside(CaptureRegion box, CaptureRegion selection) =>
        box.Y >= selection.Y + InsideMargin && box.Bottom <= selection.Bottom - InsideMargin;

    private static double Overlap(CaptureRegion box, CaptureRegion other)
    {
        if (other.IsEmpty)
        {
            return 0;
        }

        var slackened = new CaptureRegion(
            other.X - AvoidanceSlack,
            other.Y - AvoidanceSlack,
            other.Width + (AvoidanceSlack * 2),
            other.Height + (AvoidanceSlack * 2));

        var hit = box.Intersect(slackened);
        return hit.Width * hit.Height;
    }
}
