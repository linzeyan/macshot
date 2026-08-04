using System.Text.Json;
using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Output;

[TestClass]
public sealed class CaptureImageFormatTests
{
    /// <summary>
    /// The extension is the only thing a viewer reads before it opens the file, so it has
    /// to name the container the bytes are actually in and no two formats may claim the
    /// same one. A HEIC written as <c>.jpg</c> is a file that fails to open with no
    /// explanation anywhere.
    /// </summary>
    [TestMethod]
    public void FileExtension_NamesTheContainerAndIsUniquePerFormat()
    {
        Assert.AreEqual(".png", CaptureImageFormat.Png.FileExtension());
        Assert.AreEqual(".jpg", CaptureImageFormat.Jpeg.FileExtension());
        Assert.AreEqual(".heic", CaptureImageFormat.Heic.FileExtension());

        var extensions = Enum.GetValues<CaptureImageFormat>()
            .Select(format => format.FileExtension())
            .ToList();

        CollectionAssert.AllItemsAreUnique(extensions);
    }

    /// <summary>
    /// The quality slider is shown for exactly the formats it changes. Offering it for a
    /// lossless format is a control that does nothing; hiding it for a lossy one takes
    /// away the only say the user has over the size of every file they save.
    /// </summary>
    [TestMethod]
    public void IsLossy_MarksEveryFormatTheQualitySettingReaches()
    {
        Assert.IsFalse(CaptureImageFormat.Png.IsLossy());
        Assert.IsTrue(CaptureImageFormat.Jpeg.IsLossy());
        Assert.IsTrue(CaptureImageFormat.Heic.IsLossy());
    }

    /// <summary>
    /// HEVC is an optional Windows component, so HEIC is the one format that may be
    /// absent on a machine that has every other one. Getting this wrong in either
    /// direction is a bug the user sees: marked wrongly false, the option is offered
    /// where it cannot be written; wrongly true, a format that always works is hidden.
    /// </summary>
    [TestMethod]
    public void RequiresOptionalCodec_IsTrueForHeicAlone()
    {
        Assert.IsTrue(CaptureImageFormat.Heic.RequiresOptionalCodec());
        Assert.IsFalse(CaptureImageFormat.Png.RequiresOptionalCodec());
        Assert.IsFalse(CaptureImageFormat.Jpeg.RequiresOptionalCodec());
    }

    /// <summary>
    /// Someone who chose HEIC chose a small file and accepted the artefacts. Substituting
    /// PNG when the codec is missing would honour the picture and multiply the size they
    /// picked a format to avoid, which is not the bargain they made.
    /// </summary>
    [TestMethod]
    public void Fallback_KeepsTheLossyOrLosslessCharacterOfWhatWasAsked()
    {
        foreach (var format in Enum.GetValues<CaptureImageFormat>())
        {
            Assert.AreEqual(
                format.IsLossy(),
                format.Fallback().IsLossy(),
                $"{format} falls back to a format of the opposite character.");
        }

        Assert.AreEqual(CaptureImageFormat.Jpeg, CaptureImageFormat.Heic.Fallback());
    }

    /// <summary>
    /// The encode path substitutes the fallback in one step and does not loop, so a
    /// fallback that itself needed an optional codec would be a second failure with
    /// nothing left to try — on the machine that had already failed once.
    /// </summary>
    [TestMethod]
    public void Fallback_IsAlwaysAFormatThisMachineCanCertainlyWrite()
    {
        foreach (var format in Enum.GetValues<CaptureImageFormat>())
        {
            Assert.IsFalse(
                format.Fallback().RequiresOptionalCodec(),
                $"{format} falls back to a format that may itself be missing.");
        }
    }

    /// <summary>
    /// A format that needs no optional codec is always writable, so substituting anything
    /// for it would silently save in a format nobody asked for.
    /// </summary>
    [TestMethod]
    public void Fallback_LeavesAnAlwaysAvailableFormatAlone()
    {
        Assert.AreEqual(CaptureImageFormat.Png, CaptureImageFormat.Png.Fallback());
        Assert.AreEqual(CaptureImageFormat.Jpeg, CaptureImageFormat.Jpeg.Fallback());
    }

    /// <summary>
    /// These reach the user in the preferences list and in the save dialog's file type
    /// entries. macOS names them PNG, JPEG and HEIC; "Png" in a menu would be this port
    /// leaking its own identifiers into the interface.
    /// </summary>
    [TestMethod]
    public void DisplayName_UsesTheMacAppsNamesRatherThanTheEnums()
    {
        Assert.AreEqual("PNG", CaptureImageFormat.Png.DisplayName());
        Assert.AreEqual("JPEG", CaptureImageFormat.Jpeg.DisplayName());
        Assert.AreEqual("HEIC", CaptureImageFormat.Heic.DisplayName());
    }

    /// <summary>
    /// The settings file stores the format by name, and every one of them must survive a
    /// write and a read: a format that did not round-trip would reset the user's choice
    /// on the next launch, silently, every launch.
    /// </summary>
    [TestMethod]
    public void Format_SurvivesTheSettingsFileForEveryFormat()
    {
        foreach (var format in Enum.GetValues<CaptureImageFormat>())
        {
            var json = JsonSerializer.Serialize(
                CaptureSettings.Default with { Format = format },
                CaptureSettingsJson.Options);
            var restored = JsonSerializer.Deserialize<CaptureSettings>(json, CaptureSettingsJson.Options);

            Assert.IsNotNull(restored);
            Assert.AreEqual(format, restored.Normalized().Format, $"{format} did not survive the round trip.");

            // By name, not by ordinal — the file is meant to be hand-editable, and a bare
            // number would change meaning the next time the enum gains an entry.
            StringAssert.Contains(json, $"\"{format}\"");
        }
    }
}
