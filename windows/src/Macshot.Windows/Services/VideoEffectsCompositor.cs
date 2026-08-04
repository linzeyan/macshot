using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

// Imported rather than qualified for the same reason as in ScreenRecorder: inside
// namespace Macshot.Windows the name "Windows" binds to Macshot.Windows.
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Macshot.Windows.Services;

/// <summary>A caption and the pixels it was rasterized to.</summary>
/// <remarks>
/// Paired up before the export starts rather than looked up inside the loop, because the
/// raster depends only on the output size and the segment's rectangle, and neither
/// changes once Save has been pressed.
/// </remarks>
internal sealed record VideoCaption(VideoTextSegment Segment, FrameOverlay.VideoCaptionRaster Raster);

/// <summary>
/// Writes a recording out with the effects band applied to it.
/// </summary>
/// <remarks>
/// <para>
/// macshot's effects band renders through <c>EffectsVideoCompositor</c>, an
/// <c>AVVideoCompositing</c> that AVFoundation hands each source frame to along with the
/// composition time, and takes back whatever Core Image drew. Windows has no equivalent
/// seat in its pipeline. This is what replaces it.
/// </para>
/// <para>
/// <strong>Why not a video effect.</strong> The nearest thing Windows offers is
/// <c>MediaClip.VideoEffectDefinitions</c>, which takes a
/// <c>VideoEffectDefinition(activatableClassId)</c> — a WinRT <em>class name</em> that the
/// media pipeline activates by string at run time. It is not impossible here, but the
/// shape of it is wrong for this app. A packaged app registers the name in its appx
/// manifest; an unpackaged one, which this is (<c>WindowsPackageType=None</c>, no MSIX
/// identity), carries a registration-free entry in <c>app.manifest</c> naming
/// <c>WinRT.Host.dll</c> — because the activation factory has to be a native export, and
/// a C# class has none. So the effect cannot live in this executable at all: it needs a
/// second project, a CsWinRT component with its own winmd, loaded through that host shim.
/// </para>
/// <para>
/// And then the part that decides it. The host shim loads the component into its own
/// assembly load context, which does not resolve against a .NET
/// <em>self-contained</em> publish — precisely how this app is released. That was
/// CsWinRT issue 1277, open from 2022 and fixed only in 2.3.1 behind an opt-in
/// (<c>CsWinRTLoadComponentsInDefaultALC</c>). The failure mode is the worst kind: a
/// framework-dependent build on a developer's machine works, warnings-as-errors passes,
/// the tests pass, and the shipped self-contained zip throws on the user's first export.
/// Nothing this port can run would catch it.
/// </para>
/// <para>
/// <strong>What is used instead.</strong> Both halves of a manual pipeline are already
/// shipping in this port and are ordinary SDK types that need no activation by name:
/// <see cref="GifExporter"/> reads a finished recording frame by frame through
/// <see cref="MediaComposition.GetThumbnailAsync"/>, and <see cref="ScreenRecorder"/>
/// writes an MP4 by feeding a <see cref="MediaStreamSource"/> to a
/// <see cref="MediaTranscoder"/>. This file is those two joined end to end, with
/// <see cref="VideoTimeline"/> deciding which source instant each output frame comes from
/// and <see cref="FrameZoom"/>, <see cref="FrameCensor"/> and <see cref="FrameOverlay"/>
/// deciding what is done to it once it arrives.
/// </para>
/// <para>
/// <strong>The audio.</strong> Frames written this way carry none, which is why a zoom
/// export used to be silent. The fix is a second pass: the frames go to a scratch file,
/// and a <see cref="MediaComposition"/> then puts that file's video beside the original
/// recording's audio, one <see cref="BackgroundAudioTrack"/> per stretch that still plays
/// at 1×, each trimmed to its own piece of source and delayed to where the timeline puts
/// it. That is the only route Windows offers — there is no muxer here that would copy an
/// encoded video track next to a new audio one — so the second pass re-encodes the video
/// once more. It is paid only when the recording has audio to carry, which a great many
/// screen recordings do not.
/// </para>
/// <para>
/// <strong>What it costs.</strong> Every frame of the export is decoded by seeking to it,
/// which is what <see cref="GifExporter"/> already pays and why a GIF export reports
/// progress. An effects export pays it at the recording's own frame rate rather than a
/// GIF's, so it is several times slower than the trim-only path — which is why the
/// trim-only path is still taken whenever the band is holding nothing that needs this.
/// The plural <c>GetThumbnailsAsync</c> would be the faster call and is not usable: it
/// returns the first frame whatever timestamps it is given (WindowsAppSDK issue 5049),
/// which is a second reason beyond the one <see cref="GifExporter"/> already records.
/// </para>
/// <para>
/// Compile-checked only. Nothing in continuous integration has an encoder or a recording
/// to encode.
/// </para>
/// </remarks>
internal static class VideoEffectsCompositor
{
    /// <summary>
    /// Reads <paramref name="source"/>, applies <paramref name="effects"/>, and writes the
    /// result to <paramref name="destination"/>.
    /// </summary>
    /// <param name="sourceWidth">
    /// The recording's own frame size. Frames are decoded at it and a zoom's rectangle is
    /// measured in it, so that magnifying twice reads twice the detail rather than
    /// stretching an already-shrunk frame.
    /// </param>
    /// <param name="frameRate">
    /// What the output runs at. The source is sampled at this rate rather than at its own,
    /// because a ramp is a continuous curve and the export needs a frame wherever it is
    /// asked for one.
    /// </param>
    /// <param name="hasAudio">
    /// Whether the recording has a track worth carrying. Asked of the file rather than
    /// assumed, because the second pass exists only to carry it and a recording with none
    /// should not pay for a re-encode that would add silence.
    /// </param>
    public static async Task WriteAsync(
        StorageFile source,
        StorageFile destination,
        VideoTrim trim,
        VideoEffects effects,
        IReadOnlyList<VideoCaption> captions,
        double sourceSeconds,
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight,
        int frameRate,
        int bitrate,
        bool hasAudio,
        IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(captions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameRate);

        var pieces = effects.Pieces(trim);
        var map = VideoTimeline.TimeMap(pieces);
        var outputSeconds = VideoTimeline.TotalOutputSeconds(pieces);

        if (outputSeconds <= 0)
        {
            throw new InvalidOperationException(
                "The cuts and the trim between them leave nothing of this recording to export.");
        }

        IReadOnlyList<VideoAudioRun> runs = hasAudio ? VideoTimeline.AudioRuns(pieces) : [];
        var carriesAudio = runs.Count > 0;

        // Written straight to where it was asked for when there is no audio to add, so a
        // silent recording is encoded once rather than twice.
        var frames = carriesAudio ? await ScratchFileAsync() : destination;

        try
        {
            await WriteFramesAsync(
                source,
                frames,
                map,
                effects,
                captions,
                outputSeconds,
                sourceWidth,
                sourceHeight,
                outputWidth,
                outputHeight,
                frameRate,
                bitrate,
                progress);

            if (carriesAudio)
            {
                await MuxAsync(
                    frames,
                    source,
                    destination,
                    runs,
                    sourceSeconds,
                    outputWidth,
                    outputHeight,
                    frameRate,
                    bitrate);
            }
        }
        finally
        {
            if (carriesAudio)
            {
                // Best effort. A scratch file left behind in the temporary directory is
                // untidy; an export that failed because it could not tidy up would be
                // worse, and the name says what left it there.
                try
                {
                    await frames.DeleteAsync();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException)
                {
                }
            }
        }
    }

