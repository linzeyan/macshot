namespace Macshot.Windows.Core.Capture;

/// <summary>
/// One stretch of the exported audio: where it begins, how long it runs, which source
/// frame it starts from, and how fast it reads that source.
/// </summary>
/// <param name="OutputFrame">Where this stretch starts in the exported file.</param>
/// <param name="Frames">How many output frames it covers.</param>
/// <param name="SourceFrame">The recording's frame that its first output frame reads.</param>
/// <param name="Rate">
/// Source frames consumed per output frame. One is ordinary playback, two is double
/// speed, and zero is silence.
/// </param>
public readonly record struct AudioSpan(long OutputFrame, long Frames, long SourceFrame, double Rate)
{
    /// <summary>One past the last output frame this covers.</summary>
    public long OutputEnd => OutputFrame + Frames;

    /// <summary>Whether nothing is read for this stretch at all.</summary>
    public bool IsSilence => Rate <= 0;
}

/// <summary>
/// Turns the export's pieces into a plan for reading the recording's audio.
/// </summary>
/// <remarks>
/// <para>
/// macshot re-times audio with AVFoundation's <c>scaleTimeRange</c>, which resamples a
/// track along with the video it belongs to and lets the pitch move with it. That pitch
/// shift is not a compromise in what it does — it is what playing the same samples at a
/// different rate <em>is</em>, and it is the sound a sped-up recording is supposed to
/// have. Which means the whole of it is arithmetic: which input frame a given output
/// frame reads. No time-stretching, no phase vocoder, no third-party DSP.
/// </para>
/// <para>
/// So it lives here, where it can be tested from a Mac, and the Windows half is left with
/// nothing to decide — it extracts the recording's audio to PCM, copies frames where this
/// says to copy them, and hands the result back as one background track.
/// </para>
/// <para>
/// <strong>Why frames rather than seconds.</strong> A span's boundaries have to land on
/// whole frames or the channels go out of step from there onward, and the spans have to
/// abut exactly or the export gains a click at every cut. Rounding once, here, into
/// integers that are then summed is what guarantees both; two independent conversions
/// from seconds would not.
/// </para>
/// </remarks>
public static class AudioRetime
{
    /// <summary>
    /// The reading plan for <paramref name="pieces"/>, at <paramref name="sampleRate"/>
    /// frames per second.
    /// </summary>
    /// <remarks>
    /// One span per piece and no merging, unlike <see cref="VideoTimeline.AudioRuns"/>:
    /// that one is turning pieces into background tracks, which are expensive enough that
    /// a hundred of them matters, and this one is turning them into a loop over frames,
    /// where a hundred spans is a hundred iterations of the outer loop.
    /// </remarks>
    public static IReadOnlyList<AudioSpan> Spans(IEnumerable<VideoPiece> pieces, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        var spans = new List<AudioSpan>();
        var at = 0L;

        foreach (var piece in pieces)
        {
            var frames = (long)Math.Round(piece.OutputDuration * sampleRate);
            if (frames <= 0)
            {
                continue;
            }

            // The piece's own factor, which is already source seconds per output second
            // and so is also source frames per output frame. A freeze covers no source at
            // all, so its factor is zero, and zero is what this reads as silence — the
            // one rule covers all three kinds without naming any of them.
            spans.Add(new AudioSpan(
                at,
                frames,
                (long)Math.Round(piece.SourceStart * sampleRate),
                piece.Factor));

            // Laid end to end from the integer counts, so no span can overlap the one
            // before it or leave a gap: the export is exactly the sum of these.
            at += frames;
        }

        return spans;
    }

    /// <summary>How many frames the retimed audio runs for.</summary>
    public static long TotalFrames(IEnumerable<AudioSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        var total = 0L;
        foreach (var span in spans)
        {
            total += span.Frames;
        }

        return total;
    }

    /// <summary>
    /// The recording's frame that <paramref name="outputFrame"/> reads, or −1 where the
    /// export should write silence.
    /// </summary>
    /// <param name="sourceFrames">
    /// How many frames the recording actually holds. A span can ask for one past the end
    /// — a trim landing mid-frame, or an audio track a hair shorter than its video — and
    /// silence is the right answer rather than a clamp, which would hold the last frame.
    /// </param>
    /// <remarks>
    /// Computed from the span's own start every time rather than by stepping a cursor
    /// forward. The difference matters: at a rate of 1.7 an accumulated cursor drifts by
    /// the rounding error of every frame before it, which over an hour at 48 kHz is a
    /// quarter of a second of slip against the picture. From the span's start the error
    /// is never more than the half-frame of a single rounding, whatever the length.
    /// </remarks>
    public static long Read(AudioSpan span, long outputFrame, long sourceFrames)
    {
        if (span.IsSilence || outputFrame < span.OutputFrame || outputFrame >= span.OutputEnd)
        {
            return -1;
        }

        var frame = span.SourceFrame + (long)((outputFrame - span.OutputFrame) * span.Rate);

        return frame >= 0 && frame < sourceFrames ? frame : -1;
    }
}
