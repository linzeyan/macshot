using System.Text;
using Macshot.Windows.Core.Capture;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Macshot.Windows.Recording.Tests;

/// <summary>
/// That a GIF export samples the recording where the file will play it back, and at the
/// size and over the stretch it was asked for.
/// </summary>
/// <remarks>
/// <para>
/// A GIF is the one output whose timing the format decides rather than the app: a delay is
/// stored in hundredths of a second, so 15 frames a second is not expressible and becomes 7
/// hundredths, which plays at 14.3. The export therefore has two rates in it — the one
/// asked for and the one the file will run at — and sampling at the first while writing the
/// second drifts half a second every minute. Nothing about the resulting file looks wrong;
/// it is simply out of step with what was recorded, further with every frame.
/// </para>
/// <para>
/// So these read the frames back out of the finished GIF and ask which source second each
/// one is showing, which is a question only the picture can answer.
/// </para>
/// </remarks>
[TestClass]
public sealed class GifExporterTests
{
    private const int Seconds = 6;

    /// <summary>
    /// The rate the format cannot express: 100/15 rounds to 7 hundredths, so the file plays
    /// at 14.3 and the export has to follow it there.
    /// </summary>
    private const int AwkwardRate = 15;

    private const double AwkwardStep = 0.07;

    private StorageFolder _scratch = null!;

    [TestInitialize]
    public async Task CreateScratchAsync() => _scratch = await TestExport.ScratchAsync();

    [TestCleanup]
    public async Task DeleteScratchAsync() => await _scratch.DeleteAsync();

    /// <summary>
    /// Every frame shows the source second that belongs at the moment the file will play it
    /// — which is the one thing that separates a GIF sampled at the rate asked for from one
    /// sampled at the rate written into it.
    /// </summary>
    [TestMethod]
    public async Task WriteAsync_SamplesTheSourceAtTheRateTheGifWillPlayBackAt()
    {
        var (result, gif) = await ExportAsync(new VideoTrim(0, Seconds), AwkwardRate);

        Assert.IsFalse(result.Truncated, "six seconds is nowhere near the frame ceiling");
        Assert.AreEqual(
            (int)Math.Ceiling(Seconds / AwkwardStep),
            result.Frames,
            "a frame every seven hundredths of a second is what the delay written into the file asks for");

        var decoder = await BitmapDecoder.CreateAsync(await gif.OpenReadAsync());

        Assert.AreEqual((uint)result.Frames, decoder.FrameCount, "the file holds fewer frames than were written");

        for (var index = 0; index < result.Frames; index++)
        {
            var at = index * AwkwardStep;

            // Frames landing within a source frame or two of a colour change are skipped:
            // which side of it NearestFrame picks there is not what this is asserting, and
            // the frames that separate the two sampling rates are nowhere near one — frame
            // 44 sits at 3.08s, which the rate asked for would have put at 2.93s.
            if (Math.Abs(at - Math.Round(at)) < 0.05)
            {
                continue;
            }

            Assert.AreEqual(
                (int)at,
                await SecondShownAsync(decoder, index),
                $"frame {index}, which plays {at:0.00}s in, shows the wrong source second");
        }
    }

    /// <summary>
    /// The trim handles decide both ends. A GIF that ignored them would still play, and
    /// would still be about the right length.
    /// </summary>
    /// <remarks>
    /// Half a second off each colour change, for the reason the frame loop above skips
    /// those moments: a frame sampled exactly where the picture changes may come from
    /// either side of it, and which one is not what this is about.
    /// </remarks>
    [TestMethod]
    public async Task WriteAsync_TakesOnlyWhatIsBetweenTheTrimHandles()
    {
        var (result, gif) = await ExportAsync(new VideoTrim(2.5, 4.5), 10);

        Assert.AreEqual(20, result.Frames, "two seconds at ten a second");

        var decoder = await BitmapDecoder.CreateAsync(await gif.OpenReadAsync());

        Assert.AreEqual(2, await SecondShownAsync(decoder, 0), "the first frame is not where the trim starts");
        Assert.AreEqual(4, await SecondShownAsync(decoder, 19), "the last frame is not where the trim ends");
    }

    /// <summary>
    /// Every frame comes out at the size asked for, whatever the recording's shape. A
    /// thumbnail is fitted <em>inside</em> what it is asked for, so a request that does not
    /// match the source's aspect ratio comes back short on one edge — and a GIF whose frames
    /// are not all one size is not a GIF.
    /// </summary>
    /// <param name="width">
    /// Both ways off the recording's 4:3: wider than it and taller than it, because a frame
    /// fitted inside what was asked for would be short on a different edge each way round.
    /// </param>
    [TestMethod]
    [DataRow(320, 180)]
    [DataRow(240, 320)]
    public async Task WriteAsync_ScalesEveryFrameToTheSizeAskedForRatherThanFittingItInside(
        int width, int height)
    {
        var (result, gif) = await ExportAsync(new VideoTrim(0, 1), 10, width, height);
        var decoder = await BitmapDecoder.CreateAsync(await gif.OpenReadAsync());

        for (var index = 0; index < result.Frames; index++)
        {
            var frame = await decoder.GetFrameAsync((uint)index);

            Assert.AreEqual((uint)width, frame.PixelWidth, $"frame {index} is the wrong width");
            Assert.AreEqual((uint)height, frame.PixelHeight, $"frame {index} is the wrong height");
        }
    }

    /// <summary>
    /// The GIF loops. A file without the Netscape block plays once and stops on its last
    /// frame, which is the one defect here that a person who exported one would notice and
    /// have no way to describe.
    /// </summary>
    [TestMethod]
    public async Task WriteAsync_MarksTheGifToLoopWithoutEnd()
    {
        var (_, gif) = await ExportAsync(new VideoTrim(0, 1), 10);
        var decoder = await BitmapDecoder.CreateAsync(await gif.OpenReadAsync());

        var properties = await decoder.BitmapContainerProperties.GetPropertiesAsync(
            ["/appext/application", "/appext/data"]);

        CollectionAssert.AreEqual(
            Encoding.ASCII.GetBytes("NETSCAPE2.0"),
            (byte[])properties["/appext/application"].Value,
            "the application extension is not the one a viewer looks for");

        // Sub-block length 3, extension id 1, then the loop count little-endian. Zero is
        // "forever"; anything else is a number of times.
        CollectionAssert.AreEqual(
            new byte[] { 3, 1, 0, 0 },
            (byte[])properties["/appext/data"].Value,
            "the loop count is not the one that means without end");
    }

    /// <summary>Which entry of <see cref="TestVideo.Palette"/> one frame of the GIF is.</summary>
    private static async Task<int> SecondShownAsync(BitmapDecoder decoder, int index)
    {
        var frame = await decoder.GetFrameAsync((uint)index);
        var pixels = await frame.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        var picture = new Frame((int)frame.PixelWidth, (int)frame.PixelHeight, pixels.DetachPixelData());

        return TestVideo.Nearest(picture.Average(0, 0, 1, 1));
    }

    private async Task<(GifExportResult Result, StorageFile File)> ExportAsync(
        VideoTrim trim,
        int frameRate,
        int width = TestVideo.Width,
        int height = TestVideo.Height)
    {
        var source = await TestVideo.WriteSecondsAsync(_scratch, Seconds);
        var gif = await _scratch.CreateFileAsync(
            "macshot-export.gif", CreationCollisionOption.GenerateUniqueName);

        var result = await GifExporter.WriteAsync(source, gif, trim, width, height, frameRate);

        return (result, gif);
    }
}
