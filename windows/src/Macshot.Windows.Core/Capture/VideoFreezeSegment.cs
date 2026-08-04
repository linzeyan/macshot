namespace Macshot.Windows.Core.Capture;

/// <summary>
/// A moment of a recording that the export holds still on.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>VideoFreezeSegment</c>. It has no source length at all — it is a point,
/// and <see cref="Hold"/> is how long the output stays on the frame found there. That
/// asymmetry is why it is its own type rather than a speed segment at factor zero:
/// division by the factor is how a speed's output length is worked out, and zero has no
/// answer.
/// </para>
/// <para>
/// The hold is silent, in this port as on macOS. Two frames' worth of audio stretched
/// over a second is a chirp, and silence is what a paused picture sounds like.
/// </para>
/// </remarks>
public readonly record struct VideoFreezeSegment(double At, double Hold)
{
    /// <summary>
    /// Below this the hold is not distinguishable from no freeze at all, which is
    /// macshot's reason for the same floor.
    /// </summary>
    public const double MinHold = 0.1;

    /// <summary>Past this it is a still image rather than an edit. macshot's ceiling.</summary>
    public const double MaxHold = 30.0;

    public const double DefaultHold = 1.0;

    /// <summary>The holds the box beside the band offers, in macshot's order.</summary>
    public static IReadOnlyList<double> PresetHolds { get; } = [0.25, 0.5, 1.0, 2.0, 3.0, 5.0];

    /// <summary>
    /// How near an end of the recording a freeze may be placed.
    /// </summary>
    /// <remarks>
    /// macshot's epsilon, and its reason: <see cref="VideoCuts.KeptRanges"/> treats a
    /// boundary as outside, so a freeze placed at exactly 0 or exactly the duration falls
    /// in no kept range and is dropped without saying so.
    /// </remarks>
    public const double EdgeMargin = 0.001;

    public static double ClampHold(double hold) => Math.Clamp(hold, MinHold, MaxHold);

    /// <summary>A freeze at <paramref name="at"/>, kept off both ends of the recording.</summary>
    public static VideoFreezeSegment Placed(double at, double totalSeconds, double hold = DefaultHold) =>
        new(
            Math.Clamp(at, EdgeMargin, Math.Max(EdgeMargin, totalSeconds - EdgeMargin)),
            ClampHold(hold));

    /// <summary>Slides the freeze to <paramref name="at"/>. A freeze has no edges to drag.</summary>
    public VideoFreezeSegment MovedTo(double at, double totalSeconds) =>
        this with { At = Math.Clamp(at, EdgeMargin, Math.Max(EdgeMargin, totalSeconds - EdgeMargin)) };

    public VideoFreezeSegment WithHold(double hold) => this with { Hold = ClampHold(hold) };
}
