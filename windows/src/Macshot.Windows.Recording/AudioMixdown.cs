using System.Runtime.InteropServices;
using Macshot.Windows.Core.Capture;

// Imported rather than qualified for the same reason as in VideoEffectsCompositor: inside
// namespace Macshot.Windows the name "Windows" binds to Macshot.Windows.
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Macshot.Windows.Recording;

/// <summary>
/// Writes a recording again with its two sources of sound balanced as the merge panel was
/// told.
/// </summary>
/// <remarks>
/// <para>
/// macshot does this with an <c>AVMutableAudioMix</c> over a passthrough export
/// (<c>AudioMergeController.swift:138–217</c>): the two tracks are already in the file, so
/// setting a volume on each and remuxing costs nothing but a copy. Neither half of that is
/// available here. The tracks are not in the file — this port sums them as it records, for
/// the reason <see cref="AudioPlan"/> gives — so the balance is mixed again from the copies
/// the sidecar kept; and Windows has no muxer that would put a new audio track beside an
/// encoded video one, which <see cref="VideoEffectsCompositor"/> found the same way, so the
/// video is re-encoded to carry it.
/// </para>
/// <para>
/// Which leaves the recording's own track to get rid of, because what it holds is these
/// same two sources already summed at unity. Muting the clip is what does that, and it is
/// the one thing here that fails silently if it stops working: the merge would be laid on
/// top of the mix it is replacing and every source would be heard twice, at volumes nobody
/// asked for, in a file that still plays.
/// </para>
/// <para>
/// Split out of <c>Services.AudioMerger</c>, which keeps the part that decides where the
/// merged file ends up. This half is the media work, and it is here because a test host can
/// load this library and cannot load a WinUI one.
/// </para>
/// </remarks>
public static class AudioMixdown
{
    /// <summary>
    /// How much of each source is read at a time. Around two seconds of sound, so the
    /// merge is a handful of memcpys per second of recording rather than a call per frame.
    /// </summary>
    private const int BlockBytes = 1 << 20;

