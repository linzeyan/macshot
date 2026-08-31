using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Macshot.Windows.Recording.Tests;

/// <summary>
/// That the media pipeline can be exercised from a test host at all.
/// </summary>
/// <remarks>
/// Everything else in this project rests on this: the export is
/// <c>Windows.Media.Editing</c> and <c>Windows.Media.Transcoding</c>, and if those need a
/// window, a package identity or an STA thread then the pipeline can only be verified by
/// starting the app. They do not — this composes a two-second clip out of solid-colour
/// images and renders it — so the rest of the suite can call the product's own compositor.
/// </remarks>
[TestClass]
public sealed class MediaStackTests
{
    /// <summary>
    /// Renders a composition and reads the result back, which is the whole shape every
    /// other test here takes: synthesize an input of known length, run it through the
    /// stack, and measure what came out.
    /// </summary>
    [TestMethod]
    public async Task RenderToFileAsync_ProducesAFileAsLongAsTheClipsThatWentIntoIt()
    {
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetTempPath());
        var red = await SolidAsync(folder, "macshot-spike-red.png", 255, 0, 0);
        var blue = await SolidAsync(folder, "macshot-spike-blue.png", 0, 0, 255);
        var destination = await folder.CreateFileAsync(
            "macshot-spike.mp4", CreationCollisionOption.GenerateUniqueName);

        try
        {
            var composition = new MediaComposition();
            composition.Clips.Add(await ImageClipAsync(red, TimeSpan.FromSeconds(1)));
            composition.Clips.Add(await ImageClipAsync(blue, TimeSpan.FromSeconds(1)));

            var result = await composition.RenderToFileAsync(
                destination,
                MediaTrimmingPreference.Precise,
                MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga));

            Assert.AreEqual(TranscodeFailureReason.None, result, "the render reported a failure");

            var written = await MediaClip.CreateFromFileAsync(destination);
            Assert.AreEqual(
                2.0,
                written.OriginalDuration.TotalSeconds,
                0.2,
                "the rendered file is not as long as the clips that went into it");
        }
        finally
        {
            foreach (var file in new[] { red, blue, destination })
            {
                await file.DeleteAsync();
            }
        }
    }

    /// <remarks>
    /// An image clip rather than a recorded one, because a test that had to record its own
    /// input could only run on a machine with a desktop — and the thing under test is what
    /// happens to frames after they are captured, not the capture.
    /// </remarks>
    private static async Task<MediaClip> ImageClipAsync(StorageFile image, TimeSpan length) =>
        await MediaClip.CreateFromImageFileAsync(image, length);

    /// <summary>One solid colour, 320x240, as a PNG on disk.</summary>
    private static async Task<StorageFile> SolidAsync(
        StorageFolder folder, string name, byte r, byte g, byte b)
    {
        const int Width = 320;
        const int Height = 240;

        var pixels = new byte[Width * Height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }

        var file = await folder.CreateFileAsync(name, CreationCollisionOption.GenerateUniqueName);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, Width, Height, 96, 96, pixels);
        await encoder.FlushAsync();

        return file;
    }
}
