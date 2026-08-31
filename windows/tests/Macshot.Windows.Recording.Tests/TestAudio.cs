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
    public static Task<StorageFile> WriteToneAsync(StorageFolder folder, int seconds) =>
        WriteToneAsync(folder, seconds, Frequency, frame => Ladder[Math.Min(Ladder.Length - 1, frame / AudioPlan.SampleRate)]);

    /// <summary>
    /// A tone that holds one pitch and one loudness for the whole of it.
    /// </summary>
    /// <remarks>
    /// What the merge tests want, where the question is not <em>when</em> a sound was made
    /// but <em>which source</em> made it: two constant tones an octave and a half apart can
    /// be told from each other in the same file, which two amplitudes of the same tone
    /// cannot.
    /// </remarks>
    public static Task<StorageFile> WriteToneAsync(
        StorageFolder folder, int seconds, double frequency, double amplitude) =>
        WriteToneAsync(folder, seconds, frequency, _ => amplitude);

    private static async Task<StorageFile> WriteToneAsync(
        StorageFolder folder, int seconds, double frequency, Func<int, double> amplitudeAt)
    {
        var frames = AudioPlan.SampleRate * seconds;
        var samples = new byte[frames * AudioPlan.Channels * (AudioPlan.BitsPerSample / 8)];

        for (var frame = 0; frame < frames; frame++)
        {
            var value = (short)(amplitudeAt(frame)
                * short.MaxValue
                * Math.Sin(2 * Math.PI * frequency * frame / AudioPlan.SampleRate));

            for (var channel = 0; channel < AudioPlan.Channels; channel++)
            {
                var i = ((frame * AudioPlan.Channels) + channel) * 2;
                samples[i] = (byte)(value & 0xFF);
                samples[i + 1] = (byte)((value >> 8) & 0xFF);
            }
        }

        return await WriteWavAsync(folder, samples);
    }

    /// <summary>
    /// The two sources summed at unity, which is the track a macshot recording carries.
    /// </summary>
    /// <remarks>
    /// Through the product's own mixer rather than a second addition written here: what the
    /// merge has to replace is exactly what <see cref="AudioMerge.Blend"/> at unity
    /// produces, and a test that summed them its own way would be asserting against its own
    /// arithmetic.
    /// </remarks>
    public static async Task<StorageFile> SummedAsync(
        StorageFolder folder, StorageFile microphone, StorageFile system)
    {
        var first = await ReadAsync(microphone);
        var second = await ReadAsync(system);
        var samples = new byte[Math.Max(first.Length, second.Length) - WavAudio.HeaderBytes];

        AudioMerge.Blend(
            first.AsSpan(WavAudio.HeaderBytes),
            second.AsSpan(WavAudio.HeaderBytes),
            samples,
            AudioMerge.DefaultVolume,
            AudioMerge.DefaultVolume);

        return await WriteWavAsync(folder, samples);
    }

    private static async Task<StorageFile> WriteWavAsync(StorageFolder folder, byte[] samples)
    {
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

    /// <summary>
    /// How loud <paramref name="hz"/> is over one window, as the amplitude of a sine at
    /// that pitch — so 0 means the source that made it is not in this file at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Goertzel rather than a full transform, which is one accumulator and no library: the
    /// question is only ever about the two pitches the sources were given, and answering it
    /// for two known bins is a dozen lines against a dependency.
    /// </para>
    /// <para>
    /// Both pitches divide the sample rate a whole number of times over a whole second, so
    /// a window on a second boundary lands exactly on their bins and neither leaks into the
    /// other.
    /// </para>
    /// </remarks>
    public static double Level(byte[] wav, double hz, double from, double to)
    {
        var layout = WavAudio.Read(wav)
            ?? throw new InvalidOperationException("the rendered audio is not a WAV this can read");

        var first = (long)(from * layout.SampleRate);
        var last = Math.Min(layout.Frames, (long)(to * layout.SampleRate));
        var coefficient = 2 * Math.Cos(2 * Math.PI * hz / layout.SampleRate);

        double previous = 0;
        double before = 0;
        long count = 0;

        for (var frame = first; frame < last; frame++)
        {
            var i = layout.DataOffset + (frame * layout.BytesPerFrame);
            var value = (short)(wav[i] | (wav[i + 1] << 8)) / (double)short.MaxValue;
            var current = value + (coefficient * previous) - before;

            before = previous;
            previous = current;
            count++;
        }

        if (count == 0)
        {
            return 0;
        }

        var power = (previous * previous) + (before * before) - (coefficient * previous * before);

        return 2 * Math.Sqrt(Math.Max(0, power)) / count;
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

        return await ReadAsync(rendered);
    }

    /// <remarks>
    /// Through the WinRT buffer rather than <c>File.ReadAllBytes</c>, because a file the
    /// transcoder has just finished with can still be held open for a moment and this is the
    /// API that waits for it.
    /// </remarks>
    private static async Task<byte[]> ReadAsync(StorageFile file)
    {
        var buffer = await FileIO.ReadBufferAsync(file);
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