    private static async Task<StorageFile> ScratchFileAsync()
    {
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetTempPath());

        return await folder.CreateFileAsync(
            "macshot-effects.mp4",
            CreationCollisionOption.GenerateUniqueName);
    }

    /// <summary>Writes every frame of the export, with no audio track at all.</summary>
    private static async Task WriteFramesAsync(
        StorageFile source,
        StorageFile destination,
        IReadOnlyList<VideoTimeMapEntry> map,
        VideoEffects effects,
        IReadOnlyList<VideoCaption> captions,
        double outputSeconds,
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight,
        int frameRate,
        int bitrate,
        IProgress<double>? progress)
    {
        var composition = new MediaComposition();

        // The whole clip, untrimmed. A segment's times are on the source clock so that it
        // survives the trim handles being dragged under it, and seeking into an untrimmed
        // composition is what keeps those two clocks the same one.
        composition.Clips.Add(await MediaClip.CreateFromFileAsync(source));

        var interval = TimeSpan.FromSeconds(1.0 / frameRate);
        var total = Math.Max(1, (int)Math.Round(outputSeconds * frameRate));
        var written = 0;
        var reader = new FrameReader(composition, sourceWidth, sourceHeight);
        Exception? failure = null;

        var stream = new MediaStreamSource(new VideoStreamDescriptor(
            VideoEncodingProperties.CreateUncompressed(
                MediaEncodingSubtypes.Bgra8,
                (uint)outputWidth,
                (uint)outputHeight)))
        {
            Duration = TimeSpan.FromSeconds(outputSeconds),

            // Read straight through. Nothing asks this stream to seek — the transcoder
            // walks it from the start — and admitting to seeking would mean answering a
            // seek request by re-deriving the frame index from a timestamp for no gain.
            CanSeek = false,
        };

        stream.Starting += (_, args) => args.Request.SetActualStartPosition(TimeSpan.Zero);

        stream.SampleRequested += async (_, args) =>
        {
            var request = args.Request;
            var deferral = request.GetDeferral();
            try
            {
                if (written >= total || failure is not null)
                {
                    // No sample is how a MediaStreamSource says the stream is over, which
                    // is what finishes the file.
                    request.Sample = null;
                    return;
                }

                var at = written * interval;
                var pixels = await FrameAtAsync(
                    reader,
                    map,
                    effects,
                    captions,
                    at.TotalSeconds,
                    sourceWidth,
                    sourceHeight,
                    outputWidth,
                    outputHeight);

                var sample = MediaStreamSample.CreateFromBuffer(pixels.AsBuffer(), at);

                // Said rather than left to the encoder to infer: an uncompressed stream
                // carries no timing of its own, and samples with no duration are written
                // at whatever rate the profile claims regardless of their timestamps.
                sample.Duration = interval;
                request.Sample = sample;

                written++;
                progress?.Report(written / (double)total);
            }
            catch (Exception error)
            {
                // Kept rather than swallowed. Throwing out of a deferred sample request
                // tears the process down, so the stream is ended here and the reason is
                // raised once the transcoder has finished with it — otherwise a failure
                // halfway through would be indistinguishable from a short recording.
                failure = error;
                request.Sample = null;
            }
            finally
            {
                deferral.Complete();
            }
        };

        using var output = await destination.OpenAsync(FileAccessMode.ReadWrite);

        // Truncated first: the file may already exist, and a shorter export written over a
        // longer one would otherwise keep the old one's tail.
        output.Size = 0;

        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(
            stream,
            output,
            SilentProfile(outputWidth, outputHeight, frameRate, bitrate));

        if (!prepared.CanTranscode)
        {
            throw new InvalidOperationException(
                $"Windows cannot write this export on this machine ({prepared.FailureReason}).");
        }

        await prepared.TranscodeAsync();

        if (failure is not null)
        {
            throw new InvalidOperationException(
                $"macshot stopped after {written} frames: {failure.Message}",
                failure);
        }

        if (written == 0)
        {
            throw new InvalidOperationException("The recording had no frame at the moment the export starts.");
        }
    }

    /// <summary>
    /// The frame the exported file shows at <paramref name="outputSeconds"/>, with
    /// everything the band asks for already applied, as BGRA bytes at the output size.
    /// </summary>
    private static async Task<byte[]> FrameAtAsync(
        FrameReader reader,
        IReadOnlyList<VideoTimeMapEntry> map,
        VideoEffects effects,
        IReadOnlyList<VideoCaption> captions,
        double outputSeconds,
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight)
    {
        var at = VideoTimeline.SourceAt(map, outputSeconds);
        var read = await reader.ReadAsync(at);
        var crop = CropAt(effects, at, sourceWidth, sourceHeight);

        var frame = FrameZoom.Sample(read.Pixels, sourceWidth, sourceHeight, crop, outputWidth, outputHeight);

        // FrameZoom hands the buffer straight back when nothing about the frame changes,
        // which during a freeze would mean drawing the censor into the very frame the next
        // output frame is about to reuse — each one darker than the last.
        if (read.Reused && ReferenceEquals(frame, read.Pixels))
        {
            frame = (byte[])read.Pixels.Clone();
        }

        foreach (var censor in effects.Censors)
        {
            var strength = censor.OpacityAt(at);
            if (strength <= 0)
            {
                continue;
            }

            FrameCensor.Apply(
                frame,
                outputWidth,
                outputHeight,
                VideoOverlayGeometry.OutputRect(
                    censor.Rect,
                    crop,
                    sourceWidth,
                    sourceHeight,
                    outputWidth,
                    outputHeight),
                censor.Style,
                strength);
        }

        // After the censors, so a caption placed over one stays readable rather than being
        // blurred along with what it is labelling. macshot composites in the same order.
        foreach (var caption in captions)
        {
            var strength = caption.Segment.OpacityAt(at);
            if (strength <= 0)
            {
                continue;
            }

            FrameOverlay.Composite(
                frame,
                outputWidth,
                outputHeight,
                caption.Raster.Pixels,
                caption.Raster.Width,
                caption.Raster.Height,
                VideoOverlayGeometry.OutputRect(
                    caption.Segment.Rect,
                    crop,
                    sourceWidth,
                    sourceHeight,
                    outputWidth,
                    outputHeight),
                strength);
        }

        return frame;
    }

    /// <summary>
    /// The part of the source frame that fills the output at <paramref name="at"/>.
    /// </summary>
    /// <remarks>
    /// The whole frame when no zoom is running, so the caller has one rectangle to work in
    /// rather than a branch on whether there is a zoom at all.
    /// </remarks>
    private static CaptureRegion CropAt(VideoEffects effects, double at, int width, int height)
    {
        foreach (var zoom in effects.Zooms)
        {
            if (zoom.Covers(at))
            {
                return zoom.SourceRectAt(at, width, height);
            }
        }

        return new CaptureRegion(0, 0, width, height);
    }

    /// <summary>
    /// Puts the frames that were just written beside the recording's own audio.
    /// </summary>
    /// <remarks>
    /// One background track per stretch that still plays at 1×: trimmed to its own piece
    /// of the recording and delayed to where the timeline puts it, which is what carries
    /// the sound across a cut without it drifting out of step with the picture.
    /// </remarks>
    private static async Task MuxAsync(
        StorageFile frames,
        StorageFile source,
        StorageFile destination,
        IReadOnlyList<VideoAudioRun> runs,
        double sourceSeconds,
        int outputWidth,
        int outputHeight,
        int frameRate,
        int bitrate)
    {
        var composition = new MediaComposition();
        composition.Clips.Add(await MediaClip.CreateFromFileAsync(frames));

        foreach (var run in runs)
        {
            if (run.Duration <= 0)
            {
                continue;
            }

            var track = await BackgroundAudioTrack.CreateFromFileAsync(source);
            track.TrimTimeFromStart = TimeSpan.FromSeconds(Math.Max(0, run.SourceStart));
            track.TrimTimeFromEnd = TimeSpan.FromSeconds(Math.Max(0, sourceSeconds - run.SourceEnd));
            track.Delay = TimeSpan.FromSeconds(Math.Max(0, run.OutputStart));
            composition.BackgroundAudioTracks.Add(track);
        }

        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);
        profile.Video.Width = (uint)outputWidth;
        profile.Video.Height = (uint)outputHeight;
        profile.Video.Bitrate = (uint)Math.Max(1, bitrate);
        profile.Video.FrameRate.Numerator = (uint)frameRate;
        profile.Video.FrameRate.Denominator = 1;

        // Precise rather than the nearest key frame, as the trim-only export asks for:
        // this pass is where the audio is cut to the run boundaries, and key frames
        // seconds apart would put those cuts somewhere else.
        var reason = await composition.RenderToFileAsync(destination, MediaTrimmingPreference.Precise, profile);

        if (reason != TranscodeFailureReason.None)
        {
            throw new InvalidOperationException(
                $"Windows could not put the audio back beside the video ({reason}).");
        }
    }

    /// <remarks>
    /// Built from a stock profile and then overridden, for the reason
    /// <see cref="ScreenRecorder"/> gives: a profile carries container, codec and profile
    /// level as well, and assembling one field by field means owning every default it has.
    /// </remarks>
    private static MediaEncodingProfile SilentProfile(int width, int height, int frameRate, int bitrate)
    {
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);

        // No audio stream at all rather than one nothing ever writes to, which would leave
        // the file waiting for samples that never come. The recording's own audio is put
        // back by MuxAsync afterwards. Null-forgiving because the projection does not admit
        // that dropping the stream is allowed, which it is and which is the documented way
        // to do it.
        profile.Audio = null!;

        profile.Video.Width = (uint)width;
        profile.Video.Height = (uint)height;

        // Set, though a MediaTranscoder is reported to ignore it (WindowsAppSDK issue
        // 4804). Left in because it costs nothing, it is what the trim-only export already
        // asks for, and a transcoder that starts honouring it should find the number the
        // rest of this window's quality control computed rather than a stock default.
        profile.Video.Bitrate = (uint)Math.Max(1, bitrate);
        profile.Video.FrameRate.Numerator = (uint)frameRate;
        profile.Video.FrameRate.Denominator = 1;
        return profile;
    }

    /// <summary>What <see cref="FrameReader.ReadAsync"/> hands back.</summary>
    /// <remarks>
    /// <see cref="Reused"/> is not a detail of the cache: it tells the caller the buffer
    /// belongs to the reader rather than to it, and that drawing on it in place would
    /// corrupt the next frame as well as this one.
    /// </remarks>
    private readonly record struct DecodedFrame(byte[] Pixels, bool Reused);

    /// <summary>
    /// Fetches source frames by timestamp, and does not fetch the same one twice running.
    /// </summary>
    /// <remarks>
    /// The cache is there for the freeze effect and pays for itself only there: a hold of
    /// macshot's longest, thirty seconds, is nine hundred output frames all showing the
    /// same instant, and seeking to it nine hundred times would make a one-second edit the
    /// slowest thing in the editor.
    /// </remarks>
    private sealed class FrameReader(MediaComposition composition, int width, int height)
    {
        /// <summary>
        /// How near two requests have to be to be the same frame. Half a frame at 120fps,
        /// which is the fastest this port records, so no two genuinely different frames
        /// are ever collapsed into one.
        /// </summary>
        private const double SameFrame = 1.0 / 240;

        private byte[]? _cached;
        private double _cachedAt;

        public async Task<DecodedFrame> ReadAsync(double at)
        {
            if (_cached is { } cached && Math.Abs(at - _cachedAt) < SameFrame)
            {
                return new DecodedFrame(cached, true);
            }

            // NearestFrame rather than the nearest key frame, as in GifExporter: key frames
            // in a screen recording can be seconds apart, and an export sampled at them
            // would hold still and then jump.
            using var thumbnail = await composition.GetThumbnailAsync(
                TimeSpan.FromSeconds(at),
                width,
                height,
                VideoFramePrecision.NearestFrame);

            var decoder = await BitmapDecoder.CreateAsync(thumbnail);
            var frame = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,

                // As everywhere else this port encodes a capture: the alpha byte is
                // undefined, and honouring it would punch holes in the picture.
                BitmapAlphaMode.Ignore,

                // Scaled here as well as at the thumbnail, because a thumbnail is fitted
                // inside what it is asked for and comes back short on one edge for any
                // aspect ratio that does not match. Every sample must be the size the
                // stream declared.
                new BitmapTransform { ScaledWidth = (uint)width, ScaledHeight = (uint)height },
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            _cached = frame.DetachPixelData();
            _cachedAt = at;

            return new DecodedFrame(_cached, false);
        }
    }
}
