using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Output;

[TestClass]
public sealed class RecordingFormatTests
{
    /// <summary>
    /// The extension is the only thing a player reads before it opens the file, and no
    /// two formats may claim the same one.
    /// </summary>
    [TestMethod]
    public void FileExtension_NamesTheContainerAndIsUniquePerFormat()
    {
        Assert.AreEqual(".mp4", RecordingFormat.Mp4.FileExtension());
        Assert.AreEqual(".gif", RecordingFormat.Gif.FileExtension());

        CollectionAssert.AllItemsAreUnique(
            Enum.GetValues<RecordingFormat>().Select(format => format.FileExtension()).ToList());
    }

    /// <summary>
    /// This reaches the user in the recording page's format list. The page listed
    /// <c>Enum.ToString()</c> and so offered "Mp4", which is this port's own identifier
    /// showing through — nothing anywhere else on the machine spells the format that way.
    /// </summary>
    [TestMethod]
    public void DisplayName_SpellsTheFormatTheWayEverythingElseDoes()
    {
        Assert.AreEqual("MP4", RecordingFormat.Mp4.DisplayName());
        Assert.AreEqual("GIF", RecordingFormat.Gif.DisplayName());

        foreach (var format in Enum.GetValues<RecordingFormat>())
        {
            Assert.AreNotEqual(
                format.ToString(),
                format.DisplayName(),
                $"{format} is offered under the name the enum uses.");
        }
    }
}
