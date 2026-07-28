using System.Diagnostics;
using System.Threading.Channels;
using Macshot.Windows.Core.Capture;

// Imported rather than qualified for the same reason as in GraphicsCaptureService:
// inside namespace Macshot.Windows the name "Windows" binds to Macshot.Windows.
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Macshot.Windows.Services;

/// <summary>One finished recording, and what it took to make it.</summary>
public sealed record RecordingResult(string Path, TimeSpan Duration, int Frames, int DroppedFrames);

/// <summary>
/// Records a display to an MP4 file.
/// </summary>
/// <remarks>
/// <para>
/// This is what the move to <c>Windows.Graphics.Capture</c> was for. Recording is the
/// same capture item a screenshot opens, left running: the compositor hands over a
/// frame whenever the content changes, and those frames reach the platform's own
/// H.264 encoder without ever coming back to the CPU as pixels.
/// </para>
/// <para>
/// The encoder is reached through <see cref="MediaTranscoder"/> rather than Media
/// Foundation directly. A sink writer would be several hundred lines of COM interop
/// for the same file; a <see cref="MediaStreamSource"/> handing over surfaces is the
/// supported way to say "here are frames, write me an MP4".
/// </para>
/// <para>
/// Everything here is compile-checked only. Nothing in continuous integration has a
/// compositor, an encoder, or a display, so the first real answer about whether a
/// recording plays comes from hardware.
/// </para>
/// </remarks>
public sealed class ScreenRecorder : IDisposable
{
    /// <summary>
    /// Frames the pool holds. More than one so the compositor can produce the next
    /// frame while the encoder still holds the last, and few enough that a stalled
    /// encoder falls behind by a fraction of a second rather than by a backlog it
    /// then plays back as slow motion.
    /// </summary>
    private const int BufferCount = 3;

    /// <summary>
    /// Frames waiting to be encoded. Deliberately shallow, for the same reason: what
    /// a recording must not do is drift behind what is on screen.
    /// </summary>
    private const int QueueDepth = 3;

    private IDirect3DDevice? _device;
    private bool _disposed;

    /// <summary>Whether this build of Windows can record at all.</summary>
    public static bool IsSupported => GraphicsCaptureService.IsSupported;

    /// <summary>
    /// Records one display until <paramref name="cancellation"/> asks it to stop, and
    /// writes it to <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    /// Stopping is how a recording ends, so cancellation returns a finished file
    /// rather than throwing. The only failures raised are the ones that mean there is
    /// no file: no encoder, no capture item, nowhere to write.
    /// </remarks>
    public Task<RecordingResult> RecordDisplayAsync(
        nint monitorHandle,
        string path,
        CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RecordAsync(GraphicsCaptureService.OpenDisplay(monitorHandle), path, cancellation);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _device?.Dispose();
        _device = null;
    }

