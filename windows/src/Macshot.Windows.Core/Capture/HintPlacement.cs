namespace Macshot.Windows.Core.Capture;

/// <summary>
/// Where the overlay's hint pill sits relative to the region it is talking about.
/// </summary>
/// <remarks>
/// <para>
/// Below the region, not above it. macshot puts its helper text under the selection
/// (<c>OverlayView.swift:2294-2301</c>) because the resolution box owns the space above,
/// and a line of instructions and a reading of the size cannot both have it. The port had
/// them both preferring above, and the box — drawn on the later layer — sat on the pill.
/// </para>
/// <para>
/// macOS gets away with only two sides because it shows the helper text while the drag is
/// happening and never afterwards. This pill also carries what a tool is doing, which is
/// said with the toolbar up and the toolbar underneath the region, so a side being taken
/// is the normal case rather than the crowded one: it slides past what is in the way
/// instead of giving up on the side.
/// </para>
/// <para>Top-left origin, so "above" here is a smaller Y, unlike the AppKit original.</para>
/// </remarks>
public static class HintPlacement
{
    /// <summary>How far off the region's edge the pill sits (<c>OverlayView.swift:2296</c>).</summary>
    public const double EdgeGap = 8;

    /// <summary>How close to the screen's edge it may come (<c>OverlayView.swift:2303</c>).</summary>
    private const double ScreenMargin = 4;

    /// <summary>
    /// The slack around something being avoided, and the gap left when the pill is pushed
    /// clear of it. A pill that only just misses the tool strip reads as touching it.
    /// </summary>
    private const double AvoidanceSlack = 4;

    /// <summary>
    /// Places a pill of <paramref name="size"/> against <paramref name="anchor"/>.
    /// </summary>
    /// <param name="anchor">What the pill is about: the region, or the frame around it.</param>
    /// <param name="screen">What the pill must stay inside.</param>
    /// <param name="size">How big the pill measured; its position is ignored.</param>
    /// <param name="avoid">What it should not cover — the size box and the toolbar strips.</param>
    public static CaptureRegion For(
        CaptureRegion anchor,
        CaptureRegion screen,
        CaptureRegion size,
        IReadOnlyList<CaptureRegion>? avoid = null)
    {
        var occupied = (avoid ?? []).Where(other => !other.IsEmpty).ToArray();

        var left = Math.Clamp(
            anchor.X + ((anchor.Width - size.Width) / 2),
            screen.X + ScreenMargin,
            Math.Max(screen.X + ScreenMargin, screen.Right - size.Width - ScreenMargin));

        CaptureRegion At(double top) => new(left, top, size.Width, size.Height);

        bool OnScreen(CaptureRegion pill) =>
            pill.Y >= screen.Y + ScreenMargin && pill.Bottom <= screen.Bottom - ScreenMargin;

        var below = Cleared(At(anchor.Bottom + EdgeGap), occupied, downwards: true);
        if (OnScreen(below))
        {
            return below;
        }

        var above = Cleared(At(anchor.Y - size.Height - EdgeGap), occupied, downwards: false);
        if (OnScreen(above))
        {
            return above;
        }

        // Neither side has the room, so the pill goes wherever it covers least — a line
        // half behind the toolbar can still be read, and one off the screen cannot.
        var reachable = new[] { below, above }.MinBy(Blocked)!;
        return At(Math.Clamp(
            reachable.Y,
            screen.Y + ScreenMargin,
            Math.Max(screen.Y + ScreenMargin, screen.Bottom - size.Height - ScreenMargin)));

        double Blocked(CaptureRegion pill) => occupied.Sum(other => Overlap(pill, other));
    }

    /// <summary>
    /// Pushes the pill past everything it lands on, in the direction it was already going.
    /// </summary>
    /// <remarks>
    /// Bounded by how many rectangles there are rather than run until it settles: each
    /// pass clears at least one of them, and two that push back at each other would
    /// otherwise spin here forever.
    /// </remarks>
    private static CaptureRegion Cleared(
        CaptureRegion pill, IReadOnlyList<CaptureRegion> occupied, bool downwards)
    {
        for (var pass = 0; pass < occupied.Count; pass++)
        {
            var moved = false;

            foreach (var other in occupied)
            {
                if (Overlap(pill, other) == 0)
                {
                    continue;
                }

                pill = new CaptureRegion(
                    pill.X,
                    downwards
                        ? other.Bottom + AvoidanceSlack
                        : other.Y - AvoidanceSlack - pill.Height,
                    pill.Width,
                    pill.Height);
                moved = true;
            }

            if (!moved)
            {
                break;
            }
        }

        return pill;
    }

    private static double Overlap(CaptureRegion pill, CaptureRegion other)
    {
        var slackened = new CaptureRegion(
            other.X - AvoidanceSlack,
            other.Y - AvoidanceSlack,
            other.Width + (AvoidanceSlack * 2),
            other.Height + (AvoidanceSlack * 2));

        var hit = pill.Intersect(slackened);
        return hit.Width * hit.Height;
    }
}
