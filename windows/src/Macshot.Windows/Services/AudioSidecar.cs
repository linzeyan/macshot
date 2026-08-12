using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Services;

/// <summary>
/// Each source of a finished recording's sound, kept as a file of its own.
/// </summary>
/// <remarks>
/// What the merge panel needs and the recording cannot give it. The MP4 carries one track
/// with both sources summed into it (<see cref="AudioPlan"/>), and summing is what destroys
/// the balance between them: no volume set afterwards can be honoured from the file alone.
/// These are the same samples before the sum.
/// </remarks>
public sealed record RecordedAudioTracks(string MicrophonePath, string SystemPath)
{
    /// <summary>
    /// Throws both away once the question has been answered.
    /// </summary>
    /// <remarks>
    /// Best effort. They are minutes of uncompressed audio in the temporary directory and
    /// leaving one behind is untidy; refusing to deliver a recording because one would not
    /// delete would be worse, and the name says what left it there.
    /// </remarks>
    public void Discard()
    {
        foreach (var path in new[] { MicrophonePath, SystemPath })
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                DiagnosticLog.Write($"Could not delete the audio track at '{path}': {error.Message}");
            }
        }
    }
}

/// <summary>
/// Writes each source of a recording's sound to a WAV of its own while the recording runs.
/// </summary>
/// <remarks>
/// <para>
/// Fed from the same loop that builds the mixed track, one twenty-millisecond sample at a
/// time, so the two files are frame-for-frame in step with each other and with the
/// recording. Anything that resampled or re-derived them afterwards would have to solve
/// that alignment again.
/// </para>
/// <para>
/// Uncompressed, because they exist to be read back as numbers and WAV is the only thing
/// this port both writes and reads that way — the same reason
/// <see cref="Macshot.Windows.Core.Capture.WavAudio"/> exists at all. That costs 192 KB a
/// second per source, which is why they are only opened when both sources are recording:
/// with one there is no question to ask and nothing to keep them for.
/// </para>
/// </remarks>
internal sealed class AudioSidecar : IDisposable
{
    /// <summary>
    /// How much of one source will be kept, in bytes. About thirty-four minutes at the
    /// rate this port records.
    /// </summary>
    /// <remarks>
    /// Past it the pair is abandoned and the panel is not offered, rather than filling the
    /// user's temporary directory for a question about a recording far longer than the one
    /// the panel is for. <see cref="Macshot.Windows.Services.VideoEffectsCompositor"/> caps
    /// the track it reads at the same place for the same reason.
    /// </remarks>
    private const long LargestTrackBytes = 400L * 1024 * 1024;

    private readonly string _microphonePath;
    private readonly string _systemPath;
    private readonly FileStream _microphone;
    private readonly FileStream _system;
    private readonly byte[] _bytes = new byte[AudioPlan.BytesPerSample];

    private long _written;
    private bool _abandoned;
    private bool _disposed;

    private AudioSidecar(string microphonePath, string systemPath, FileStream microphone, FileStream system)
    {
        _microphonePath = microphonePath;
        _systemPath = systemPath;
        _microphone = microphone;
        _system = system;
    }

    /// <summary>
    /// The two files, or null while there is no complete pair to read.
    /// </summary>
    /// <remarks>
    /// Null until the writing has stopped, because a header written for a length that is
    /// still growing describes a file nothing can read to the end of.
    /// </remarks>
    public RecordedAudioTracks? Files =>
        _disposed && !_abandoned ? new RecordedAudioTracks(_microphonePath, _systemPath) : null;

    /// <summary>
    /// Opens a pair beside the recording, or nothing when the temporary directory will not
    /// take them.
    /// </summary>
    /// <remarks>
    /// Nothing rather than a failure: the recording itself is unaffected, and losing the
    /// merge panel is not worth losing the recording over.
    /// </remarks>
    public static AudioSidecar? Open()
    {
        // One id across both, so a pair left behind by a crash is recognisable as a pair.
        var id = Guid.NewGuid().ToString("N")[..8];
        var microphonePath = Path.Combine(Path.GetTempPath(), $"macshot-{id}-microphone.wav");
        var systemPath = Path.Combine(Path.GetTempPath(), $"macshot-{id}-system.wav");

        FileStream? microphone = null;

        try
        {
            microphone = Create(microphonePath);
            var system = Create(systemPath);

            return new AudioSidecar(microphonePath, systemPath, microphone, system);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Write($"Could not keep the recording's audio sources apart: {error.Message}");
            microphone?.Dispose();
            return null;
        }
    }

    /// <summary>Appends one sample of each source.</summary>
    public void Write(ReadOnlySpan<short> microphone, ReadOnlySpan<short> system)
    {
        if (_disposed || _abandoned)
        {
            return;
        }

        try
        {
            AudioMixing.WriteBytes(microphone, _bytes);
            _microphone.Write(_bytes);
            AudioMixing.WriteBytes(system, _bytes);
            _system.Write(_bytes);

            _written += _bytes.Length;
            if (_written > LargestTrackBytes)
            {
                Abandon($"the recording passed {LargestTrackBytes / (1024 * 1024)} MB of audio per source");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A full disk, most likely. The recording is still being written and is worth
            // far more than the question about it, so the pair goes and the recording
            // carries on.
            Abandon(error.Message);
        }
    }

    /// <summary>
    /// Finishes both files: the header each was opened with says nothing about how long it
    /// turned out to be, and is rewritten here now that it does.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (!_abandoned)
            {
                Finish(_microphone);
                Finish(_system);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _abandoned = true;
            DiagnosticLog.Write($"Could not finish the recording's audio sources: {error.Message}");
        }
        finally
        {
            _microphone.Dispose();
            _system.Dispose();

            if (_abandoned)
            {
                new RecordedAudioTracks(_microphonePath, _systemPath).Discard();
            }
        }
    }

    private static FileStream Create(string path)
    {
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);

        // A placeholder, rewritten by Finish. Written now rather than at the end so the
        // samples can go straight down as they arrive: seeking back over a finished file to
        // insert forty-four bytes would mean copying the whole of it.
        stream.Write(WavAudio.Header(AudioPlan.SampleRate, AudioPlan.Channels, AudioPlan.BitsPerSample, 0));
        return stream;
    }

    private static void Finish(FileStream stream)
    {
        var data = stream.Position - WavAudio.HeaderBytes;
        stream.Flush();
        stream.Position = 0;
        stream.Write(WavAudio.Header(AudioPlan.SampleRate, AudioPlan.Channels, AudioPlan.BitsPerSample, data));
        stream.Flush();
    }

    private void Abandon(string why)
    {
        _abandoned = true;
        DiagnosticLog.Verbose($"the recording's audio sources are no longer being kept: {why}");
    }
}
