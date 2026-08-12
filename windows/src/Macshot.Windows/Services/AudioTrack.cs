using System.Runtime.InteropServices.WindowsRuntime;
using Macshot.Windows.Core.Capture;

using Windows.Media.Core;

namespace Macshot.Windows.Services;

/// <summary>
/// The clock a recording keeps, as the sound needs to see it.
/// </summary>
/// <remarks>
/// The sound is paced against the picture rather than against real time, which is what
/// makes a pause come out as an absence in both at once.
/// </remarks>
internal interface IRecordingClock
{
    /// <summary>How much of the recording has been written so far.</summary>
    TimeSpan Elapsed { get; }

    /// <summary>Whether the recording is being held.</summary>
    bool IsPaused { get; }
}

/// <summary>
/// The one audio track a recording carries, with whatever sources were asked for mixed
/// into it.
/// </summary>
/// <remarks>
/// <para>
/// Samples are produced on demand and paced against the recording's own clock, not
/// against a timer: <see cref="MediaStreamSource"/> asks for the next sample as soon as
/// it has the last, and a track that answered immediately would encode an hour of
/// silence before the first second of video was recorded.
/// </para>
/// <para>
/// A held recording is drained and thrown away rather than left to pile up. The
/// alternative is resuming into however many minutes of sound the pause lasted, played
/// against the picture from the moment the pause ended.
/// </para>
/// </remarks>
internal sealed class AudioTrack : IDisposable
{
    /// <summary>
    /// How long to wait when the recording has not yet reached the next sample. Short
    /// against a sample's twenty milliseconds, so the wait ends near where it should
    /// rather than a whole sample late.
    /// </summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(5);

    private readonly AudioEndpoint? _system;
    private readonly AudioEndpoint? _microphone;
    private readonly AudioSampleBuffer _systemSamples = new();
    private readonly AudioSampleBuffer _microphoneSamples = new();

    /// <summary>
    /// Each source kept apart as well as mixed, or null when there is only one of them.
    /// </summary>
    /// <remarks>
    /// The merge panel's per-track volumes can only be honoured from these: the track this
    /// class writes has both summed into it, and a sum cannot be taken apart again.
    /// </remarks>
    private readonly AudioSidecar? _sidecar;

    private readonly short[] _mixed = new short[AudioPlan.FramesPerSample * AudioPlan.Channels];
    private readonly short[] _source = new short[AudioPlan.FramesPerSample * AudioPlan.Channels];

    private long _index;
    private bool _disposed;

    private AudioTrack(AudioEndpoint? system, AudioEndpoint? microphone, AudioSidecar? sidecar)
    {
        _system = system;
        _microphone = microphone;
        _sidecar = sidecar;
    }

    /// <summary>
    /// The recording's two sources as files of their own, once the track has been closed.
    /// </summary>
    /// <remarks>
    /// Null for a recording with one source, and null when keeping them failed or ran past
    /// what <see cref="AudioSidecar"/> will hold — in which case the question the panel
    /// asks has no answer that could be honoured, and it is not asked.
    /// </remarks>
    public RecordedAudioTracks? SeparateTracks => _sidecar?.Files;

    /// <summary>
    /// Opens the sources asked for, or null when none were asked for or none could be
    /// opened.
    /// </summary>
    /// <remarks>
    /// Null is what tells the recorder to build a file with no audio stream in it at
    /// all, which is the right file for a machine with nothing to record from — an
    /// empty audio track would leave the encoder waiting for samples that never come.
    /// </remarks>
    public static AudioTrack? Open(bool system, bool microphone)
    {
        var speakers = system ? AudioEndpoint.Open(AudioSource.System) : null;
        var mic = microphone ? AudioEndpoint.Open(AudioSource.Microphone) : null;

        if (speakers is null && mic is null)
        {
            return null;
        }

        // Only when the merge panel would be offered at all: keeping the sources apart is
        // for balancing one against the other, and one source has nothing to be balanced
        // against.
        return new AudioTrack(
            speakers,
            mic,
            AudioMerge.IsOffered(speakers is not null, mic is not null) ? AudioSidecar.Open() : null);
    }

    public void Start()
    {
        _system?.Start();
        _microphone?.Start();
    }

    /// <summary>
    /// The next sample, or null once the recording has stopped — which is what ends the
    /// audio stream and lets the file be written.
    /// </summary>
    public async Task<MediaStreamSample?> NextSampleAsync(IRecordingClock clock, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(clock);

        while (!cancellation.IsCancellationRequested && !_disposed)
        {
            Drain();

            if (clock.IsPaused)
            {
                // Held: what the endpoints produced meanwhile is not part of the
                // recording, so it is dropped rather than carried across the pause.
                _systemSamples.Take(_source);
                _microphoneSamples.Take(_source);
                await Task.Delay(Tick, CancellationToken.None);
                continue;
            }

            if (clock.Elapsed >= AudioPlan.TimestampOf(_index + 1))
            {
                return Mix();
            }

            await Task.Delay(Tick, CancellationToken.None);
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _system?.Dispose();
        _microphone?.Dispose();

        // Last: this is what rewrites each file's header with the length it turned out to
        // be, so the pair only becomes readable here.
        _sidecar?.Dispose();
    }

    private void Drain()
    {
        _system?.Drain(_systemSamples);
        _microphone?.Drain(_microphoneSamples);
    }

    /// <summary>
    /// Builds one sample out of whatever each source has, silence standing in for
    /// whatever it does not.
    /// </summary>
    private MediaStreamSample Mix()
    {
        Array.Clear(_mixed);

        if (_system is not null)
        {
            _systemSamples.Take(_mixed);
        }

        if (_microphone is not null)
        {
            // The microphone endpoint is asked for stereo like the other one, so it
            // arrives already in both channels and is simply added in.
            _microphoneSamples.Take(_source);

            // Before the sum rather than after it: at this point _mixed still holds the
            // system audio alone, and once the microphone has been added neither source
            // can be recovered from it.
            _sidecar?.Write(_source, _mixed);

            AudioMixing.MixInto(_mixed, _source);
        }

        // A buffer of its own per sample: the encoder holds what it is handed for as
        // long as it likes, and a reused array would be rewritten underneath it.
        var bytes = new byte[AudioPlan.BytesPerSample];
        AudioMixing.WriteBytes(_mixed, bytes);

        var sample = MediaStreamSample.CreateFromBuffer(bytes.AsBuffer(), AudioPlan.TimestampOf(_index));
        sample.Duration = AudioPlan.SampleDuration;
        _index++;
        return sample;
    }
}
