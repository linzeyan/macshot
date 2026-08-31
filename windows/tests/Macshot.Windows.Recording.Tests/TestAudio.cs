using Macshot.Windows.Core.Capture;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Macshot.Windows.Recording.Tests;

/// <summary>
/// A soundtrack whose loudness says which source second it came from, and a way to read
/// that back out of an export.
/// </summary>
/// <remarks>
/// <para>
/// The ear's version of <see cref="TestVideo.Palette"/>, and for the same reason: an
/// export moves sound and picture with two different mechanisms — the frames are decoded
/// and rewritten, the track is either trimmed into runs or resampled to PCM — so "the two
/// still line up" is a claim that has to be measured rather than assumed. A tone whose
/// amplitude steps once a second turns it into arithmetic: the loudness at output time t
/// names the source second it belongs to, and it can be compared against the colour on
/// screen at the same moment.
/// </para>
/// <para>
/// Amplitude rather than pitch, because RMS survives AAC at any bitrate worth using and
/// needs no transform to read.
/// </para>
/// </remarks>
internal static class TestAudio
{
    /// <summary>440 Hz, which is well inside every codec's comfortable range.</summary>
    private const double Frequency = 440;

    /// <summary>One amplitude per source second, far enough apart to name unambiguously.</summary>
    public static readonly double[] Ladder = [0.10, 0.25, 0.40, 0.55, 0.70, 0.85];

    /// <summary>The tone as a WAV, at the product's own sample rate and channel count.</summary>
    public static async Task<StorageFile> WriteToneAsync(StorageFolder folder, int seconds)
    {
        var frames = AudioPlan.SampleRate * seconds;
        var samples = new byte[frames * AudioPlan.Channels * (AudioPlan.BitsPerSample / 8)];

        for (var frame = 0; frame < frames; frame++)
        {
            var second = Math.Min(Ladder.Length - 1, frame / AudioPlan.SampleRate);
            var value = (short)(Ladder[second]
                * short.MaxValue
                * Math.Sin(2 * Math.PI * Frequency * frame / AudioPlan.SampleRate));

            for (var channel = 0; channel < AudioPlan.Channels; channel++)
            {
                var i = ((frame * AudioPlan.Channels) + channel) * 2;
                samples[i] = (byte)(value & 0xFF);
                samples[i + 1] = (byte)((value >> 8) & 0xFF);
            }
        }

        var file = await folder.CreateFileAsync(
            "macshot-tone.wav", CreationCollisionOption.GenerateUniqueName);

        var header = WavAudio.Header(
            AudioPlan.SampleRate, AudioPlan.Channels, AudioPlan.BitsPerSample, samples.Length);

        await FileIO.WriteBytesAsync(file, [.. header, .. samples]);

        return file;
    }

    /// <summary>
    /// The loudness of <paramref name="video"/> over one window, as an index into
    /// <see cref="Ladder"/>.
    /// </summary>
    /// <remarks>
    /// Rendered to WAV rather than decoded frame by frame: the transcoder is already here,
    /// and a canonical 16-bit file is something <see cref="WavAudio"/> can read and this
    /// can average without any format handling of its own.
    /// </remarks>
    public static int SecondHeardAt(byte[] wav, double from, double to)
    {
        var layout = WavAudio.Read(wav)
            ?? throw new InvalidOperationException("the rendered audio is not a WAV this can read");

        var first = (long)(from * layout.SampleRate);
        var last = Math.Min(layout.Frames, (long)(to * layout.SampleRate));

        double total = 0;
        long count = 0;
        for (var frame = first; frame < last; frame++)
        {
            var i = layout.DataOffset + (frame * layout.BytesPerFrame);
            var value = (short)(wav[i] | (wav[i + 1] << 8)) / (double)short.MaxValue;
            total += value * value;
            count++;
        }

        var rms = count == 0 ? 0 : Math.Sqrt(total / count);

        return Nearest(rms);
    }

    /// <summary>The sound of <paramref name="video"/> as a WAV, ready to measure.</summary>
    public static async Task<byte[]> SoundAsync(StorageFolder folder, StorageFile video)
    {
        var composition = new MediaComposition();
        composition.Clips.Add(await MediaClip.CreateFromFileAsync(video));

        var rendered = await folder.CreateFileAsync(
            "macshot-export.wav", CreationCollisionOption.GenerateUniqueName);

        var result = await composition.RenderToFileAsync(
            rendered,
            MediaTrimmingPreference.Precise,
            MediaEncodingProfile.CreateWav(AudioEncodingQuality.High));

        Assert.AreEqual(
            TranscodeFailureReason.None, result, "the export's sound could not be rendered");

        var buffer = await FileIO.ReadBufferAsync(rendered);
        var bytes = new byte[buffer.Length];
        using (var reader = DataReader.FromBuffer(buffer))
        {
            reader.ReadBytes(bytes);
        }

        return bytes;
    }

    /// <summary>
    /// A tone at <see cref="Ladder"/>'s amplitude has an RMS of that over root two, which
    /// is what this inverts. Nearest rather than exact: AAC moves it by a percent or so.
    /// </summary>
    private static int Nearest(double rms)
    {
        var best = 0;
        var closest = double.MaxValue;

        for (var index = 0; index < Ladder.Length; index++)
        {
            var distance = Math.Abs((Ladder[index] / Math.Sqrt(2)) - rms);
            if (distance < closest)
            {
                closest = distance;
                best = index;
            }
        }

        return best;
    }
}