    /// <summary>
    /// Writes <paramref name="source"/> to <paramref name="destination"/> with the two
    /// sidecar tracks mixed at the volumes <paramref name="answer"/> asks for.
    /// </summary>
    /// <param name="microphonePath">
    /// The sidecar copies, as paths rather than as the record that names them: that record
    /// belongs to the recorder, and this library has no business knowing how a recording
    /// was made.
    /// </param>
    public static async Task WriteAsync(
        StorageFile source,
        StorageFile destination,
        string microphonePath,
        string systemPath,
        AudioMergeAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        var blendedPath = Path.Combine(Path.GetTempPath(), $"macshot-{Guid.NewGuid():N}-merged.wav");

        try
        {
            // Off the UI thread: this is a straight pass over every sample of the
            // recording, and on the dispatcher it would freeze macshot for as long as it
            // ran. VideoEffectsCompositor moves its own audio pass off for the same reason.
            await Task.Run(() => Blend(microphonePath, systemPath, blendedPath, answer));

            var composition = new MediaComposition();
            var clip = await MediaClip.CreateFromFileAsync(source);

            // Muted, for the reason the remarks give: what it holds is these same two
            // sources summed at unity, and left audible every one would be heard twice.
            clip.Volume = 0;
            composition.Clips.Add(clip);
            composition.BackgroundAudioTracks.Add(
                await BackgroundAudioTrack.CreateFromFileAsync(
                    await StorageFile.GetFileFromPathAsync(blendedPath)));

            var reason = await composition.RenderToFileAsync(
                destination,
                MediaTrimmingPreference.Precise,
                await ProfileAsync(source));

            if (reason != TranscodeFailureReason.None)
            {
                throw new InvalidOperationException(
                    $"Windows could not merge the recording's audio ({reason}).");
            }
        }
        finally
        {
            // Best effort, as the compositor's scratch files are: a file left in the
            // temporary directory is untidy, and a merge that failed because it could not
            // tidy up would be worse.
            try
            {
                File.Delete(blendedPath);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Mixes the two sources into one WAV at the volumes asked for.
    /// </summary>
    /// <remarks>
    /// Streamed rather than read whole, unlike the retimed track in
    /// <see cref="VideoEffectsCompositor"/>: this reads both files front to back exactly
    /// once, so there is nothing to be gained by holding half an hour of uncompressed audio
    /// in memory — twice.
    /// </remarks>
    private static void Blend(
        string microphonePath, string systemPath, string path, AudioMergeAnswer answer)
    {
        using var microphone = OpenSamples(microphonePath);
        using var system = OpenSamples(systemPath);

        var frame = AudioPlan.Channels * (AudioPlan.BitsPerSample / 8);

        // The longer of the two, in whole frames. They are written by one loop a sample at
        // a time and can only differ by the last of them, and the shorter one reads as
        // silence past its end.
        var data = Math.Max(microphone.Length, system.Length) - WavAudio.HeaderBytes;
        data = Math.Max(0, data - (data % frame));

        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BlockBytes);
        output.Write(WavAudio.Header(AudioPlan.SampleRate, AudioPlan.Channels, AudioPlan.BitsPerSample, data));

        var fromMicrophone = new byte[BlockBytes];
        var fromSystem = new byte[BlockBytes];
        var into = new byte[BlockBytes];

        for (long at = 0; at < data; at += BlockBytes)
        {
            var wanted = (int)Math.Min(BlockBytes, data - at);
            Fill(microphone, fromMicrophone, wanted);
            Fill(system, fromSystem, wanted);

            AudioMerge.Blend(
                fromMicrophone.AsSpan(0, wanted),
                fromSystem.AsSpan(0, wanted),
                into.AsSpan(0, wanted),
                answer.MicrophoneVolume,
                answer.SystemVolume);

            output.Write(into, 0, wanted);
        }
    }

    /// <remarks>
    /// Positioned past the header rather than parsed, because <c>AudioSidecar</c> is the
    /// only thing that writes these files and writes exactly one layout: 48 kHz stereo at
    /// sixteen bits, behind a canonical forty-four byte header.
    /// </remarks>
    private static FileStream OpenSamples(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BlockBytes)
        {
            Position = WavAudio.HeaderBytes,
        };

        return stream;
    }

    /// <summary>
    /// Reads <paramref name="wanted"/> bytes, and leaves silence wherever the file ended
    /// first — which is what makes a source that came up a frame short quiet rather than
    /// the end of the merge.
    /// </summary>
    private static void Fill(FileStream stream, byte[] block, int wanted)
    {
        var read = stream.ReadAtLeast(block.AsSpan(0, wanted), wanted, throwOnEndOfStream: false);
        block.AsSpan(read, wanted - read).Clear();
    }

    /// <remarks>
    /// Built from a stock profile and overridden, as every other encode in this port is: a
    /// profile carries container, codec and level as well, and assembling one field by
    /// field means owning every default it has. The video half is set to what the recording
    /// already is, so the pass this merge costs changes the sound and nothing else.
    /// </remarks>
    private static async Task<MediaEncodingProfile> ProfileAsync(StorageFile source)
    {
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);

        // Said outright: the audio half of an Auto profile is left unresolved, and handed a
        // track to encode it throws MF_E_ATTRIBUTENOTFOUND. VideoEditorWindow found this
        // the hard way, on every export of a recording with sound.
        profile.Audio = AudioEncodingProperties.CreateAac(
            (uint)AudioPlan.SampleRate,
            (uint)AudioPlan.Channels,
            AudioPlan.Bitrate);

        var video = await source.Properties.GetVideoPropertiesAsync();

        if (video.Width > 0 && video.Height > 0)
        {
            // The same arithmetic the recording was encoded with, from the size it turned
            // out to be: a merge must not be the moment a recording changes bitrate.
            var plan = RecordingPlan.Resolve((int)video.Width, (int)video.Height, await FrameRateAsync(source));

            profile.Video.Width = (uint)plan.Width;
            profile.Video.Height = (uint)plan.Height;
            profile.Video.Bitrate = plan.Bitrate;
            profile.Video.FrameRate.Numerator = (uint)plan.FrameRate;
            profile.Video.FrameRate.Denominator = 1;
        }

        return profile;
    }

    /// <remarks>
    /// Read from the file rather than from the recording preference, for the reason
    /// <c>VideoEditorWindow.ProbeAsync</c> gives: the preference may have been changed since,
    /// and a 60 fps recording re-encoded as though it were 30 comes out at half the bitrate
    /// it needs.
    /// </remarks>
    private static async Task<int> FrameRateAsync(StorageFile source)
    {
        try
        {
            var rate = (await MediaEncodingProfile.CreateFromFileAsync(source)).Video?.FrameRate;

            return rate is { Denominator: > 0 }
                ? (int)Math.Round(rate.Numerator / (double)rate.Denominator)
                : RecordingPlan.DefaultFrameRate;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or COMException)
        {
            return RecordingPlan.DefaultFrameRate;
        }
    }
}
