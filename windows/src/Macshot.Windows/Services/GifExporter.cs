using Macshot.Windows.Core.Capture;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Macshot.Windows.Services;

/// <summary>What came of writing a video out as a GIF.</summary>
/// <param name="Frames">How many frames reached the file.</param>
/// <param name="Truncated">
/// Whether the frame ceiling stopped it early, so the caller can say so rather than hand
/// back a GIF that quietly ends before the recording does.
/// </param>
public sealed record GifExportResult(int Frames, bool Truncated);

/// <summary>
/// Writes part of a video out as an animated GIF.
/// </summary>
/// <remarks>
/// <para>
/// The video editor's GIF export. <see cref="ScreenRecorder"/> writes a GIF while the
/// screen is being recorded, from frames Windows hands it; this one starts from a file
/// that already exists, which means seeking to each moment and asking the platform for
/// the frame there.
/// </para>
/// <para>
/// Frame by frame through <see cref="MediaComposition.GetThumbnailAsync"/> rather than in
/// one batch through its plural counterpart, whose result is every thumbnail concatenated
/// into a single stream with nothing but each bitmap's own header to say where the next
/// begins. Slower, and the only version that cannot silently mis-split.
/// </para>
/// </remarks>
internal static class GifExporter
{
    /// <summary>What the GIF's own timing is measured in.</summary>
    /// <remarks>
    /// A GIF frame's delay is stored in hundredths of a second, so a rate that does not
    /// divide 100 is rounded to one that does — 15 a second becomes 7 hundredths, which
    /// plays at a little over 14. Every encoder has this; it is the format, not a
    /// shortcut taken here.
    /// </remarks>
    private const double DelayUnitsPerSecond = 100;

    private const float Dpi = 96f;

    public static async Task<GifExportResult> WriteAsync(
        StorageFile source,
        StorageFile destination,
        VideoTrim trim,
        int width,
        int height,
        int frameRate,
        IProgress<double>? progress = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var rate = Math.Clamp(frameRate, GifRecordingPlan.MinFrameRate, GifRecordingPlan.MaxFrameRate);
        var delay = (ushort)Math.Max(1, Math.Round(DelayUnitsPerSecond / rate));

        // From the delay actually written rather than from the rate asked for, so the
        // frames sampled from the source are spaced the way the file will play them back.
        // Sampling at 15 and playing at 14.3 would drift half a second a minute.
        var step = delay / DelayUnitsPerSecond;

        var composition = new MediaComposition();
        composition.Clips.Add(await MediaClip.CreateFromFileAsync(source));

        using var output = await destination.OpenAsync(FileAccessMode.ReadWrite);

        // Truncated first: the file may already exist, and a shorter GIF written over a
        // longer one would otherwise keep the old one's tail.
        output.Size = 0;

        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.GifEncoderId, output);
        await SetLoopingAsync(encoder);

        var written = 0;
        var truncated = false;

        for (var at = trim.Start; at < trim.End; at += step)
        {
            cancellation.ThrowIfCancellationRequested();

            if (written >= GifRecordingPlan.MaximumFrames)
            {
                truncated = true;
                break;
            }

            var pixels = await FrameAtAsync(composition, at, width, height);

            if (written > 0)
            {
                // The encoder starts on a frame of its own, so this moves on only once
                // there is something already written to move on from.
                await encoder.GoToNextFrameAsync();
            }

            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,

                // As everywhere else macshot encodes a capture: the alpha byte is
                // undefined, and honouring it would punch holes in the GIF.
                BitmapAlphaMode.Ignore,
                (uint)width,
                (uint)height,
                Dpi,
                Dpi,
                pixels);

            await encoder.BitmapProperties.SetPropertiesAsync(new BitmapPropertySet
            {
                { "/grctlext/Delay", new BitmapTypedValue(delay, PropertyType.UInt16) },
            });

            written++;
            progress?.Report(Math.Clamp((at - trim.Start) / Math.Max(trim.Duration, step), 0, 1));
        }

        if (written == 0)
        {
            throw new InvalidOperationException("The recording had no frame at the moment the export starts.");
        }

        await encoder.FlushAsync();
        return new GifExportResult(written, truncated);
    }

    /// <summary>The frame at <paramref name="seconds"/>, scaled, as BGRA bytes.</summary>
    /// <remarks>
    /// <see cref="VideoFramePrecision.NearestFrame"/> rather than the nearest key frame:
    /// key frames in a screen recording can be seconds apart, and a GIF sampled at them
    /// would hold still and then jump.
    /// </remarks>
    private static async Task<byte[]> FrameAtAsync(
        MediaComposition composition,
        double seconds,
        int width,
        int height)
    {
        using var thumbnail = await composition.GetThumbnailAsync(
            TimeSpan.FromSeconds(seconds),
            width,
            height,
            VideoFramePrecision.NearestFrame);

        var decoder = await BitmapDecoder.CreateAsync(thumbnail);
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,

            // Scaled here as well as at the thumbnail, because a thumbnail is fitted
            // inside what it is asked for and comes back short on one edge for any aspect
            // ratio that does not match. Every frame of a GIF must be the same size.
            new BitmapTransform { ScaledWidth = (uint)width, ScaledHeight = (uint)height },
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        return pixels.DetachPixelData();
    }

    /// <summary>
    /// Marks the GIF as looping forever.
    /// </summary>
    /// <remarks>
    /// A GIF plays once unless a Netscape application extension says otherwise. The block
    /// is the same one every encoder writes: the application name, then a sub-block of a
    /// loop count of zero, which means without end.
    /// </remarks>
    private static async Task SetLoopingAsync(BitmapEncoder encoder)
    {
        await encoder.BitmapContainerProperties.SetPropertiesAsync(new BitmapPropertySet
        {
            {
                "/appext/application",
                new BitmapTypedValue(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"), PropertyType.UInt8Array)
            },
            {
                "/appext/data",
                new BitmapTypedValue(new byte[] { 3, 1, 0, 0 }, PropertyType.UInt8Array)
            },
        });
    }
}
