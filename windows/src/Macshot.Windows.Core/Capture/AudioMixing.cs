namespace Macshot.Windows.Core.Capture;

/// <summary>
/// Puts two sources of sound into one track's worth of samples.
/// </summary>
/// <remarks>
/// Here rather than beside the capture code because it is arithmetic on numbers, which
/// is the half of recording that can be tested without a sound card. Everything that
/// touches an audio endpoint is in the app layer and is compile-checked only.
/// </remarks>
public static class AudioMixing
{
    /// <summary>
    /// Adds <paramref name="addition"/> into <paramref name="track"/>, sample for
    /// sample, and clips rather than wrapping.
    /// </summary>
    /// <remarks>
    /// Summing is what mixing is, and two loud sources can sum past what a 16-bit
    /// sample holds. Wrapping turns that into a burst of noise at full scale, which is
    /// far worse than the flattened peak clipping gives — and worse than halving both,
    /// which would make every recording quiet to guard against a moment that usually
    /// never comes.
    /// </remarks>
    public static void MixInto(Span<short> track, ReadOnlySpan<short> addition)
    {
        var shared = Math.Min(track.Length, addition.Length);
        for (var index = 0; index < shared; index++)
        {
            track[index] = (short)Math.Clamp(track[index] + addition[index], short.MinValue, short.MaxValue);
        }
    }

    /// <summary>
    /// Writes a mono source across both channels of a stereo track.
    /// </summary>
    /// <remarks>
    /// A microphone is one channel and the track is two. Carried in both rather than in
    /// the left, which is what leaving it as-is would do: a voice arriving in one ear is
    /// heard as a fault in the recording.
    /// </remarks>
    public static void SpreadInto(Span<short> stereo, ReadOnlySpan<short> mono)
    {
        var frames = Math.Min(stereo.Length / 2, mono.Length);
        for (var frame = 0; frame < frames; frame++)
        {
            var sample = mono[frame];
            stereo[frame * 2] = sample;
            stereo[(frame * 2) + 1] = sample;
        }
    }

    /// <summary>
    /// Turns interleaved 16-bit samples into the bytes a buffer carries, little-endian.
    /// </summary>
    public static void WriteBytes(ReadOnlySpan<short> samples, Span<byte> into)
    {
        if (into.Length < samples.Length * 2)
        {
            throw new ArgumentException("The destination is smaller than the samples.", nameof(into));
        }

        for (var index = 0; index < samples.Length; index++)
        {
            var value = (ushort)samples[index];
            into[index * 2] = (byte)value;
            into[(index * 2) + 1] = (byte)(value >> 8);
        }
    }
}
