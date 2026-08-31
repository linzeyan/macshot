using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Macshot.Windows.Recording.Tests;

/// <summary>One decoded frame, and the average colour of any part of it.</summary>
/// <remarks>
/// Averaged rather than sampled, because a solid colour through H.264 is solid only to
/// within the encoder's ringing — and the average over a region that is one colour is
/// that colour however the blocks landed.
/// </remarks>
internal sealed record Frame(int Width, int Height, byte[] Bgra)
{
    /// <param name="x">Left edge, 0-1 across the frame. The other three are the same.</param>
    public (byte R, byte G, byte B) Average(double x, double y, double width, double height)
    {
        var left = (int)Math.Round(x * Width);
        var top = (int)Math.Round(y * Height);
        var right = Math.Min(Width, (int)Math.Round((x + width) * Width));
        var bottom = Math.Min(Height, (int)Math.Round((y + height) * Height));

        long b = 0, g = 0, r = 0, count = 0;
        for (var row = top; row < bottom; row++)
        {
            for (var column = left; column < right; column++)
            {
                var i = ((row * Width) + column) * 4;
                b += Bgra[i];
                g += Bgra[i + 1];
                r += Bgra[i + 2];
                count++;
            }
        }

        return count == 0
            ? ((byte)0, (byte)0, (byte)0)
            : ((byte)(r / count), (byte)(g / count), (byte)(b / count));
    }
}

/// <summary>
/// Recordings whose picture says what the export did to it, and a way to read that back.
/// </summary>
/// <remarks>
/// <para>
/// The export's job is to decide <em>which source frame belongs at which output moment</em>,
/// and <em>what is drawn on it</em>. <see cref="WriteSecondsAsync"/> answers the first: a
/// colour per source second, so the colour at output time t names the second it came
/// from. <see cref="WriteQuadrantsAsync"/> answers the second: four colours in four
/// corners, so a zoom, a censor and a caption can each be measured where they landed.
/// </para>
/// <para>
/// Synthesized rather than recorded, so the suite needs no desktop and no capture — and
/// so the input is the same on every machine. The colours are corners of the colour cube,
/// which survive 4:2:0 chroma subsampling with room to spare.
/// </para>
/// </remarks>
internal static class TestVideo
{
    /// <summary>640x480, which is what <see cref="VideoEncodingQuality.Vga"/> renders.</summary>
    public const int Width = 640;

    public const int Height = 480;

    /// <summary>One colour per source second, in order.</summary>
    public static readonly (byte R, byte G, byte B)[] Palette =
    [
        (255, 0, 0),
        (0, 255, 0),
        (0, 0, 255),
        (255, 255, 0),
        (255, 0, 255),
        (0, 255, 255),
        (255, 255, 255),
        (32, 32, 32),
    ];

    /// <summary>The quadrant colours of <see cref="WriteQuadrantsAsync"/>, clockwise from
    /// the top left.</summary>
    public static readonly (byte R, byte G, byte B) TopLeft = (255, 0, 0);

    /// <inheritdoc cref="TopLeft"/>
    public static readonly (byte R, byte G, byte B) TopRight = (0, 255, 0);

    /// <inheritdoc cref="TopLeft"/>
    public static readonly (byte R, byte G, byte B) BottomRight = (255, 255, 0);

    /// <inheritdoc cref="TopLeft"/>
    public static readonly (byte R, byte G, byte B) BottomLeft = (0, 0, 255);

    /// <summary>An mp4 of <paramref name="seconds"/> seconds, a colour a second.</summary>
    public static async Task<StorageFile> WriteSecondsAsync(StorageFolder folder, int seconds)
    {
        var images = new List<StorageFile>(seconds);
        var composition = new MediaComposition();

        for (var second = 0; second < seconds; second++)
        {
            var image = await ImageAsync(folder, Solid(Palette[second % Palette.Length]));
            images.Add(image);
            composition.Clips.Add(
                await MediaClip.CreateFromImageFileAsync(image, TimeSpan.FromSeconds(1)));
        }

        return await RenderAsync(folder, composition, images);
    }

