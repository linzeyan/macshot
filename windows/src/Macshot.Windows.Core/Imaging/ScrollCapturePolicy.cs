namespace Macshot.Windows.Core.Imaging;

/// <summary>Why a scroll capture stopped, or that it has not.</summary>
public enum ScrollCaptureStop
{
    /// <summary>Still going.</summary>
    None,

    /// <summary>The view stopped moving, which is what the bottom of a page looks like.</summary>
    Complete,

    /// <summary>Frames stopped matching, so what is stitched is all that can be trusted.</summary>
    LostTrack,

    /// <summary>The height limit was reached before the view ran out.</summary>
    HeightLimit,
}

/// <summary>
/// Decides when a scroll capture has finished, from what each frame did to the
/// stitched image.
/// </summary>
/// <remarks>
/// <para>
/// Kept apart from the capture loop because it is the part that can be wrong in
/// ways nobody sees: stopping one frame early loses the bottom of the page,
/// stopping late pads the image with a repeated last screen, and never stopping
/// grows the buffer until the machine gives out. None of that needs a real window
/// to exercise, so none of it should need one to test.
/// </para>
/// <para>
/// Every threshold below counts consecutive frames rather than elapsed time. A
/// slow machine takes longer to produce the same frames; it should not thereby get
/// a different capture.
/// </para>
/// </remarks>
public sealed class ScrollCapturePolicy
{
    /// <summary>
    /// How many frames in a row may match where they already sit before the view is
    /// called finished. More than one, because a scroll that is still animating can
    /// deliver a repeat between two real advances; a page that has genuinely stopped
    /// delivers them forever.
    /// </summary>
    public const int UnchangedFramesBeforeComplete = 3;

    /// <summary>
    /// How many frames in a row may fail to match before the capture gives up. A
    /// single rejection is ordinary — a frame caught mid-repaint, a banner animating
    /// through the match band — while a run of them means the view is no longer the
    /// one that was being followed.
    /// </summary>
    public const int RejectedFramesBeforeLostTrack = 4;

    private readonly int _maximumHeight;
    private int _unchanged;
    private int _rejected;

    /// <param name="maximumHeight">
    /// Rows past which the capture stops regardless. A feed that loads more as it is
    /// scrolled never reaches a bottom, and without a ceiling the capture would run
    /// until the buffer exhausted the machine.
    /// </param>
    public ScrollCapturePolicy(int maximumHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHeight);
        _maximumHeight = maximumHeight;
    }

    /// <summary>Whether the capture should stop, having taken one more frame.</summary>
    public ScrollCaptureStop Observe(ScrollStitchOutcome outcome, int stitchedHeight)
    {
        switch (outcome)
        {
            case ScrollStitchOutcome.Seeded:
            case ScrollStitchOutcome.Advanced:
                // Progress clears both counts: a run of rejections the view recovered
                // from was noise, not the view being lost.
                _unchanged = 0;
                _rejected = 0;
                break;

            case ScrollStitchOutcome.Unchanged:
                _unchanged++;
                break;

            case ScrollStitchOutcome.Rejected:
                _rejected++;
                break;
        }

        // Checked before the counters, because reaching the ceiling is a real result
        // and a page that also happens to have stopped should not report otherwise.
        if (stitchedHeight >= _maximumHeight)
        {
            return ScrollCaptureStop.HeightLimit;
        }

        if (_unchanged >= UnchangedFramesBeforeComplete)
        {
            return ScrollCaptureStop.Complete;
        }

        return _rejected >= RejectedFramesBeforeLostTrack
            ? ScrollCaptureStop.LostTrack
            : ScrollCaptureStop.None;
    }
}
