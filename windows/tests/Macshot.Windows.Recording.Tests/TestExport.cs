using Macshot.Windows.Core.Capture;
using Windows.Storage;

namespace Macshot.Windows.Recording.Tests;

/// <summary>Runs the product's export the way the video editor runs it.</summary>
/// <remarks>
/// The parameters the editor computes rather than the ones it asks the user for: the
/// output is the source's own size and a fixed frame rate, so that anything the tests
/// measure is the effect and not a rescale.
/// </remarks>
internal static class TestExport
{
    public const int FrameRate = 30;

    /// <summary>Enough for 640x480 that the colours come back unambiguous.</summary>
    public const int Bitrate = 4_000_000;

    /// <returns>The file written, and whether the recording's sound came with it.</returns>
    public static async Task<(StorageFile File, bool CarriedAudio)> RunAsync(
        StorageFolder scratch,
        StorageFile source,
        double sourceSeconds,
        VideoEffects effects,
        IReadOnlyList<VideoCaption>? captions = null,
        bool hasAudio = false)
    {
        var destination = await scratch.CreateFileAsync(
            "macshot-export.mp4", CreationCollisionOption.GenerateUniqueName);

        var carried = await VideoEffectsCompositor.WriteAsync(
            source,
            destination,
            VideoTrim.Whole(sourceSeconds),
            effects,
            captions ?? [],
            sourceSeconds,
            TestVideo.Width,
            TestVideo.Height,
            TestVideo.Width,
            TestVideo.Height,
            FrameRate,
            Bitrate,
            hasAudio);

        return (destination, carried);
    }

    /// <summary>A scratch folder of this run's own, deleted with everything in it.</summary>
    public static async Task<StorageFolder> ScratchAsync()
    {
        var temp = await StorageFolder.GetFolderFromPathAsync(Path.GetTempPath());

        return await temp.CreateFolderAsync(
            "macshot-export-tests", CreationCollisionOption.GenerateUniqueName);
    }
}
