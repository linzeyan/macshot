using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Core.Output;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Macshot.Windows.Services;

/// <summary>Encoded bytes together with the format they are actually in.</summary>
/// <param name="Format">
/// Not always the format that was asked for: one whose codec turns out to be missing is
/// written as its <see cref="CaptureImageFormatExtensions.Fallback"/> instead. Whoever
/// names the file must name it from this and never from what they requested, or the
/// capture lands with an extension that lies about its contents — which is worse than
/// the failure it came from, because nothing downstream can tell.
/// </param>
public readonly record struct EncodedCapture(byte[] Bytes, CaptureImageFormat Format);

/// <summary>
/// Everything that happens to a capture once it is finished: encoding it, writing
/// it out, and putting it on the clipboard.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="NativeScreenCaptureService"/> because acquisition
/// and delivery change for entirely different reasons: the capture backend is
/// moving to <c>Windows.Graphics.Capture</c>, while delivery grows formats and
/// destinations.
/// </remarks>
public static class ImageDelivery
{
    private const int ClipboardAttempts = 5;
    private const int ClipboardRetryDelayMilliseconds = 60;

    public static string ResolveDirectory(CaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.SaveDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Macshot");
    }

    /// <summary>
    /// Encodes the capture, falling back to another format rather than failing when the
    /// asked-for encoder is not on this machine.
    /// </summary>
    /// <remarks>
    /// HEIC's codec is an optional component, and a machine where it is registered but
    /// not installed only says so here — at <c>CreateAsync</c>, or as late as the flush.
    /// A capture the user has already taken is worth more than the container it was
    /// going to be in, so the substitute is written instead. The answer carries the
    /// format that was actually used because the caller has to name the file from it.
    /// WebP fails in a second way this covers: it cannot hold a side longer than 16383
    /// pixels, which is a scroll capture of a long enough page.
    /// </remarks>
    public static async Task<EncodedCapture> EncodeAsync(
        CapturedFrame frame,
        CaptureImageFormat format,
        int quality)
    {
        ArgumentNullException.ThrowIfNull(frame);

        try
        {
            return new EncodedCapture(await EncodeToBytesAsync(frame, format, quality), format);
        }
        catch (Exception exception) when (format.Fallback() != format)
        {
            // Loud in the log, because a user who set HEIC and keeps getting JPEGs has
            // nothing else to go on. Into a fresh stream: the failure may have come at
            // the flush, and the partial one would be a truncated file.
            var substitute = format.Fallback();
            DiagnosticLog.Write(
                $"The {format.DisplayName()} encoder failed ({exception.Message}); "
                + $"writing {substitute.DisplayName()} instead.");
            return new EncodedCapture(await EncodeToBytesAsync(frame, substitute, quality), substitute);
        }
    }

    private static async Task<byte[]> EncodeToBytesAsync(
        CapturedFrame frame,
        CaptureImageFormat format,
        int quality)
    {
        // Neither of these has a WIC encoder to hand a stream to: each native library
        // writes the whole file into one buffer of its own and gives it back. Branching
        // here rather than inside EncodeIntoAsync keeps that method the WIC path it reads
        // as, and the clipboard — its only other caller — is always PNG.
        //
        // Onto the pool, because both are synchronous to the last byte and every caller
        // reaches this from a button: AVIF is around 1.4s for a full screen on a modest
        // machine, and run here it would be 1.4s of a window that does not redraw. WIC
        // below is already asynchronous and needs no such help.
        if (format is CaptureImageFormat.Webp)
        {
            return await Task.Run(() => WebpEncoder.Encode(frame, quality));
        }

        if (format is CaptureImageFormat.Avif)
        {
            return await Task.Run(() => AvifEncoder.Encode(frame, quality));
        }

        using var stream = new InMemoryRandomAccessStream();
        await EncodeIntoAsync(frame, format, quality, stream);
        return await ReadAllAsync(stream);
    }

    /// <param name="windowTitle">
    /// What the captured window called itself, which is all the <c>{window}</c> token in
    /// the template needs. Null for a capture that was dragged out rather than aimed at
    /// a window, and then the token resolves to nothing.
    /// </param>
    public static async Task<string> SaveAsync(
        CapturedFrame frame,
        CaptureSettings settings,
        string? windowTitle = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(settings);

        var directory = ResolveDirectory(settings);
        Directory.CreateDirectory(directory);

        // Encoded before it is named, because the name carries the extension and the
        // encoder is what decides which format there is actually going to be.
        var encoded = await EncodeAsync(ForSaving(frame, settings), settings.Format, settings.Quality);

        // Two captures inside the same second resolve to the same name, so the
        // collision check runs against the directory that is about to be written.
        var name = FilenameTemplate.ResolveUnique(
            settings.FilenameTemplate,
            DateTimeOffset.Now,
            encoded.Format.FileExtension(),
            candidate => File.Exists(Path.Combine(directory, candidate)),
            new FilenameContext(windowTitle));

        var path = Path.Combine(directory, name);
        await File.WriteAllBytesAsync(path, encoded.Bytes);
        return path;
    }