    private async Task<RecordingResult> RecordAsync(
        GraphicsCaptureItem item,
        string path,
        CancellationToken cancellation)
    {
        var device = _device ??= GraphicsCaptureService.CreateDirect3DDevice();
        var size = item.Size;
        var plan = RecordingPlan.Resolve(size.Width, size.Height);
        var cadence = new FrameCadence(plan.FrameInterval);
        var clock = new Stopwatch();
        var kept = 0;

        var frames = Channel.CreateBounded<TimedFrame>(new BoundedChannelOptions(QueueDepth)
        {
            // Frames are turned away by hand rather than dropped by the channel, so
            // the one discarded can also be disposed. A capture frame holds a texture;
            // leaking one leaks video memory for the length of the recording.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });

        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            BufferCount,
            size);

        pool.FrameArrived += (sender, _) =>
        {
            if (sender.TryGetNextFrame() is not { } frame)
            {
                return;
            }

            var elapsed = clock.Elapsed;
            if (!cadence.ShouldKeep(elapsed) || !frames.Writer.TryWrite(new TimedFrame(frame, elapsed)))
            {
                frame.Dispose();
            }
        };

        using var session = pool.CreateCaptureSession(item);

        // Unlike a screenshot, which is almost never wanted with a pointer in it: a
        // recording of someone demonstrating something without the pointer is missing
        // the thing being demonstrated.
        session.IsCursorCaptureEnabled = true;

        // A window that closes, or a display that is unplugged, ends the recording
        // with what it has rather than hanging on an item that will never deliver
        // another frame.
        item.Closed += (_, _) => frames.Writer.TryComplete();
        using var stopping = cancellation.Register(() => frames.Writer.TryComplete());

        var source = BuildSource(size);
        source.Starting += (_, args) =>
        {
            // Capture starts here rather than earlier, because here is when the
            // pipeline first wants a frame. Starting sooner would only fill the queue
            // with a moment of desktop from before the recording began.
            clock.Restart();
            session.StartCapture();
            args.Request.SetActualStartPosition(TimeSpan.Zero);
        };

        source.SampleRequested += async (_, args) =>
        {
            var request = args.Request;
            var deferral = request.GetDeferral();
            try
            {
                if (await ReadNextAsync(frames.Reader) is not { } timed)
                {
                    // No sample is how a MediaStreamSource says the stream is over,
                    // which is what finishes the file.
                    request.Sample = null;
                    return;
                }

                kept++;
                var sample = MediaStreamSample.CreateFromDirect3D11Surface(timed.Frame.Surface, timed.Timestamp);

                // The frame owns the texture the sample points at, so it stays alive
                // until the encoder says it is done with it.
                sample.Processed += (_, _) => timed.Frame.Dispose();
                request.Sample = sample;
            }
            catch (Exception)
            {
                // Ending the stream is the only useful answer here: throwing out of a
                // deferred sample request tears the process down instead, and the
                // partial recording is still worth writing.
                request.Sample = null;
            }
            finally
            {
                deferral.Complete();
            }
        };

        try
        {
            await TranscodeAsync(source, path, plan);
        }
        finally
        {
            frames.Writer.TryComplete();
            clock.Stop();
            Drain(frames.Reader);
        }

        return new RecordingResult(path, clock.Elapsed, kept, cadence.Dropped);
    }

    /// <summary>
    /// Describes the frames as they are captured: uncompressed BGRA, the size the item
    /// actually is. Scaling to the encoded size is the transcoder's job, and claiming
    /// a size the surfaces do not have would corrupt every frame.
    /// </summary>
    private static MediaStreamSource BuildSource(SizeInt32 size)
    {
        var properties = VideoEncodingProperties.CreateUncompressed(
            MediaEncodingSubtypes.Bgra8,
            (uint)size.Width,
            (uint)size.Height);

        return new MediaStreamSource(new VideoStreamDescriptor(properties))
        {
            // A live source: buffering ahead would mean the recording lagged what is
            // on screen, and there is nothing to buffer ahead of anyway.
            BufferTime = TimeSpan.Zero,
        };
    }

    private static async Task TranscodeAsync(MediaStreamSource source, string path, RecordingPlan plan)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("A recording needs a full path to write to.", nameof(path));
        }

        Directory.CreateDirectory(directory);
        var folder = await StorageFolder.GetFolderFromPathAsync(directory);
        var file = await folder.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.ReplaceExisting);

        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, stream, BuildProfile(plan));

        if (!prepared.CanTranscode)
        {
            throw new InvalidOperationException(
                $"Windows cannot record to MP4 on this machine: {prepared.FailureReason}.");
        }

        await prepared.TranscodeAsync();
    }

    /// <remarks>
    /// Built from a stock profile and then overridden, because a profile carries far
    /// more than the four values below — container, codec, profile level — and
    /// assembling one field by field means owning every default it has.
    /// </remarks>
    private static MediaEncodingProfile BuildProfile(RecordingPlan plan)
    {
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);

        // No microphone and no system audio yet: an audio stream nothing ever writes
        // to would leave the file waiting for samples that never come. Null-forgiving
        // because the projection does not admit that dropping the stream is allowed,
        // which it is and which is the documented way to do it.
        profile.Audio = null!;

        profile.Video.Width = (uint)plan.Width;
        profile.Video.Height = (uint)plan.Height;
        profile.Video.Bitrate = plan.Bitrate;
        profile.Video.FrameRate.Numerator = (uint)plan.FrameRate;
        profile.Video.FrameRate.Denominator = 1;
        return profile;
    }

    /// <summary>
    /// The next frame to encode, or null once the recording has stopped and the queue
    /// has been emptied.
    /// </summary>
    private static async Task<TimedFrame?> ReadNextAsync(ChannelReader<TimedFrame> reader)
    {
        while (await reader.WaitToReadAsync())
        {
            if (reader.TryRead(out var frame))
            {
                return frame;
            }
        }

        return null;
    }

    /// <summary>
    /// Disposes whatever the encoder never asked for. Each queued frame holds a
    /// texture, and a recording that ended early would otherwise leave a few behind.
    /// </summary>
    private static void Drain(ChannelReader<TimedFrame> reader)
    {
        while (reader.TryRead(out var frame))
        {
            frame.Frame.Dispose();
        }
    }

    private sealed record TimedFrame(Direct3D11CaptureFrame Frame, TimeSpan Timestamp);
}
