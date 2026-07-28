using Macshot.Windows.Core.Annotations;
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

    /// <summary>
    /// The drawing style is remembered across captures, so an unreadable value has
    /// to become the default here instead of reaching the renderer.
    /// </summary>
    [TestMethod]
    public void Normalized_RepairsAnUnreadableAnnotationStyle()
    {
        var settings = new CaptureSettings
        {
            AnnotationColor = "not a colour",
            AnnotationStrokeWidth = 0,
            AnnotationLineStyle = (LineStyle)42,
        }.Normalized();

        Assert.AreEqual(AnnotationStyle.Default.Color.ToHex(), settings.AnnotationColor);
        Assert.AreEqual(CaptureSettings.MinStrokeWidth, settings.AnnotationStrokeWidth);
        Assert.AreEqual(LineStyle.Solid, settings.AnnotationLineStyle);
    }

    [TestMethod]
    public void AnnotationStyle_RoundTripsThroughTheSettings()
    {
        var style = new AnnotationStyle(new AnnotationColor(255, 0, 0, 128), 7, LineStyle.Dotted);

        var restored = CaptureSettings.Default.WithAnnotationStyle(style).Normalized().ToAnnotationStyle();

        Assert.AreEqual(style.Color, restored.Color);
        Assert.AreEqual(style.StrokeWidth, restored.StrokeWidth);
        Assert.AreEqual(style.LineStyle, restored.LineStyle);
    }

    [TestMethod]
    public void Default_DeliversToTheClipboardAndDisk()
    {
        Assert.IsTrue(CaptureSettings.Default.CopyToClipboard);
        Assert.IsTrue(CaptureSettings.Default.AutoSave);
        Assert.AreEqual(CaptureImageFormat.Png, CaptureSettings.Default.Format);
    }
}