    /// <summary>An mp4 of <paramref name="seconds"/> seconds, four colours in four corners.</summary>
    public static async Task<StorageFile> WriteQuadrantsAsync(StorageFolder folder, int seconds)
    {
        var image = await ImageAsync(folder, Quadrants());
        var composition = new MediaComposition();
        composition.Clips.Add(
            await MediaClip.CreateFromImageFileAsync(image, TimeSpan.FromSeconds(seconds)));

        return await RenderAsync(folder, composition, [image]);
    }

    /// <summary>The frame <paramref name="video"/> shows at <paramref name="seconds"/>.</summary>
    public static async Task<Frame> FrameAtAsync(StorageFile video, double seconds)
    {
        var composition = new MediaComposition();
        composition.Clips.Add(await MediaClip.CreateFromFileAsync(video));

        using var stream = await composition.GetThumbnailAsync(
            TimeSpan.FromSeconds(seconds), 0, 0, VideoFramePrecision.NearestFrame);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        return new Frame((int)decoder.PixelWidth, (int)decoder.PixelHeight, pixels.DetachPixelData());
    }

    /// <summary>
    /// Which entry of <see cref="Palette"/> the whole frame at <paramref name="seconds"/> is.
    /// </summary>
    public static async Task<int> SecondShownAtAsync(StorageFile video, double seconds) =>
        Nearest((await FrameAtAsync(video, seconds)).Average(0, 0, 1, 1));

    /// <summary>How long <paramref name="video"/> runs, in seconds.</summary>
    public static async Task<double> SecondsAsync(StorageFile video) =>
        (await MediaClip.CreateFromFileAsync(video)).OriginalDuration.TotalSeconds;

    /// <summary>How far apart two colours are, so a test can say "much closer to".</summary>
    public static double Distance((byte R, byte G, byte B) a, (byte R, byte G, byte B) b) =>
        Math.Sqrt(((a.R - b.R) * (a.R - b.R))
            + ((a.G - b.G) * (a.G - b.G))
            + ((a.B - b.B) * (a.B - b.B)));

    private static int Nearest((byte R, byte G, byte B) colour)
    {
        var best = 0;
        var closest = double.MaxValue;

        for (var index = 0; index < Palette.Length; index++)
        {
            var distance = Distance(Palette[index], colour);
            if (distance < closest)
            {
                closest = distance;
                best = index;
            }
        }

        return best;
    }

    private static async Task<StorageFile> RenderAsync(
        StorageFolder folder, MediaComposition composition, IReadOnlyList<StorageFile> images)
    {
        try
        {
            var file = await folder.CreateFileAsync(
                "macshot-source.mp4", CreationCollisionOption.GenerateUniqueName);

            var result = await composition.RenderToFileAsync(
                file,
                MediaTrimmingPreference.Precise,
                MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga));

            Assert.AreEqual(
                TranscodeFailureReason.None, result, "the test video could not be rendered");

            return file;
        }
        finally
        {
            foreach (var image in images)
            {
                await image.DeleteAsync();
            }
        }
    }

    private static byte[] Solid((byte R, byte G, byte B) colour)
    {
        var pixels = new byte[Width * Height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = colour.B;
            pixels[i + 1] = colour.G;
            pixels[i + 2] = colour.R;
            pixels[i + 3] = 255;
        }

        return pixels;
    }

    private static byte[] Quadrants()
    {
        var pixels = new byte[Width * Height * 4];
        for (var row = 0; row < Height; row++)
        {
            for (var column = 0; column < Width; column++)
            {
                var colour = (row < Height / 2, column < Width / 2) switch
                {
                    (true, true) => TopLeft,
                    (true, false) => TopRight,
                    (false, true) => BottomLeft,
                    _ => BottomRight,
                };

                var i = ((row * Width) + column) * 4;
                pixels[i] = colour.B;
                pixels[i + 1] = colour.G;
                pixels[i + 2] = colour.R;
                pixels[i + 3] = 255;
            }
        }

        return pixels;
    }

    private static async Task<StorageFile> ImageAsync(StorageFolder folder, byte[] pixels)
    {
        var file = await folder.CreateFileAsync(
            "macshot-frame.png", CreationCollisionOption.GenerateUniqueName);

        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, Width, Height, 96, 96, pixels);
        await encoder.FlushAsync();

        return file;
    }
}
