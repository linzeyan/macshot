using System.Buffers.Binary;

namespace Macshot.Windows.Core.Capture;

/// <summary>Which of a recording's two sources of sound a track carries.</summary>
public enum AudioTrackKind
{
    /// <summary>
    /// The microphone. First because macshot writes it as track 0 — the order the panel
    /// lists them in follows the order the tracks are in
    /// (<c>AudioMergeController.swift:148–150</c>).
    /// </summary>
    Microphone,

    /// <summary>Everything the machine was playing.</summary>
    System,
}

/// <summary>What was said about a finished recording's two sources.</summary>
/// <param name="Merge">
/// Whether to write the recording again with the two sources balanced as below. False is
/// macshot's <em>Keep Separate</em>: the recording is delivered as it was made.
/// </param>
public readonly record struct AudioMergeAnswer(bool Merge, double MicrophoneVolume, double SystemVolume)
{
    /// <summary>Leave the recording alone — what a dismissed panel means.</summary>
    public static AudioMergeAnswer KeepSeparate { get; } =
        new(false, AudioMerge.DefaultVolume, AudioMerge.DefaultVolume);
}

/// <summary>
/// Balancing a recording's microphone against its system audio after the fact.
/// </summary>
/// <remarks>
/// <para>
/// macshot records the two sources as two tracks in one file and offers, once the
/// recording stops, to flatten them into one at volumes the user sets
/// (<c>AudioMergeController.swift</c>). This port writes one track with both already
/// summed into it, for the reason <see cref="AudioPlan"/> gives at length — a
/// <c>MediaStreamSource</c> takes a single audio descriptor — so the same panel asks a
/// question with the same two answers about a file that is already flat: take the two
/// sources balanced as the sliders say, or take the recording as it was made.
/// </para>
/// <para>
/// Which is why the volumes cannot be honoured by touching the recording: summing throws
/// away what each source contributed. They are honoured by keeping each source's samples
/// beside the recording while it runs and mixing them again here.
/// </para>
/// <para>
/// Here rather than beside the recorder because it is arithmetic on bytes, and getting it
/// wrong is silent: a merge that read the samples a byte out of phase produces a file that
/// plays as noise rather than one that fails.
/// </para>
/// </remarks>
public static class AudioMerge
{
    /// <summary>Silence. macshot's slider floor.</summary>
    public const double MinimumVolume = 0;

    /// <summary>
    /// Half again as loud as recorded — macshot's slider ceiling
    /// (<c>AudioMergeController.swift:53</c>). Above unity on purpose: the case the panel
    /// exists for is a voice recorded too quietly against what the machine was playing,
    /// and only being able to turn the other source down would leave the whole recording
    /// quieter than it was.
    /// </summary>
    public const double MaximumVolume = 1.5;

    /// <summary>As recorded, which is where both sliders start.</summary>
    public const double DefaultVolume = 1;

    /// <summary>
    /// How far from unity a volume has to be before merging is worth doing.
    /// </summary>
    /// <remarks>
    /// A hundredth is below what anyone can hear, and the merge re-encodes the whole
    /// recording — Windows has no muxer that would put a new audio track beside an encoded
    /// video one. Without this, pressing <em>Merge Audio</em> without moving a slider would
    /// spend minutes producing a file identical to the one already on disk.
    /// </remarks>
    public const double VolumeTolerance = 0.01;

    /// <summary>The tracks, in the order the panel lists them.</summary>
    public static IReadOnlyList<AudioTrackKind> Order { get; } =
        [AudioTrackKind.Microphone, AudioTrackKind.System];

    /// <summary>
    /// What the panel calls a track. These are macshot's own labels, which are also the
    /// keys its translations are filed under, so a word changed here is a row that comes
    /// out in English in forty languages.
    /// </summary>
    public static string Label(AudioTrackKind kind) => kind switch
    {
        AudioTrackKind.Microphone => "Microphone:",
        AudioTrackKind.System => "System audio:",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Whether a finished recording gets the question at all.
    /// </summary>
    /// <remarks>
    /// Only when both sources were recorded, as macshot asks only when the file it made
    /// holds two tracks (<c>AppDelegate.swift:2597</c>). With one source there is nothing
    /// to balance it against, and a panel offering to merge one track into itself would be
    /// a question with no wrong answer.
    /// </remarks>
    public static bool IsOffered(bool systemAudio, bool microphone) => systemAudio && microphone;

    /// <summary>Holds a volume to what the sliders can express.</summary>
    public static double Clamp(double volume) =>
        double.IsNaN(volume) ? DefaultVolume : Math.Clamp(volume, MinimumVolume, MaximumVolume);

    /// <summary>
    /// Whether merging at these volumes would produce anything the recording does not
    /// already hold.
    /// </summary>
    /// <remarks>
    /// The recording carries both sources summed at unity, so unity on both sliders asks
    /// for exactly the file that already exists.
    /// </remarks>
    public static bool Rewrites(double microphoneVolume, double systemVolume) =>
        Math.Abs(Clamp(microphoneVolume) - DefaultVolume) > VolumeTolerance
        || Math.Abs(Clamp(systemVolume) - DefaultVolume) > VolumeTolerance;

    /// <summary>
    /// Sums the two sources into <paramref name="into"/> at the volumes given, as
    /// little-endian 16-bit samples.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One rounding and one clamp per sample, rather than scaling each source into place
    /// and adding the results: a microphone turned up to 1.5 would be flattened against the
    /// ceiling before the other source had been added, so the loud passages of a merge
    /// would come out quieter than the same passages of the mix already in the file.
    /// </para>
    /// <para>
    /// A source that runs out is silence from there on rather than the end of the merge.
    /// The two are written a sample at a time by the same loop while the recording runs, so
    /// they can only differ by the last sample — and losing the tail of a recording to a
    /// rounding at its very end would be far worse than a few milliseconds of quiet.
    /// </para>
    /// </remarks>
    public static void Blend(
        ReadOnlySpan<byte> microphone,
        ReadOnlySpan<byte> system,
        Span<byte> into,
        double microphoneVolume,
        double systemVolume)
    {
        if (into.Length % 2 != 0)
        {
            throw new ArgumentException("A 16-bit track is a whole number of two-byte samples.", nameof(into));
        }

        var micGain = Clamp(microphoneVolume);
        var systemGain = Clamp(systemVolume);

        for (var at = 0; at + 1 < into.Length; at += 2)
        {
            var mixed =
                (Sample(microphone, at) * micGain) + (Sample(system, at) * systemGain);

            BinaryPrimitives.WriteInt16LittleEndian(
                into.Slice(at, 2),
                (short)Math.Clamp(Math.Round(mixed), short.MinValue, short.MaxValue));
        }
    }

    private static short Sample(ReadOnlySpan<byte> track, int at) =>
        at + 1 < track.Length ? BinaryPrimitives.ReadInt16LittleEndian(track.Slice(at, 2)) : (short)0;
}
