namespace Macshot.Windows.Core.Capture;

/// <summary>
/// What sound is recorded alongside the picture, and in what shape.
/// </summary>
/// <remarks>
/// <para>
/// macshot takes system audio from ScreenCaptureKit and the microphone from a separate
/// capture session, encodes each as AAC — 48 kHz stereo at 256 kbps and 48 kHz mono at
/// 128 kbps — and writes them as **two tracks** into one file, the microphone first
/// because most players decode only the first (<c>RecordingEngine.swift:171–223</c>).
/// </para>
/// <para>
/// This port writes **one** track with both mixed into it, and the reason is worth
/// stating plainly rather than leaving as a difference. A <c>MediaStreamSource</c> takes
/// a single audio descriptor, so two tracks would mean replacing the whole output half
/// of the recorder with <c>IMFSinkWriter</c>. And macshot's own comment says most
/// players decode only the first track — which is to say that on the Mac, a recording
/// made with both sources plays back as the microphone alone for most viewers. Mixing
/// gives the viewer both, at the cost of not being able to separate them afterwards.
/// </para>
/// </remarks>
public static class AudioPlan
{
    /// <summary>
    /// 48 kHz, which is what macshot pins both of its sources to and what a Windows
    /// render endpoint mixes at, so nothing has to be resampled twice.
    /// </summary>
    public const int SampleRate = 48_000;

    /// <summary>
    /// Stereo. The system mix is stereo and a mono track would fold it down; a
    /// microphone is mono and is carried in both channels.
    /// </summary>
    public const int Channels = 2;

    /// <summary>Sixteen-bit signed samples, the shape both endpoints are asked for.</summary>
    public const int BitsPerSample = 16;

    /// <summary>
    /// macshot's rate for system audio. The mixed track carries both sources, so it
    /// takes the higher of the two rates rather than the microphone's 128 kbps.
    /// </summary>
    public const uint Bitrate = 256_000;

    /// <summary>
    /// How much sound goes in one sample. Twenty milliseconds is short enough that the
    /// sound is never far behind the picture and long enough that a recording is not
    /// fifty buffer handovers a second.
    /// </summary>
    public static TimeSpan SampleDuration { get; } = TimeSpan.FromMilliseconds(20);

    /// <summary>How many frames of sound are in one sample.</summary>
    public const int FramesPerSample = SampleRate / 50;

    /// <summary>How many bytes are in one sample: frames × channels × two bytes each.</summary>
    public const int BytesPerSample = FramesPerSample * Channels * (BitsPerSample / 8);

    /// <summary>Where the sample with this index starts.</summary>
    public static TimeSpan TimestampOf(long sampleIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sampleIndex);

        // From the sample count rather than from a clock: a timestamp derived from
        // elapsed real time drifts against the samples actually written, and the drift
        // is heard as the sound sliding out of step with the picture.
        return TimeSpan.FromTicks(SampleDuration.Ticks * sampleIndex);
    }
}
