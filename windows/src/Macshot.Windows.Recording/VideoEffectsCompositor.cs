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

namespace Macshot.Windows.Recording;

/// <summary>A caption and the pixels it was rasterized to.</summary>
/// <remarks>
/// Paired up before the export starts rather than looked up inside the loop, because the
/// raster depends only on the output size and the segment's rectangle, and neither
/// changes once Save has been pressed.
/// </remarks>
public sealed record VideoCaption(VideoTextSegment Segment, FrameOverlay.VideoCaptionRaster Raster);

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
/// and a <see cref="MediaComposition"/> then puts that file's video beside the recording's
/// own audio. That is the only route Windows offers — there is no muxer here that would
/// copy an encoded video track next to a new audio one — so the second pass re-encodes the
/// video once more. It is paid only when the recording has audio to carry, which a great
/// many screen recordings do not.
/// </para>
/// <para>
/// The recording's track is lifted out into a WAV of its own before either route can use
/// it, because a <see cref="BackgroundAudioTrack"/> refuses a file with video in it. What
/// happens then depends on whether anything changes how fast the sound plays. Nothing does
/// for a zoom, a censor, a caption or a cut, so those get one background track per stretch
/// still running at 1×, trimmed and delayed into place. A speed segment is the other case:
/// it has to be resampled, so the extracted PCM is re-timed by <see cref="AudioRetime"/>
/// and put back as a single track already on the output's clock. See
/// <see cref="RetimedAudioAsync"/> for why that is arithmetic rather than signal
/// processing.
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
/// Exercised end to end by <c>Macshot.Windows.Recording.Tests</c>, which synthesizes a
/// recording, exports it through here, and reads the answer back off the frames and out of
/// the track. That is why this class lives in a library of its own rather than beside the
/// window that calls it: a WinUI project cannot be loaded by a test host.
/// </para>
/// </remarks>
public static class VideoEffectsCompositor
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
    /// <returns>
    /// Whether the recording's sound made it into the export. False for a recording that
    /// had none, and false when it had some the machine would not decode — the caller is
    /// expected to say so rather than leave it to be found on playback.
    /// </returns>
    public static async Task<bool> WriteAsync(
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

        // A speed segment has to have the track resampled; everything else only moves it
        // about, which background tracks and their trims already do far more cheaply than
        // decoding the whole thing to PCM.
        var resamples = hasAudio && effects.NeedsAudioRetime;
        IReadOnlyList<VideoAudioRun> runs = hasAudio && !resamples
            ? VideoTimeline.AudioRuns(pieces)
            : [];

        var carriesAudio = resamples || runs.Count > 0;
        var scratch = new List<StorageFile>(4);

        try
        {
            // Written straight to where it was asked for when there is no audio to add,
            // so a silent recording is encoded once rather than twice.
            var frames = destination;
            if (carriesAudio)
            {
                frames = await ScratchFileAsync("macshot-effects.mp4");
                scratch.Add(frames);
            }

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
                var sound = await ExtractedAudioAsync(source, scratch);
                var retimed = resamples && sound is not null
                    ? await RetimedAudioAsync(sound, pieces, scratch)
                    : null;

                if (sound is null || (resamples && retimed is null))
                {
                    // The track could not be lifted out of the recording, or not read back
                    // as samples, on this machine. The frames are already written, so they
                    // are put where they were asked for and the caller is told the sound
                    // did not come with them — an export that failed outright because the
                    // audio could not be carried is the worse answer.
                    //
                    // Copied rather than moved: a move repoints this StorageFile at the
                    // destination, and the cleanup below would then delete the export.
                    await frames.CopyAndReplaceAsync(destination);
                    return false;
                }

                await MuxAsync(
                    frames,
                    sound,
                    destination,
                    retimed,
                    runs,
                    sourceSeconds,
                    outputWidth,
                    outputHeight,
                    frameRate,
                    bitrate);
            }

            return carriesAudio;
        }
        finally
        {
            foreach (var file in scratch)
            {
                // Best effort. A scratch file left behind in the temporary directory is
                // untidy; an export that failed because it could not tidy up would be
                // worse, and the name says what left it there.
                try
                {
                    await file.DeleteAsync();
                }
                catch (Exception error) when (error is IOException
                    or UnauthorizedAccessException or COMException or FileNotFoundException)
                {
                }
            }
        }
    }

    private static async Task<StorageFile> ScratchFileAsync(string name)
    {
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetTempPath());

        return await folder.CreateFileAsync(name, CreationCollisionOption.GenerateUniqueName);
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

                // Flipped for the same reason ScreenRecorder flips what it hands over:
                // Media Foundation reads an uncompressed RGB type from the bottom row up,
                // and everything upstream here is top-down — the decoder's frame, and the
                // rectangles the censors and captions were placed in. Missing it made
                // every effects export upside down, which no preview shows because the
                // preview plays the source rather than the buffer.
                var sample = MediaStreamSample.CreateFromBuffer(
                    FrameTransforms.FlipVertical(outputWidth, outputHeight, pixels).AsBuffer(),
                    at);

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
    /// Two ways in, and which one is used is decided by whether anything on the band
    /// changes how fast the sound plays. <paramref name="retimed"/> is one track already
    /// laid out on the output's own clock, so it goes on at the start and nothing here has
    /// to reason about where it belongs. Otherwise it is one background track per stretch
    /// that still plays at 1×, each trimmed to its own piece of the recording and delayed
    /// to where the timeline puts it, which carries the sound across a cut without paying
    /// to re-time a track that did not need changing.
    /// </remarks>
    /// <param name="sound">
    /// The recording's track, already lifted out of it by
    /// <see cref="ExtractedAudioAsync"/>. Not the recording itself: a
    /// <see cref="BackgroundAudioTrack"/> refuses a file with a video stream in it.
    /// </param>
    private static async Task MuxAsync(
        StorageFile frames,
        StorageFile sound,
        StorageFile destination,
        StorageFile? retimed,
        IReadOnlyList<VideoAudioRun> runs,
        double sourceSeconds,
        int outputWidth,
        int outputHeight,
        int frameRate,
        int bitrate)
    {
        var composition = new MediaComposition();
        composition.Clips.Add(await MediaClip.CreateFromFileAsync(frames));

        if (retimed is not null)
        {
            composition.BackgroundAudioTracks.Add(await BackgroundAudioTrack.CreateFromFileAsync(retimed));
        }

        foreach (var run in runs)
        {
            if (run.Duration <= 0)
            {
                continue;
            }

            var track = await BackgroundAudioTrack.CreateFromFileAsync(sound);
            track.TrimTimeFromStart = TimeSpan.FromSeconds(Math.Max(0, run.SourceStart));
            track.TrimTimeFromEnd = TimeSpan.FromSeconds(Math.Max(0, sourceSeconds - run.SourceEnd));
            track.Delay = TimeSpan.FromSeconds(Math.Max(0, run.OutputStart));
            composition.BackgroundAudioTracks.Add(track);
        }

        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);

        // Said outright, as ScreenRecorder says it. The audio half of an Auto profile is
        // left unresolved, and this is the one pass that has a track to encode: handed one
        // it asked for an attribute nobody had set and threw MF_E_ATTRIBUTENOTFOUND, so
        // every export of a recording with sound failed and left a file of zero bytes.
        profile.Audio = AudioEncodingProperties.CreateAac(
            (uint)AudioPlan.SampleRate,
            (uint)AudioPlan.Channels,
            AudioPlan.Bitrate);

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

    /// <summary>
    /// The largest source track this will decode to samples, in bytes.
    /// </summary>
    /// <remarks>
    /// The whole PCM track is held in memory, because the read order is monotonic but not
    /// contiguous — a speed reads every Nth frame — and a windowed reader would be a
    /// second thing to get wrong for a case that barely occurs. 400 MB is a little over
    /// half an hour at the rate macshot records, which is far past any screen recording
    /// this editor is meant for; past it the sound is dropped and the window says so,
    /// rather than the export dying of an allocation nobody could act on.
    /// </remarks>
    private const long LargestTrackBytes = 400L * 1024 * 1024;

    /// <summary>How many frames are built up before they are pushed at the file.</summary>
    private const int WriteBlockFrames = 4096;

    /// <summary>
    /// Lifts the recording's audio out of it and into a WAV of its own — or nothing when
    /// this machine will not decode it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both routes below need this, and for the same reason:
    /// <see cref="BackgroundAudioTrack.CreateFromFileAsync"/> throws <em>source clip cannot
    /// be video file</em> when it is handed a recording, so a recording's own track cannot
    /// be laid back beside its own frames without being taken out of it first. That is not
    /// a limit of the retime path — it is what made every export of a recording with sound
    /// fail, whichever effect was on the band.
    /// </para>
    /// <para>
    /// PCM rather than another compressed track. <see cref="RetimedAudioAsync"/> reads it
    /// as numbers, <see cref="MuxAsync"/> re-encodes whatever it is handed, and a scratch
    /// file that lives for the length of one export is a poor place to spend a second
    /// generation of loss.
    /// </para>
    /// </remarks>
    private static async Task<StorageFile?> ExtractedAudioAsync(
        StorageFile source,
        List<StorageFile> scratch)
    {
        var extracted = await ScratchFileAsync("macshot-source.wav");
        scratch.Add(extracted);

        var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);

        // Pinned rather than left to the quality preset. What comes back is read as numbers
        // by the retime, and a preset free to change its rate or its depth between Windows
        // versions would change what those numbers mean without anything failing.
        profile.Audio = AudioEncodingProperties.CreatePcm(
            (uint)AudioPlan.SampleRate,
            (uint)AudioPlan.Channels,
            (uint)AudioPlan.BitsPerSample);

        var prepared = await new MediaTranscoder().PrepareFileTranscodeAsync(source, extracted, profile);
        if (!prepared.CanTranscode)
        {
            return null;
        }

        await prepared.TranscodeAsync();

        return extracted;
    }

    /// <summary>
    /// Re-times the extracted track onto the export's clock, and hands back a WAV of the
    /// result — or nothing when this machine will not decode it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what macOS gets from <c>scaleTimeRange</c>, which resamples a track along
    /// with the picture and lets the pitch move with it. The pitch shift is not an
    /// approximation of that behaviour — it <em>is</em> that behaviour: playing the same
    /// samples at a different rate is what changes both the duration and the pitch, and it
    /// is what a sped-up recording is supposed to sound like. There is no time-stretching
    /// here and none is wanted.
    /// </para>
    /// <para>
    /// Which leaves only arithmetic, and the arithmetic is in <see cref="AudioRetime"/>
    /// where it is tested. This does the plumbing either side of it: a copy of frames in
    /// the order it is told, then a WAV header. PCM is what it is handed because that is
    /// the only encoding Windows will both write and let this read back as numbers.
    /// </para>
    /// <para>
    /// An <c>AudioGraph</c> would be the obvious route and is the wrong one — it renders
    /// at real time, so a two-minute recording would take two minutes of export to
    /// re-time. This pass is bounded by disk and memcpy instead.
    /// </para>
    /// </remarks>
    private static async Task<StorageFile?> RetimedAudioAsync(
        StorageFile extracted,
        IReadOnlyList<VideoPiece> pieces,
        List<StorageFile> scratch)
    {
        if ((await extracted.GetBasicPropertiesAsync()).Size > LargestTrackBytes)
        {
            return null;
        }

        var pcm = await File.ReadAllBytesAsync(extracted.Path);

        // Read rather than assumed, though the extraction asked for exactly this: a
        // transcoder that answered with something else would otherwise have its output
        // read as though it were 48 kHz stereo, which is noise rather than a failure.
        if (WavAudio.Read(pcm) is not { } wav || wav.Frames <= 0)
        {
            return null;
        }

        var spans = AudioRetime.Spans(pieces, wav.SampleRate);
        if (AudioRetime.TotalFrames(spans) <= 0)
        {
            return null;
        }

        var retimed = await ScratchFileAsync("macshot-retimed.wav");
        scratch.Add(retimed);

        // Off the UI thread: this is a straight memcpy loop over the whole track, and on
        // the dispatcher it would freeze the editor for as long as it ran.
        await Task.Run(() => WriteRetimed(retimed.Path, pcm, wav, spans));

        return retimed;
    }

    /// <summary>Copies frames where <see cref="AudioRetime"/> says they go.</summary>
    /// <remarks>
    /// Synchronous and buffered, deliberately. There are forty-eight thousand frames in a
    /// second of output and each is four bytes, so an awaited write per frame would spend
    /// far more on state machines than on the copy; a block of frames at a time makes the
    /// whole pass a sequence of memcpys into a stream that flushes in megabytes.
    /// </remarks>
    private static void WriteRetimed(string path, byte[] pcm, WavLayout wav, IReadOnlyList<AudioSpan> spans)
    {
        var frame = wav.BytesPerFrame;
        var total = AudioRetime.TotalFrames(spans);

        using var output = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1 << 20);

        output.Write(WavAudio.Header(wav.SampleRate, wav.Channels, wav.BitsPerSample, total * frame));

        var block = new byte[frame * WriteBlockFrames];
        var filled = 0;

        foreach (var span in spans)
        {
            for (var at = span.OutputFrame; at < span.OutputEnd; at++)
            {
                var read = AudioRetime.Read(span, at, wav.Frames);
                var into = block.AsSpan(filled * frame, frame);

                if (read < 0)
                {
                    // A freeze, or a span reaching past the end of a track that is a hair
                    // shorter than its video. Zeroes are silence in signed PCM.
                    into.Clear();
                }
                else
                {
                    // Both offsets fit an int because the track is capped at 400 MB above.
                    pcm.AsSpan((int)(wav.DataOffset + (read * frame)), frame).CopyTo(into);
                }

                if (++filled == WriteBlockFrames)
                {
                    output.Write(block, 0, block.Length);
                    filled = 0;
                }
            }
        }

        if (filled > 0)
        {
            output.Write(block, 0, filled * frame);
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
