using System.Buffers;
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
/// <param name="AudioTracks">
/// Each source of the recording's sound, kept apart, or null when there was only one of
/// them to keep. This is what the merge panel weighs one against the other; the recording
/// itself carries them summed.
/// </param>
public sealed record RecordingResult(
    string Path,
    TimeSpan Duration,
    int Frames,
    int DroppedFrames,
    RecordedAudioTracks? AudioTracks = null);

/// <summary>
/// Which sounds a recording carries, if any.
/// </summary>
/// <remarks>
/// Both mix into one track rather than becoming two, for the reason
/// <see cref="AudioPlan"/> gives at length.
/// </remarks>
/// <param name="MicrophoneDeviceId">
/// Which microphone, or null for the one Windows would open. Carried here rather than
/// read from the settings where the endpoint is opened, so that what a recording listens
/// to is settled at the moment it is asked for — the same moment the switches are.
/// </param>
public readonly record struct RecordingAudio(
    bool SystemAudio,
    bool Microphone,
    string? MicrophoneDeviceId = null)
{
    /// <summary>Whether anything at all was asked for.</summary>
    public bool IsSilent => !SystemAudio && !Microphone;
}

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
    /// <remarks>
    /// Four rather than three because <see cref="Mp4Frames"/> keeps the last frame
    /// alive to hand over again while the screen is still, and a frame that is being
    /// held is one the pool cannot recycle.
    /// </remarks>
    private const int BufferCount = 4;

    /// <summary>
    /// Frames waiting to be encoded. Deliberately shallow, for the same reason: what
    /// a recording must not do is drift behind what is on screen.
    /// </summary>
    private const int QueueDepth = 3;

    private const double Dpi = 96;

    /// <summary>
    /// Guards <see cref="_running"/>, which the UI thread writes through
    /// <see cref="SetPaused"/> while the recording task replaces it.
    /// </summary>
    private readonly object _pauseGate = new();

    private IDirect3DDevice? _device;
    private bool _disposed;

    /// <summary>The stream being recorded, or null between recordings.</summary>
    private FrameStream? _running;

    /// <summary>Whether this build of Windows can record at all.</summary>
    public static bool IsSupported => GraphicsCaptureService.IsSupported;

    /// <summary>
    /// Holds or resumes the recording. A held recording keeps its file and its clock:
    /// the pause is absent from the result rather than present as a still.
    /// </summary>
    /// <remarks>
    /// Doing nothing when no recording is running is deliberate. The panel that calls
    /// this outlives the recording by a few seconds to say where the file went, and a
    /// pause pressed in that window is a no-op, not a failure.
    /// </remarks>
    public void SetPaused(bool paused)
    {
        lock (_pauseGate)
        {
            _running?.SetPaused(paused);
        }
    }

    /// <summary>
    /// Records one display until <paramref name="cancellation"/> asks it to stop, and
    /// writes it to <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    /// Stopping is how a recording ends, so cancellation returns a finished file
    /// rather than throwing. The only failures raised are the ones that mean there is
    /// no file: no encoder, no capture item, nowhere to write.
    /// </remarks>
    /// <param name="region">
    /// The part of the display to keep, in that display's own pixels, or null for all
    /// of it. There is no crop in the capture API, so a region costs the frames a trip
    /// through main memory — see <see cref="CropperOrNull"/>.
    /// </param>
    /// <param name="frameRate">
    /// Frames a second, or null for whichever plan's own default the format calls for.
    /// The two plans clamp it to what they can encode, so a number out of range slows or
    /// smooths the recording rather than failing it.
    /// </param>
    /// <param name="audio">
    /// Which sounds to record. Ignored for GIF, which has nowhere to put them — as it
    /// is on macOS.
    /// </param>
    public Task<RecordingResult> RecordDisplayAsync(
        nint monitorHandle,
        string path,
        RecordingFormat format,
        CancellationToken cancellation,
        CaptureRegion? region = null,
        int? frameRate = null,
        RecordingAudio audio = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var item = GraphicsCaptureService.OpenDisplay(monitorHandle);
        return format == RecordingFormat.Gif
            ? RecordGifAsync(item, path, region, null, frameRate ?? GifRecordingPlan.DefaultFrameRate, cancellation)
            : RecordMp4Async(item, path, region, null, frameRate ?? RecordingPlan.DefaultFrameRate, audio, cancellation);
    }

    /// <summary>
    /// Records one window until <paramref name="cancellation"/> asks it to stop, and
    /// writes it to <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window's own capture item rather than a rectangle of the display, which is what
    /// makes this follow: the compositor renders that window's tree, so the recording holds
    /// the window wherever it is dragged to and holds only the window — a dialog or another
    /// app in front of it is simply not in the file.
    /// </para>
    /// <para>
    /// That is also what the overlays cost. The click ring, the keystroke pill and the
    /// webcam bubble are macshot's own windows laid over the desktop, and none of them is
    /// in this window's tree, so a window recording cannot carry any of the three. The
    /// caller leaves them down rather than showing the user three overlays that will not be
    /// in what they are recording.
    /// </para>
    /// <para>
    /// A window that is closed mid-recording closes its capture item, which ends the stream
    /// with what it has: the file is written rather than lost. Everything else the window
    /// can do — move, resize, minimize — is <see cref="WindowRecordingArea"/>'s.
    /// </para>
    /// </remarks>
    public Task<RecordingResult> RecordWindowAsync(
        CaptureWindow window,
        string path,
        RecordingFormat format,
        CancellationToken cancellation,
        int? frameRate = null,
        RecordingAudio audio = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var item = GraphicsCaptureService.OpenWindow(window.Id);
        var size = item.Size;

        // Both rectangles or neither: with nothing to compare, the whole item is kept and
        // the recording carries the invisible resize border rather than a guess at where
        // it is. Asked here rather than taken from the enumeration behind the overlay,
        // because that list was made before the user chose anything.
        var follow = WindowEnumerator.TryGetBounds(window.Id, out var windowRect, out var visible)
            ? WindowRecordingArea.Resolve(windowRect, visible, size.Width, size.Height)
            : WindowRecordingArea.Resolve(default, default, size.Width, size.Height);

        return format == RecordingFormat.Gif
            ? RecordGifAsync(item, path, null, follow, frameRate ?? GifRecordingPlan.DefaultFrameRate, cancellation)
            : RecordMp4Async(item, path, null, follow, frameRate ?? RecordingPlan.DefaultFrameRate, audio, cancellation);
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
        CaptureRegion? region,
        WindowRecordingArea? follow,
        int frameRate,
        RecordingAudio audio,
        CancellationToken cancellation)
    {
        var size = item.Size;
        var crop = CropperOrNull(size, region);

        // What each sample holds, which is what the stream has to be told and what the
        // profile is resolved from. The three cases are the whole item, a fixed rectangle
        // of a display, and the pinned rectangle of a window that may still resize.
        var sourceWidth = follow?.Width ?? crop?.Width ?? size.Width;
        var sourceHeight = follow?.Height ?? crop?.Height ?? size.Height;

        var plan = RecordingPlan.Resolve(sourceWidth, sourceHeight, frameRate);

        using var frames = new FrameStream(Device(), item, plan.FrameInterval, cancellation);
        using var held = Holding(frames);
        using var video = new Mp4Frames(frames, crop, follow, plan.FrameInterval);

        // Null when nothing was asked for, and also when nothing could be opened — a
        // machine with no microphone records without one rather than not at all.
        using var track = audio.IsSilent
            ? null
            : AudioTrack.Open(audio.SystemAudio, audio.Microphone, audio.MicrophoneDeviceId);

        var source = BuildSource(sourceWidth, sourceHeight, track is not null);
        source.Starting += (_, args) =>
        {
            // Capture starts here rather than earlier, because here is when the
            // pipeline first wants a frame. Starting sooner would only fill the queue
            // with desktop from before the recording began.
            frames.Start();
            track?.Start();
            args.Request.SetActualStartPosition(TimeSpan.Zero);
        };

        source.SampleRequested += async (_, args) =>
        {
            var request = args.Request;
            var deferral = request.GetDeferral();
            try
            {
                request.Sample = track is not null && request.StreamDescriptor is AudioStreamDescriptor
                    ? await track.NextSampleAsync(frames, cancellation)
                    : await video.NextAsync(request);
            }
            catch (Exception exception)
            {
                // Ending the stream is the only useful answer here: throwing out of a
                // deferred sample request tears the process down instead, and the
                // partial recording is still worth writing. The reason is kept rather
                // than dropped, because without it the only thing that reaches the user
                // is that the sink processed no samples — which is the symptom of every
                // failure on this path and the cause of none of them.
                video.Fail(exception);
                request.Sample = null;
            }
            finally
            {
                deferral.Complete();
            }
        };

        using var output = await OpenForWritingAsync(path);
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(
            source,
            output,
            BuildProfile(plan, track is not null));

        if (!prepared.CanTranscode)
        {
            output.Dispose();
            Discard(path);
            throw new InvalidOperationException(
                $"Windows cannot record to MP4 on this machine: {prepared.FailureReason}.");
        }

        try
        {
            await prepared.TranscodeAsync();
        }
        catch (Exception exception)
        {
            // Closed before the file is removed, because it is still open for writing
            // here. A failed recording must not be left on disk: a nought-byte file in
            // the pictures folder is indistinguishable from one that worked until it is
            // opened, and the panel has already said where it went.
            output.Dispose();
            Discard(path);

            throw new InvalidOperationException(
                $"Windows would not finish the recording: {(video.Failure ?? exception).Message}"
                    + $" ({video.Kept} frames captured, {video.Repeated} repeated while the screen"
                    + $" was still, {frames.Dropped} dropped, over {frames.Elapsed:mm\\:ss})",
                video.Failure ?? exception);
        }

        // Closed here rather than left to the using below, because closing is what finishes
        // the files each source was kept in — until then they carry the length they were
        // opened with, which is none, and the result would name a pair nothing can read.
        track?.Dispose();

        DiagnosticLog.Verbose(
            $"recorded {video.Kept} frames ({video.Repeated} repeated, {frames.Dropped} dropped)"
                + $" over {frames.Elapsed:mm\\:ss}");

        return new RecordingResult(path, frames.Elapsed, video.Kept, frames.Dropped, track?.SeparateTracks);
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
        CaptureRegion? region,
        WindowRecordingArea? follow,
        int frameRate,
        CancellationToken cancellation)
    {
        var size = item.Size;
        var crop = CropperOrNull(size, region);
        var plan = GifRecordingPlan.Resolve(
            follow?.Width ?? crop?.Width ?? size.Width,
            follow?.Height ?? crop?.Height ?? size.Height,
            frameRate);

        var timing = new GifFrameTiming();

        using var frames = new FrameStream(Device(), item, plan.FrameInterval, cancellation);
        using var held = Holding(frames);
        using var output = await OpenForWritingAsync(path);

        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.GifEncoderId, output);
        await SetLoopingAsync(encoder);

        frames.Start();

        GifFrame? previous = null;
        var written = 0;

        while (written < GifRecordingPlan.MaximumFrames && await frames.NextAsync() is { } timed)
        {
            var current = await ToGifFrameAsync(timed, plan, crop, follow);
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
    /// Describes the frames as they are handed over: uncompressed BGRA, the size of
    /// what is actually put in each sample — the whole item, or the crop of it. Scaling
    /// to the encoded size is the transcoder's job, and claiming a size the samples do
    /// not have would corrupt every frame.
    /// </summary>
    private static MediaStreamSource BuildSource(int width, int height, bool withAudio)
    {
        var properties = VideoEncodingProperties.CreateUncompressed(
            MediaEncodingSubtypes.Bgra8,
            (uint)width,
            (uint)height);

        var video = new VideoStreamDescriptor(properties);

        // With no audio track the descriptor is left out entirely rather than added and
        // starved: a stream nothing ever writes to leaves the file waiting for samples
        // that never come.
        var source = withAudio
            ? new MediaStreamSource(video, new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(
                (uint)AudioPlan.SampleRate,
                (uint)AudioPlan.Channels,
                (uint)AudioPlan.BitsPerSample)))
            : new MediaStreamSource(video);

        // A live source: buffering ahead would mean the recording lagged what is
        // on screen, and there is nothing to buffer ahead of anyway.
        source.BufferTime = TimeSpan.Zero;
        return source;
    }

    /// <remarks>
    /// Built from a stock profile and then overridden, because a profile carries far
    /// more than the four values below — container, codec, profile level — and
    /// assembling one field by field means owning every default it has.
    /// </remarks>
    private static MediaEncodingProfile BuildProfile(RecordingPlan plan, bool withAudio)
    {
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);

        // A recording with no sound gets no audio stream at all: one nothing ever writes
        // to would leave the file waiting for samples that never come. Null-forgiving
        // because the projection does not admit that dropping the stream is allowed,
        // which it is and which is the documented way to do it.
        profile.Audio = withAudio
            ? AudioEncodingProperties.CreateAac(
                (uint)AudioPlan.SampleRate,
                (uint)AudioPlan.Channels,
                AudioPlan.Bitrate)
            : null!;

        profile.Video.Width = (uint)plan.Width;
        profile.Video.Height = (uint)plan.Height;
        profile.Video.Bitrate = plan.Bitrate;
        profile.Video.FrameRate.Numerator = (uint)plan.FrameRate;
        profile.Video.FrameRate.Denominator = 1;
        return profile;
    }

    /// <summary>
    /// The rectangle of the item a recording keeps, or null when it keeps all of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null rather than the whole item, because the difference is not cosmetic: with no
    /// crop the MP4 path hands the compositor's texture straight to the encoder and the
    /// pixels never touch the CPU, while a crop has to copy every frame down, cut the
    /// rectangle out and hand back a buffer. That is what recording part of a display
    /// costs, and it is only paid when part of one was asked for.
    /// </para>
    /// <para>
    /// The rectangle comes back even-sided and inside the item, for the reason
    /// <see cref="RecordingPlan"/> gives: H.264 stores colour at half resolution in each
    /// direction, and the encoder refuses a profile with an odd dimension. Rounding the
    /// source rather than only the profile keeps the two agreeing, which is what stops
    /// the transcoder scaling a 401-pixel crop into a 400-pixel video.
    /// </para>
    /// </remarks>
    private static RecordedArea? CropperOrNull(SizeInt32 size, CaptureRegion? region)
    {
        if (region is not { } wanted)
        {
            return null;
        }

        var inside = wanted.Intersect(new CaptureRegion(0, 0, size.Width, size.Height));
        if (inside.IsEmpty)
        {
            throw new InvalidOperationException("That region is not on the display being recorded.");
        }

        var left = (int)Math.Floor(inside.X);
        var top = (int)Math.Floor(inside.Y);
        var width = Math.Max(2, (int)Math.Floor(inside.Right) - left);
        var height = Math.Max(2, (int)Math.Floor(inside.Bottom) - top);
        width -= width % 2;
        height -= height % 2;

        // Rounding a one-pixel sliver up to the two the encoder needs can push it off the
        // edge it was clamped to, so the corner gives way rather than the size: the size
        // is what the stream was told, and a sample short of it is a corrupt frame.
        left = Math.Max(0, Math.Min(left, size.Width - width));
        top = Math.Max(0, Math.Min(top, size.Height - height));

        return left == 0 && top == 0 && width == size.Width && height == size.Height
            ? null
            : new RecordedArea(left, top, width, height);
    }

    /// <summary>
    /// Copies one captured frame down to the CPU, cuts the recorded rectangle out of it,
    /// and hands back a buffer the encoder can take a sample from.
    /// </summary>
    /// <remarks>
    /// A frame the rectangle no longer fits in — the display's resolution changed under
    /// the recording — is refused rather than cropped short. The stream declared a size
    /// once and cannot take a smaller sample; the caller turns the refusal into the end
    /// of the file, which keeps what was recorded up to that point.
    /// </remarks>
    private static async Task<IBuffer> CropToBufferAsync(Direct3D11CaptureFrame frame, RecordedArea crop)
    {
        using (frame)
        {
            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);

            if (bitmap.PixelWidth < crop.Left + crop.Width || bitmap.PixelHeight < crop.Top + crop.Height)
            {
                throw new InvalidOperationException("The display changed size under the recording.");
            }

            var length = checked(bitmap.PixelWidth * bitmap.PixelHeight * 4);
            var pixels = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                bitmap.CopyToBuffer(pixels.AsBuffer(0, length));
                return CutOut(bitmap.PixelWidth, bitmap.PixelHeight, pixels, length, crop);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pixels);
            }
        }
    }

    /// <summary>
    /// The crop, as the buffer the encoder reads, out of a whole frame already in memory.
    /// </summary>
    /// <remarks>
    /// Split out of the method above because it is where the spans are: a
    /// <see cref="Span{T}"/> cannot be held across an <c>await</c>, and nothing here has
    /// one to wait for.
    /// </remarks>
    private static IBuffer CutOut(int frameWidth, int frameHeight, byte[] pixels, int length, RecordedArea crop)
    {
        var cropped = ArrayPool<byte>.Shared.Rent(checked(crop.Width * crop.Height * 4));
        try
        {
            FrameTransforms.CropInto(
                frameWidth,
                frameHeight,
                pixels.AsSpan(0, length),
                crop.AsRegion,
                cropped.AsSpan(0, crop.Width * crop.Height * 4));

            return AsEncoderBuffer(crop.Width, crop.Height, cropped.AsSpan(0, crop.Width * crop.Height * 4));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(cropped);
        }
    }

    /// <summary>
    /// Copies one captured frame down to the CPU and fits the pinned rectangle of a
    /// followed window into a buffer of the size the encoder was promised.
    /// </summary>
    /// <remarks>
    /// The content size is read off the frame before anything is copied, because it is
    /// what says how much of the pool's buffer is this delivery. They differ from the
    /// moment the window is resized, and reading the difference as pixels would keep the
    /// picture from before the resize in every frame after it.
    /// </remarks>
    private static async Task<IBuffer> FitToBufferAsync(Direct3D11CaptureFrame frame, WindowRecordingArea area)
    {
        using (frame)
        {
            var content = frame.ContentSize;
            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);

            var length = checked(bitmap.PixelWidth * bitmap.PixelHeight * 4);
            var pixels = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                bitmap.CopyToBuffer(pixels.AsBuffer(0, length));
                return FitIn(bitmap.PixelWidth, bitmap.PixelHeight, pixels, length, content, area);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pixels);
            }
        }
    }

    /// <summary>
    /// The pinned rectangle, as the buffer the encoder reads, out of a whole frame already
    /// in memory. Split out for the reason <see cref="CutOut"/> is.
    /// </summary>
    private static IBuffer FitIn(
        int frameWidth,
        int frameHeight,
        byte[] pixels,
        int length,
        SizeInt32 content,
        WindowRecordingArea area)
    {
        var fitted = ArrayPool<byte>.Shared.Rent(checked(area.Width * area.Height * 4));
        try
        {
            area.FitInto(
                frameWidth,
                frameHeight,
                pixels.AsSpan(0, length),
                content.Width,
                content.Height,
                fitted.AsSpan(0, area.Width * area.Height * 4));

            return AsEncoderBuffer(area.Width, area.Height, fitted.AsSpan(0, area.Width * area.Height * 4));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(fitted);
        }
    }

    /// <summary>
    /// Turns a top-down BGRA frame into the buffer the encoder reads, which is bottom-up.
    /// </summary>
    /// <remarks>
    /// Media Foundation reads an uncompressed RGB type from the bottom row up, and a
    /// <see cref="SoftwareBitmap"/> copied down from a capture surface is top-down, so
    /// every recording that went through main memory — a crop, and a followed window —
    /// came out upside down. A full-screen one did not: handing over the Direct3D texture
    /// instead carries its orientation with it.
    ///
    /// Declaring a negative <c>MF_MT_DEFAULT_STRIDE</c> on the stream is what the format
    /// offers for saying "this one is top-down", and it was tried first. It changed
    /// nothing — the properties bag on <see cref="VideoEncodingProperties"/> did not reach
    /// the media type, or did not reach it as the UINT32 the attribute is — and a
    /// declaration nothing reads is worse than no declaration. Rewriting the rows is the
    /// thing that can be seen to work, and it costs one pass over a buffer that has
    /// already been copied twice by the time it gets here.
    /// </remarks>
    private static IBuffer AsEncoderBuffer(int width, int height, ReadOnlySpan<byte> topDownPixels) =>
        FrameTransforms.FlipVertical(width, height, topDownPixels).AsBuffer();

    /// <summary>
    /// Copies one captured frame down to the CPU, cuts the recorded rectangle out of it
    /// if there is one, and shrinks it to the size the GIF is being written at.
    /// </summary>
    private static async Task<GifFrame> ToGifFrameAsync(
        TimedFrame timed,
        GifRecordingPlan plan,
        RecordedArea? crop,
        WindowRecordingArea? follow)
    {
        using (timed.Frame)
        {
            // Before the copy: the frame says how much of the surface is this delivery
            // rather than the one before it, and the surface outlives neither.
            var content = timed.Frame.ContentSize;
            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(timed.Frame.Surface);

            var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
            bitmap.CopyToBuffer(pixels.AsBuffer());

            var width = bitmap.PixelWidth;
            var height = bitmap.PixelHeight;
            if (follow is { } area)
            {
                pixels = area.Fit(width, height, pixels, content.Width, content.Height);
                width = area.Width;
                height = area.Height;
            }
            else if (crop is { } cropper)
            {
                (width, height, pixels) = FrameTransforms.Crop(width, height, pixels, cropper.AsRegion);
            }

            return new GifFrame(
                FrameScaler.Downscale(pixels, width, height, plan.Width, plan.Height),
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

    /// <summary>
    /// Removes the file a failed recording was being written to.
    /// </summary>
    /// <remarks>
    /// Best effort: the caller is already on its way out with the reason the recording
    /// failed, and replacing that with "and the file could not be deleted either" would
    /// report the smaller of the two problems.
    /// </remarks>
    private static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Write($"Could not remove the failed recording at '{path}': {exception.Message}");
        }
    }

    /// <summary>
    /// The part of a display a recording keeps, in that display's own pixels.
    /// </summary>
    /// <remarks>
    /// Whole pixels rather than a <see cref="CaptureRegion"/>, because everything
    /// downstream of it is counted in them: the encoder is told a width and a height,
    /// the buffer is that many bytes, and a rectangle that could be half a pixel wide
    /// would only be rounded again at each of those.
    /// </remarks>
    private readonly record struct RecordedArea(int Left, int Top, int Width, int Height)
    {
        public CaptureRegion AsRegion => new(Left, Top, Width, Height);
    }

    /// <summary>
    /// Makes <paramref name="stream"/> the one <see cref="SetPaused"/> talks to, until
    /// the returned scope is disposed. Both recording paths take one.
    /// </summary>
    private IDisposable Holding(FrameStream stream)
    {
        lock (_pauseGate)
        {
            _running = stream;
        }

        return new HeldStream(this, stream);
    }

    private sealed class HeldStream(ScreenRecorder owner, FrameStream stream) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._pauseGate)
            {
                // Only if it is still ours. A recording started before this one finished
                // tidying up would otherwise have its stream cleared out from under it.
                if (ReferenceEquals(owner._running, stream))
                {
                    owner._running = null;
                }
            }
        }
    }

    /// <summary>
    /// Turns a recording's frames into the samples the MP4 encoder asks for, and answers
    /// a request that arrives while the screen is standing still.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The compositor delivers a frame when the content changes and at no other time, so
    /// a recording of a window nobody is touching can go a minute without one. A sample
    /// request cannot be left waiting for that. Holding a deferral open is only supported
    /// while the app keeps saying so — <c>ReportSampleProgress</c>, roughly twice a second
    /// — and a request held silently across a still screen is one the pipeline gives up
    /// on. The recording then ends having delivered nothing, which is what
    /// <c>MF_E_SINK_NO_SAMPLES_PROCESSED</c> and a nought-byte file were: a window or a
    /// region that had not changed in the seconds after recording started.
    /// </para>
    /// <para>
    /// So a still screen is answered with the frame before it, timestamped now. That is
    /// also the only way the file comes out the length it was recorded for — a minute of a
    /// motionless window is a minute of video, not two frames a minute apart — and it
    /// costs the encoder almost nothing, because a frame identical to the last one is what
    /// interframe compression is for.
    /// </para>
    /// </remarks>
    private sealed class Mp4Frames(
        FrameStream frames,
        RecordedArea? crop,
        WindowRecordingArea? follow,
        TimeSpan interval) : IDisposable
    {
        /// <summary>
        /// The last sample's pixels, for the buffer paths. An <see cref="IBuffer"/> is
        /// read-only to everything downstream, so handing the same one over again is free.
        /// </summary>
        private IBuffer? _repeatable;

        /// <summary>
        /// The last sample's texture, for the path that hands surfaces straight to the
        /// encoder. Held rather than disposed when the encoder is finished with it,
        /// because that is what makes it repeatable; it goes when the next frame takes
        /// its place, or when the recording ends.
        /// </summary>
        private Direct3D11CaptureFrame? _held;

        /// <summary>
        /// When the last sample handed over was taken, so that a repeat is only ever
        /// given a timestamp later than it.
        /// </summary>
        /// <remarks>
        /// The encoder is entitled to samples in increasing order and this is the one
        /// place two could arrive out of it. It is also what a held recording does with
        /// no further handling: pausing stops the clock, so every repeat during a pause
        /// asks for a timestamp that is not later than the last, is refused, and becomes
        /// a progress report instead — which is exactly the absence a pause is meant to
        /// leave in the file.
        /// </remarks>
        private TimeSpan _delivered = TimeSpan.MinValue;

        /// <summary>Frames the recording took from the compositor.</summary>
        public int Kept { get; private set; }

        /// <summary>Frames sent again because the screen had not changed.</summary>
        public int Repeated { get; private set; }

        /// <summary>What went wrong first, or null. See <see cref="Fail"/>.</summary>
        public Exception? Failure { get; private set; }

        /// <summary>
        /// The sample for <paramref name="request"/>, or null once the recording has
        /// stopped and everything captured has been handed over.
        /// </summary>
        public async Task<MediaStreamSample?> NextAsync(MediaStreamSourceSampleRequest request)
        {
            while (true)
            {
                if (await frames.NextAsync(interval) is { } timed)
                {
                    Kept++;
                    return await KeepAsync(timed);
                }

                if (frames.IsFinished)
                {
                    // No sample is how a MediaStreamSource is told the stream is over,
                    // which is what finishes the file.
                    return null;
                }

                if (Repeat() is { } again)
                {
                    return again;
                }

                // Nothing to send: either the compositor has not delivered its first
                // frame yet, or the recording is being held. Saying so every frame is
                // what keeps the request alive across it — the documented interval is
                // every 500ms, and this is well inside that.
                request.ReportSampleProgress(0);
            }
        }

        /// <summary>
        /// Records why a sample could not be made. The first one is kept rather than the
        /// last: everything after the first failure is a consequence of it.
        /// </summary>
        public void Fail(Exception exception) => Failure ??= exception;

        public void Dispose()
        {
            _held?.Dispose();
            _held = null;
        }

        private async Task<MediaStreamSample> KeepAsync(TimedFrame timed)
        {
            if (follow is { } area)
            {
                // Through main memory for the same reason a crop is, and for one more:
                // the window may not be the size it was, and only a fitted buffer is
                // still the size the stream was told.
                return Remember(await FitToBufferAsync(timed.Frame, area), timed.Timestamp);
            }

            if (crop is { } cropper)
            {
                // A copy rather than the texture: the encoder is being handed a rectangle
                // that does not exist on the GPU. The frame is finished with the moment
                // the pixels are in memory.
                return Remember(await CropToBufferAsync(timed.Frame, cropper), timed.Timestamp);
            }

            _repeatable = null;
            _held?.Dispose();
            _held = timed.Frame;

            return Surface(timed.Frame, timed.Timestamp);
        }

        private MediaStreamSample Remember(IBuffer buffer, TimeSpan timestamp)
        {
            _held?.Dispose();
            _held = null;
            _repeatable = buffer;

            return Sample(buffer, timestamp);
        }

        /// <summary>
        /// The last frame again, at now — or null when now is not later than the sample
        /// already handed over, which is the caller's cue to wait rather than send.
        /// </summary>
        private MediaStreamSample? Repeat()
        {
            var at = frames.Elapsed;
            if (at <= _delivered)
            {
                return null;
            }

            if (_repeatable is { } buffer)
            {
                Repeated++;
                return Sample(buffer, at);
            }

            if (_held is { } frame)
            {
                Repeated++;
                return Surface(frame, at);
            }

            return null;
        }

        /// <remarks>
        /// The duration is set for the reason the audio track has always set its own: an
        /// uncompressed sample without one leaves the encoder to work it out from the
        /// sample after, and the last sample of a recording has none. The picture had
        /// never set it.
        /// </remarks>
        private MediaStreamSample Sample(IBuffer buffer, TimeSpan timestamp)
        {
            _delivered = timestamp;

            var sample = MediaStreamSample.CreateFromBuffer(buffer, timestamp);
            sample.Duration = interval;
            return sample;
        }

        private MediaStreamSample Surface(Direct3D11CaptureFrame frame, TimeSpan timestamp)
        {
            _delivered = timestamp;

            var sample = MediaStreamSample.CreateFromDirect3D11Surface(frame.Surface, timestamp);
            sample.Duration = interval;
            return sample;
        }
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
    private sealed class FrameStream : IRecordingClock, IDisposable
    {
        private readonly Direct3D11CaptureFramePool _pool;
        private readonly GraphicsCaptureSession _session;
        private readonly Channel<TimedFrame> _frames;
        private readonly FrameCadence _cadence;
        private readonly Stopwatch _clock = new();
        private readonly CancellationTokenRegistration _stopping;

        /// <summary>Written from the UI thread, read on the compositor's.</summary>
        private volatile bool _paused;

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

        /// <summary>Whether the recording is being held. Read by the audio track.</summary>
        public bool IsPaused => _paused;

        /// <summary>Frames the rate did not call for.</summary>
        public int Dropped => _cadence.Dropped;

        public void Start()
        {
            _clock.Restart();
            _session.StartCapture();
        }

        /// <summary>
        /// Holds or resumes the recording.
        /// </summary>
        /// <remarks>
        /// The clock stops with it, which is what makes a pause a pause rather than a
        /// still: timestamps carry on from where they left off, so the held stretch is
        /// simply not in the file. Frames that arrive meanwhile are disposed rather than
        /// queued — each one holds a texture, and a recording paused for a minute would
        /// otherwise resume by playing that minute back.
        /// </remarks>
        public void SetPaused(bool paused)
        {
            if (paused == _paused)
            {
                return;
            }

            _paused = paused;
            if (paused)
            {
                _clock.Stop();
            }
            else
            {
                _clock.Start();
            }
        }

        /// <summary>
        /// Whether the recording has stopped and everything it captured has been taken.
        /// </summary>
        public bool IsFinished => _frames.Reader.Completion.IsCompleted;

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

        /// <summary>
        /// The next frame, or null when none arrived within <paramref name="wait"/>
        /// <em>and</em> null once the recording is over — <see cref="IsFinished"/> tells
        /// the two apart. The caller owns the frame it is given.
        /// </summary>
        /// <remarks>
        /// A still screen produces no frames at all, so the MP4 path cannot wait for one
        /// indefinitely; see <see cref="Mp4Frames"/>. The GIF path can, and uses the
        /// overload above: a GIF says how long each frame was on screen, so a motionless
        /// stretch is one frame with a long delay rather than a run of identical ones.
        /// </remarks>
        public async Task<TimedFrame?> NextAsync(TimeSpan wait)
        {
            // Tried before anything is allocated, because a recording of something that
            // is moving — which is most of them — has a frame ready every time.
            if (_frames.Reader.TryRead(out var ready))
            {
                return ready;
            }

            using var timeout = new CancellationTokenSource(wait);
            try
            {
                return await _frames.Reader.ReadAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (ChannelClosedException)
            {
                return null;
            }
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
            if (_paused || !_cadence.ShouldKeep(elapsed) || !_frames.Writer.TryWrite(new TimedFrame(frame, elapsed)))
            {
                frame.Dispose();
            }
        }
    }
}
