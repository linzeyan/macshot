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

/// <summary>
/// Writes a recording out with a zoom applied to part of it.
/// </summary>
/// <remarks>
/// <para>
/// macshot's effects band renders through <c>EffectsVideoCompositor</c>, an
/// <c>AVVideoCompositing</c> that AVFoundation hands each source frame to along with the
/// composition time, and takes back whatever Core Image drew. Windows has no equivalent
/// seat in its pipeline, which is what <c>VideoEditorWindow</c>'s notes record as the
/// reason the band was absent. This is what replaces it.
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
/// <see cref="MediaTranscoder"/>. This file is those two joined end to end with
/// <see cref="FrameZoom"/> between them. That the two halves already work on real
/// hardware is better evidence than anything a manifest experiment could have produced.
/// </para>
/// <para>
/// <strong>What it costs.</strong> Every frame of the export is decoded by seeking to it,
/// which is what <see cref="GifExporter"/> already pays and why a GIF export reports
/// progress. A zoom export pays it at the recording's own frame rate rather than a GIF's,
/// so it is several times slower than the trim-only path — which is why the trim-only path
/// is still taken whenever there is no zoom to apply. The plural
/// <c>GetThumbnailsAsync</c> would be the faster call and is not usable: it returns the
/// first frame whatever timestamps it is given (WindowsAppSDK issue 5049), which is a
/// second reason beyond the one <see cref="GifExporter"/> already records.
/// </para>
/// <para>
/// Compile-checked only. Nothing in continuous integration has an encoder or a recording
/// to encode.
/// </para>
/// </remarks>
internal static class ZoomVideoCompositor
{
    /// <summary>
    /// Reads <paramref name="source"/> between the trim handles, magnifies the frames
    /// <paramref name="segment"/> covers, and writes the result to
    /// <paramref name="destination"/>.
    /// </summary>
    /// <param name="sourceWidth">
    /// The recording's own frame size. Frames are decoded at it and the zoom rectangle is
    /// measured in it, so that magnifying twice reads twice the detail rather than
    /// stretching an already-shrunk frame.
    /// </param>
    /// <param name="frameRate">
    /// What the output runs at. The source is sampled at this rate rather than at its own,
    /// because a zoom is a continuous curve and the export needs a frame wherever it is
    /// asked for one.
    /// </param>
    public static async Task WriteAsync(
        StorageFile source,
        StorageFile destination,
        VideoTrim trim,
        VideoZoomSegment segment,
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight,
        int frameRate,
        int bitrate,
        IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameRate);

        var composition = new MediaComposition();

        // The whole clip, untrimmed. A segment's times are on the source clock so that it
        // survives the trim handles being dragged under it, and seeking into an untrimmed
        // composition is what keeps those two clocks the same one.
        composition.Clips.Add(await MediaClip.CreateFromFileAsync(source));

        var interval = TimeSpan.FromSeconds(1.0 / frameRate);
        var total = Math.Max(1, (int)Math.Round(trim.Duration * frameRate));
        var written = 0;

        var stream = new MediaStreamSource(new VideoStreamDescriptor(
            VideoEncodingProperties.CreateUncompressed(
                MediaEncodingSubtypes.Bgra8,
                (uint)outputWidth,
                (uint)outputHeight)))
        {
            Duration = TimeSpan.FromSeconds(trim.Duration),

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
                if (written >= total)
                {
                    // No sample is how a MediaStreamSource says the stream is over, which
                    // is what finishes the file.
                    request.Sample = null;
                    return;
                }

                var at = written * interval;
                var pixels = await ZoomedFrameAsync(
                    composition,
                    segment,
                    trim.Start + at.TotalSeconds,
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
            catch (Exception)
            {
                // Ending the stream is the only useful answer here: throwing out of a
                // deferred sample request tears the process down instead.
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
            Profile(outputWidth, outputHeight, frameRate, bitrate));

        if (!prepared.CanTranscode)
        {
            throw new InvalidOperationException(
                $"Windows cannot write this export on this machine ({prepared.FailureReason}).");
        }

        await prepared.TranscodeAsync();

        if (written == 0)
        {
            throw new InvalidOperationException("The recording had no frame at the moment the export starts.");
        }
    }

    /// <summary>
    /// The frame at <paramref name="seconds"/>, with the zoom of that moment applied,
    /// as BGRA bytes at the output size.
    /// </summary>
    private static async Task<byte[]> ZoomedFrameAsync(
        MediaComposition composition,
        VideoZoomSegment segment,
        double seconds,
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight)
    {
        // NearestFrame rather than the nearest key frame, as in GifExporter: key frames in
        // a screen recording can be seconds apart, and an export sampled at them would hold
        // still and then jump.
        using var thumbnail = await composition.GetThumbnailAsync(
            TimeSpan.FromSeconds(seconds),
            sourceWidth,
            sourceHeight,
            VideoFramePrecision.NearestFrame);

        var decoder = await BitmapDecoder.CreateAsync(thumbnail);
        var frame = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,

            // As everywhere else this port encodes a capture: the alpha byte is undefined,
            // and honouring it would punch holes in the picture.
            BitmapAlphaMode.Ignore,

            // Scaled here as well as at the thumbnail, because a thumbnail is fitted inside
            // what it is asked for and comes back short on one edge for any aspect ratio
            // that does not match. Every sample must be the size the stream declared.
            new BitmapTransform { ScaledWidth = (uint)sourceWidth, ScaledHeight = (uint)sourceHeight },
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        return FrameZoom.Sample(
            frame.DetachPixelData(),
            sourceWidth,
            sourceHeight,
            segment.SourceRectAt(seconds, sourceWidth, sourceHeight),
            outputWidth,
            outputHeight);
    }

    /// <remarks>
    /// Built from a stock profile and then overridden, for the reason
    /// <see cref="ScreenRecorder"/> gives: a profile carries container, codec and profile
    /// level as well, and assembling one field by field means owning every default it has.
    /// </remarks>
    private static MediaEncodingProfile Profile(int width, int height, int frameRate, int bitrate)
    {
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);

        // No audio stream at all rather than one nothing ever writes to, which would leave
        // the file waiting for samples that never come. Null-forgiving because the
        // projection does not admit that dropping the stream is allowed, which it is and
        // which is the documented way to do it.
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
}
