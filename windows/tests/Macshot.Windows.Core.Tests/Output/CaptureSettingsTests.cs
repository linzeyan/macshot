using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Output;

[TestClass]
public sealed class CaptureSettingsTests
{
    /// <summary>
    /// The settings file is plain JSON a user can edit, and it also survives
    /// upgrades that change what a field means. Everything downstream treats these
    /// values as trusted, so repairing them has to happen here.
    /// </summary>
    [TestMethod]
    public void Normalized_ClampsValuesOutOfRange()
    {
        var settings = new CaptureSettings
        {
            Quality = 500,
            ThumbnailSeconds = 0,
        }.Normalized();

        Assert.AreEqual(CaptureSettings.MaxQuality, settings.Quality);
        Assert.AreEqual(CaptureSettings.MinThumbnailSeconds, settings.ThumbnailSeconds);
    }

    [TestMethod]
    public void Normalized_RejectsAFormatThatIsNoLongerDefined()
    {
        var settings = new CaptureSettings { Format = (CaptureImageFormat)99 }.Normalized();

        Assert.AreEqual(CaptureImageFormat.Png, settings.Format);
    }

    /// <summary>
    /// A blank directory must become null, not an empty string: an empty path would
    /// resolve relative to the process working directory and scatter captures
    /// wherever macshot happened to start.
    /// </summary>
    [TestMethod]
    public void Normalized_TreatsABlankDirectoryAsUnset()
    {
        var settings = new CaptureSettings { SaveDirectory = "   " }.Normalized();

        Assert.IsNull(settings.SaveDirectory);
    }

    [TestMethod]
    public void Normalized_RestoresTheDefaultTemplateWhenItIsBlank()
    {
        var settings = new CaptureSettings { FilenameTemplate = " " }.Normalized();

        Assert.AreEqual(FilenameTemplate.Default, settings.FilenameTemplate);
    }

    [TestMethod]
    public void Default_DeliversToTheClipboardAndDisk()
    {
        Assert.IsTrue(CaptureSettings.Default.CopyToClipboard);
        Assert.IsTrue(CaptureSettings.Default.AutoSave);
        Assert.AreEqual(CaptureImageFormat.Png, CaptureSettings.Default.Format);
    }
}
