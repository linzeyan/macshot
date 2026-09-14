namespace Macshot.Windows.Core.Capture;

/// <summary>
/// Decides when a recording should stop waiting for the compositor and take a frame the
/// way a screenshot is taken.
/// </summary>
/// <remarks>
/// <para>
/// Windows Graphics Capture hands a session a frame the moment it opens, so a recording
/// that has kept nothing at all once <see cref="StarvedAfter"/> has passed is not looking
/// at a still screen — a still screen still gives up its opening frame — but at a session
/// that is not working. One
/// Windows 10 machine reported exactly that: fifteen seconds of <c>0 frames, 0 dropped</c>
/// while screenshots on the same machine were fine, which produced a file of one frame
/// repeated a thousand times.
/// </para>
/// <para>
/// Taking one by hand costs an entire capture session — measured at about 92ms, spent
/// inside the encoder's sample request — so the answer here is nearly always no. It is in
/// Core because it is a state machine with four ways to be wrong and no window in sight,
/// and because the first version of it read <c>elapsed - TimeSpan.MinValue</c>, which
/// overflows: every recording ended a second after it started, and only a measurement on a
/// real machine found it.
/// </para>
/// </remarks>
public sealed class RetakeCadence
{
    /// <summary>
    /// How long the compositor is given to deliver its first frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A working session answers in well under a tenth of a second: measured across three
    /// VM recordings, the first frame arrived at 55, 65 and 73ms. This is four times the
    /// slowest of those, which leaves a loaded machine room without making the wait itself
    /// the defect.
    /// </para>
    /// <para>
    /// It was a whole second while a retake meant opening a second capture session, which
    /// could sit out a two-second frame timeout — being early was expensive, so the margin
    /// was generous. A retake is a screen copy now, measured against the compositor's own
    /// frames as pixel for pixel identical, so being early costs one cheap copy of a frame
    /// that looks the same and stops for good at the first real one. The asymmetry ran the
    /// other way: on the machine this exists for, the wait was a visibly frozen second at
    /// the head of every recording.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan StarvedAfter = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// The fastest the by-hand path may run. Ten a second is coarse for a recording and
    /// enormously better than one frame for the whole of it.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// How many failures in a row retire the fallback. A machine where this does not work
    /// either fails every time and pays the capture timeout for each attempt, which is worse
    /// than the still image it was trying to avoid.
    /// </summary>
    public const int Attempts = 3;

    private TimeSpan? _at;
    private int _failures;

    /// <summary>Frames taken by hand. Zero wherever recording works at all.</summary>
    public int Taken { get; private set; }

    /// <summary>
    /// Whether to take one now, and marks the attempt if so.
    /// </summary>
    /// <param name="kept">
    /// Frames the compositor has delivered. One of them retires this for good: the fallback
    /// exists for a session that never works, not for a screen that has stopped moving.
    /// </param>
    /// <param name="elapsed">How long the recording has been running.</param>
    /// <param name="paused">
    /// Whether the recording is being held. A pause is meant to leave an absence in the
    /// file, so filling it with frames taken by hand would defeat it.
    /// </param>
    public bool ShouldTake(int kept, TimeSpan elapsed, bool paused)
    {
        if (kept > 0 || _failures >= Attempts || paused || elapsed < StarvedAfter)
        {
            return false;
        }

        if (_at is { } last && elapsed - last < Interval)
        {
            return false;
        }

        // Marked before the capture rather than after it, so that one taking longer than the
        // interval does not come straight back round for another.
        _at = elapsed;
        return true;
    }

    /// <summary>What came of the attempt <see cref="ShouldTake"/> just allowed.</summary>
    public void Record(bool took)
    {
        if (took)
        {
            _failures = 0;
            Taken++;
            return;
        }

        _failures++;
    }
}
