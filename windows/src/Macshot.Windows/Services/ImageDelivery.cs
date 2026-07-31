using Macshot.Windows.Core.Output;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Macshot.Windows.Services;

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

    public static async Task<byte[]> EncodeAsync(CapturedFrame frame, CaptureImageFormat format, int quality)
    {
        ArgumentNullException.ThrowIfNull(frame);

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

        // Two captures inside the same second resolve to the same name, so the
        // collision check runs against the directory that is about to be written.
        var name = FilenameTemplate.ResolveUnique(
            settings.FilenameTemplate,
            DateTimeOffset.Now,
            settings.Format.FileExtension(),
            candidate => File.Exists(Path.Combine(directory, candidate)),
            new FilenameContext(windowTitle));

        var bytes = await EncodeAsync(frame, settings.Format, settings.Quality);
        var path = Path.Combine(directory, name);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
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
        var encoder = format.IsLossy()
            ? await BitmapEncoder.CreateAsync(EncoderIdOf(format), stream, QualityOptions(quality))
            : await BitmapEncoder.CreateAsync(EncoderIdOf(format), stream);

        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            // BitBlt produces BGRX pixels. The alpha byte is undefined and must not
            // make otherwise opaque screenshots transparent during encoding.
            BitmapAlphaMode.Ignore,
            (uint)frame.Width,
            (uint)frame.Height,
            96,
            96,
            frame.BgraPixels);
        await encoder.FlushAsync();
    }

    private static Guid EncoderIdOf(CaptureImageFormat format) => format switch
    {
        CaptureImageFormat.Png => BitmapEncoder.PngEncoderId,
        CaptureImageFormat.Jpeg => BitmapEncoder.JpegEncoderId,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown capture image format."),
    };

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
