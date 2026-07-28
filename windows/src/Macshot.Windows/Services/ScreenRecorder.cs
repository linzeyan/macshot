using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Channels;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Core.Output;

// Imported rather than qualified for the same reason as in GraphicsCaptureService:
// inside namespace Macshot.Windows the name "Windows" binds to Macshot.Windows.
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Macshot.Windows.Services;

/// <summary>One finished recording, and what it took to make it.</summary>
public sealed record RecordingResult(string Path, TimeSpan Duration, int Frames, int DroppedFrames);

/// <summary>
/// Records a display, as MP4 or as an animated GIF.
/// </summary>
/// <remarks>
/// <para>
/// This is what the move to <c>Windows.Graphics.Capture</c> was for. Recording is the
/// same capture item a screenshot opens, left running: the compositor hands over a
/// frame whenever the content changes.
/// </para>
/// <para>
/// The two formats want opposite things from those frames. MP4 wants them left where
/// they are — the surfaces go straight to the platform's H.264 encoder through a
/// <see cref="MediaStreamSource"/> and never come back to the CPU. GIF has no encoder
/// to hand them to, so every frame is copied down, shrunk, and written as its own
/// image. That difference is the whole shape of this file.
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

    private const double Dpi = 96;

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
        RecordingFormat format,
        CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var item = GraphicsCaptureService.OpenDisplay(monitorHandle);
        return format == RecordingFormat.Gif
            ? RecordGifAsync(item, path, cancellation)
            : RecordMp4Async(item, path, cancellation);
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

    private async Task<RecordingResult> RecordMp4Async(
        GraphicsCaptureItem item,
        string path,
        CancellationToken cancellation)
    {
        var size = item.Size;
        var plan = RecordingPlan.Resolve(size.Width, size.Height);
        var kept = 0;

        using var frames = new FrameStream(Device(), item, plan.FrameInterval, cancellation);

        var source = BuildSource(size);
        source.Starting += (_, args) =>
        {
            // Capture starts here rather than earlier, because here is when the
            // pipeline first wants a frame. Starting sooner would only fill the queue
            // with desktop from before the recording began.
            frames.Start();
            args.Request.SetActualStartPosition(TimeSpan.Zero);
        };

        source.SampleRequested += async (_, args) =>
        {
            var request = args.Request;
            var deferral = request.GetDeferral();
            try
            {
                if (await frames.NextAsync() is not { } timed)
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

        using var output = await OpenForWritingAsync(path);
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, output, BuildProfile(plan));

        if (!prepared.CanTranscode)
        {
            throw new InvalidOperationException(
                $"Windows cannot record to MP4 on this machine: {prepared.FailureReason}.");
        }

        await prepared.TranscodeAsync();
        return new RecordingResult(path, frames.Elapsed, kept, frames.Dropped);
    }

    /// <summary>
    /// Records to an animated GIF: every frame copied down from the compositor,
    /// shrunk, and written as an image of its own.
    /// </summary>
    /// <remarks>
    /// A frame's delay is the time until the frame after it, so each one is held back
    /// until its successor arrives and can say how long it was on screen. Measuring
    /// forward from the nominal rate instead would make every hitch in the recording
    /// disappear from the file.
    /// </remarks>
    private async Task<RecordingResult> RecordGifAsync(
        GraphicsCaptureItem item,
        string path,
        CancellationToken cancellation)
    {
        var size = item.Size;
        var plan = GifRecordingPlan.Resolve(size.Width, size.Height);
        var timing = new GifFrameTiming();

        using var frames = new FrameStream(Device(), item, plan.FrameInterval, cancellation);
        using var output = await OpenForWritingAsync(path);

        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.GifEncoderId, output);
        await SetLoopingAsync(encoder);

        frames.Start();

        GifFrame? previous = null;
        var written = 0;

        while (written < GifRecordingPlan.MaximumFrames && await frames.NextAsync() is { } timed)
        {
            var current = await ToGifFrameAsync(timed, plan);
            if (previous is not null)
            {
                await WriteGifFrameAsync(encoder, previous, timing.Next(current.Timestamp - previous.Timestamp), written, plan);
                written++;
            }

            previous = current;
        }

        if (previous is null)
        {
            throw new InvalidOperationException("The recording stopped before Windows delivered a frame.");
        }

        if (written < GifRecordingPlan.MaximumFrames)
        {
            // The last frame has nothing after it to be measured against, so it is
            // shown for one frame of the rate the recording was taken at.
            await WriteGifFrameAsync(encoder, previous, timing.Next(plan.FrameInterval), written, plan);
            written++;
        }

        await encoder.FlushAsync();
        return new RecordingResult(path, frames.Elapsed, written, frames.Dropped);
    }

    private IDirect3DDevice Device() => _device ??= GraphicsCaptureService.CreateDirect3DDevice();

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
    /// Copies one captured frame down to the CPU and shrinks it to the size the GIF
    /// is being written at.
    /// </summary>
    private static async Task<GifFrame> ToGifFrameAsync(TimedFrame timed, GifRecordingPlan plan)
    {
        using (timed.Frame)
        {
            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(timed.Frame.Surface);

            var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
            bitmap.CopyToBuffer(pixels.AsBuffer());

            return new GifFrame(
                FrameScaler.Downscale(pixels, bitmap.PixelWidth, bitmap.PixelHeight, plan.Width, plan.Height),
                timed.Timestamp);
        }
    }

    private static async Task WriteGifFrameAsync(
        BitmapEncoder encoder,
        GifFrame frame,
        int delay,
        int index,
        GifRecordingPlan plan)
    {
        if (index > 0)
        {
            // The encoder starts on a frame of its own, so this moves on only once
            // there is something already written to move on from.
            await encoder.GoToNextFrameAsync();
        }

        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,

            // The captured alpha byte is undefined, as it is everywhere else macshot
            // encodes a capture, and honouring it would punch holes in the GIF.
            BitmapAlphaMode.Ignore,
            (uint)plan.Width,
            (uint)plan.Height,
            Dpi,
            Dpi,
            frame.Pixels);

        await encoder.BitmapProperties.SetPropertiesAsync(new BitmapPropertySet
        {
            { "/grctlext/Delay", new BitmapTypedValue((ushort)delay, PropertyType.UInt16) },
        });
    }

    /// <summary>
    /// Marks the GIF as looping forever.
    /// </summary>
    /// <remarks>
    /// A GIF plays once unless a Netscape application extension says otherwise. The
    /// block is the same one every encoder writes: the application name, then a
    /// sub-block of a loop count of zero, which means without end.
    /// </remarks>
    private static async Task SetLoopingAsync(BitmapEncoder encoder)
    {
        await encoder.BitmapContainerProperties.SetPropertiesAsync(new BitmapPropertySet
        {
            {
                "/appext/application",
                new BitmapTypedValue(Encoding.ASCII.GetBytes("NETSCAPE2.0"), PropertyType.UInt8Array)
            },
            {
                "/appext/data",
                new BitmapTypedValue(new byte[] { 3, 1, 0, 0 }, PropertyType.UInt8Array)
            },
        });
    }

    private static async Task<IRandomAccessStream> OpenForWritingAsync(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("A recording needs a full path to write to.", nameof(path));
        }

        Directory.CreateDirectory(directory);
        var folder = await StorageFolder.GetFolderFromPathAsync(directory);
        var file = await folder.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.ReplaceExisting);
        return await file.OpenAsync(FileAccessMode.ReadWrite);
    }

    private sealed record TimedFrame(Direct3D11CaptureFrame Frame, TimeSpan Timestamp);

    private sealed record GifFrame(byte[] Pixels, TimeSpan Timestamp);

    /// <summary>
    /// One display's frames, at the rate asked for, in the order they arrived.
    /// </summary>
    /// <remarks>
    /// Shared by both formats because everything up to the encoder is the same
    /// problem: keep the compositor's frames in order, keep only the ones the rate
    /// calls for, and never leak the texture of one that was turned away.
    /// </remarks>
    private sealed class FrameStream : IDisposable
    {
        private readonly Direct3D11CaptureFramePool _pool;
        private readonly GraphicsCaptureSession _session;
        private readonly Channel<TimedFrame> _frames;
        private readonly FrameCadence _cadence;
        private readonly Stopwatch _clock = new();
        private readonly CancellationTokenRegistration _stopping;

        public FrameStream(
            IDirect3DDevice device,
            GraphicsCaptureItem item,
            TimeSpan interval,
            CancellationToken cancellation)
        {
            _cadence = new FrameCadence(interval);
            _frames = Channel.CreateBounded<TimedFrame>(new BoundedChannelOptions(QueueDepth)
            {
                // Frames are turned away by hand rather than dropped by the channel,
                // so the one discarded can also be disposed. A capture frame holds a
                // texture; leaking one leaks video memory for the whole recording.
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });

            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                BufferCount,
                item.Size);
            _pool.FrameArrived += OnFrameArrived;

            _session = _pool.CreateCaptureSession(item);

            // Unlike a screenshot, which is almost never wanted with a pointer in it:
            // a recording of someone demonstrating something without the pointer is
            // missing the thing being demonstrated.
            _session.IsCursorCaptureEnabled = true;

            // A window that closes, or a display that is unplugged, ends the recording
            // with what it has rather than hanging on an item that will never deliver
            // another frame.
            item.Closed += (_, _) => _frames.Writer.TryComplete();
            _stopping = cancellation.Register(() => _frames.Writer.TryComplete());
        }

        /// <summary>How long the recording has been running.</summary>
        public TimeSpan Elapsed => _clock.Elapsed;

        /// <summary>Frames the rate did not call for.</summary>
        public int Dropped => _cadence.Dropped;

        public void Start()
        {
            _clock.Restart();
            _session.StartCapture();
        }

        /// <summary>
        /// The next frame, or null once the recording has stopped and the queue has
        /// been emptied. The caller owns the frame it is given.
        /// </summary>
        public async Task<TimedFrame?> NextAsync()
        {
            while (await _frames.Reader.WaitToReadAsync())
            {
                if (_frames.Reader.TryRead(out var frame))
                {
                    return frame;
                }
            }

            return null;
        }

        public void Dispose()
        {
            _frames.Writer.TryComplete();
            _clock.Stop();
            _stopping.Dispose();
            _pool.FrameArrived -= OnFrameArrived;
            _session.Dispose();
            _pool.Dispose();

            // Whatever the encoder never asked for. Each queued frame holds a texture,
            // and a recording that ended early would otherwise leave a few behind.
            while (_frames.Reader.TryRead(out var frame))
            {
                frame.Frame.Dispose();
            }
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            if (sender.TryGetNextFrame() is not { } frame)
            {
                return;
            }

            var elapsed = _clock.Elapsed;
            if (!_cadence.ShouldKeep(elapsed) || !_frames.Writer.TryWrite(new TimedFrame(frame, elapsed)))
            {
                frame.Dispose();
            }
        }
    }
}