    /// <summary>
    /// The frame as it should be written to a file: its own pixels, or the picture at the
    /// size the display was showing it when the setting asks for that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// macshot's <c>downscaleRetina</c>. A capture is taken in physical pixels, so on a
    /// display at 150% every screenshot is half again the size it looked, and a page of
    /// them is a folder several times larger than anyone expected. This hands back the
    /// picture at the size it appeared on screen.
    /// </para>
    /// <para>
    /// Files only. The clipboard and the history are deliberately left at full size: the
    /// clipboard is a paste target that may land in something printed, and the history is
    /// the net under a capture nobody saved — the same reason it is always PNG at full
    /// quality whatever the save format is. macshot draws the line in the same place.
    /// </para>
    /// <para>
    /// The scale is the one belonging to the display the capture came from, found by its
    /// own top-left corner, falling back to the primary display for a frame whose origin
    /// is on no display at all. Enumerating for every save is a handful of Win32 calls;
    /// carrying the scale on every <see cref="CapturedFrame"/> through every crop,
    /// stitch and composite it passes through would be a field to keep true in a dozen
    /// places.
    /// </para>
    /// </remarks>
    public static CapturedFrame ForSaving(CapturedFrame frame, CaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.SaveAtStandardResolution)
        {
            return frame;
        }

        double scale;
        try
        {
            var layout = MonitorEnumerator.Enumerate().Layout;
            var origin = new CapturePoint(frame.VirtualX, frame.VirtualY);
            scale = (layout.MonitorAt(origin) ?? layout.Primary).Scale;
        }
        catch (Exception exception)
        {
            // The capture is in hand and the user asked for it to be saved. Writing it at
            // the size it was taken is a worse answer than the one they chose, and a far
            // better one than an error where a file should be.
            DiagnosticLog.Write($"Could not read the display scale; saving at full size: {exception.Message}");
            return frame;
        }

        var width = (int)Math.Round(frame.Width / scale);
        var height = (int)Math.Round(frame.Height / scale);

        // Nothing to do at 100%, and nothing to do for a capture so small that the
        // reduction would round it away.
        if (scale <= 1 || width < 1 || height < 1 || (width == frame.Width && height == frame.Height))
        {
            return frame;
        }

        return new CapturedFrame(
            frame.VirtualX,
            frame.VirtualY,
            width,
            height,
            FrameScaler.Downscale(frame.BgraPixels, frame.Width, frame.Height, width, height));
    }

    /// <summary>
    /// Puts the capture on the clipboard as a PNG whatever the save format is: the
    /// clipboard is a paste target rather than an archive, so it should never be the
    /// lossy copy.
    /// </summary>
    public static async Task CopyToClipboardAsync(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        // Not disposed: the data package hands the clipboard a reference to this
        // stream, and Flush is what forces the bytes to be rendered.
        var stream = new InMemoryRandomAccessStream();
        await EncodeIntoAsync(frame, CaptureImageFormat.Png, CaptureSettings.MaxQuality, stream);

        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));

        // Any process may hold the clipboard open, and Windows then fails the call
        // outright. Retrying briefly is the documented remedy; giving up silently
        // would look like macshot simply not copying.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetContent(package);

                // Without Flush the image lives only as long as macshot does, and
                // macshot is a background tool the user will quit.
                Clipboard.Flush();
                return;
            }
            catch (Exception) when (attempt < ClipboardAttempts)
            {
                await Task.Delay(ClipboardRetryDelayMilliseconds);
            }
        }
    }

    private static async Task EncodeIntoAsync(
        CapturedFrame frame,
        CaptureImageFormat format,
        int quality,
        IRandomAccessStream stream)
    {
        var encoderId = ImageEncoders.EncoderIdOf(format);
        var encoder = format.IsLossy()
            ? await BitmapEncoder.CreateAsync(encoderId, stream, QualityOptions(quality))
            : await BitmapEncoder.CreateAsync(encoderId, stream);

        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            // BitBlt produces BGRX pixels. The alpha byte is undefined and must not
            // make otherwise opaque screenshots transparent during encoding — so it is
            // read only for the frames that say it means something, which today is the
            // cut-out Remove Background hands back.
            frame.AlphaMode,
            (uint)frame.Width,
            (uint)frame.Height,
            96,
            96,
            frame.BgraPixels);
        await encoder.FlushAsync();
    }

    private static BitmapPropertySet QualityOptions(int quality)
    {
        return new BitmapPropertySet
        {
            {
                "ImageQuality",
                new BitmapTypedValue(
                    Math.Clamp(quality, CaptureSettings.MinQuality, CaptureSettings.MaxQuality) / 100f,
                    PropertyType.Single)
            },
        };
    }

    private static async Task<byte[]> ReadAllAsync(IRandomAccessStream stream)
    {
        stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var length = await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[length];
        reader.ReadBytes(bytes);
        return bytes;
    }
}
