using System.Text.Json;
using System.Text.Json.Nodes;
using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Output;

[TestClass]
public sealed class SettingsPortabilityTests
{
    private static readonly DateTimeOffset ExportedAt = new(2026, 8, 1, 9, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public void Export_LeavesBehindWhatBelongsToThisMachine()
    {
        // The whole reason this file exists: a save directory from another computer
        // either does not exist or belongs to someone else.
        var settings = CaptureSettings.Default with
        {
            SaveDirectory = @"C:\Users\ricky\Pictures\Macshot",
            LastSelection = new CaptureRegion(10, 20, 300, 200),
            LastSelectionDisplay = @"\\.\DISPLAY1",
        };

        var written = Settings(SettingsPortability.Export(settings, "1.0", ExportedAt));

        Assert.IsFalse(written.ContainsKey("saveDirectory"));
        Assert.IsFalse(written.ContainsKey("lastSelection"));
        Assert.IsFalse(written.ContainsKey("lastSelectionDisplay"));
    }

    [TestMethod]
    public void Export_CarriesThePreferencesThatAreAboutTheUser()
    {
        var settings = CaptureSettings.Default with
        {
            Format = CaptureImageFormat.Jpeg,
            Quality = 61,
            FilenameTemplate = "{app} {date}",
            HiddenTools = ["Loupe"],
        };

        var written = Settings(SettingsPortability.Export(settings, "1.0", ExportedAt));

        Assert.AreEqual("Jpeg", written["format"]!.GetValue<string>());
        Assert.AreEqual(61, written["quality"]!.GetValue<int>());
        Assert.AreEqual("{app} {date}", written["filenameTemplate"]!.GetValue<string>());
        Assert.AreEqual("Loupe", written["hiddenTools"]!.AsArray()[0]!.GetValue<string>());
    }

    [TestMethod]
    public void Export_NamesItselfSoAnUnrelatedFileCanBeRefused()
    {
        var envelope = Envelope(SettingsPortability.Export(CaptureSettings.Default, "3.9.1", ExportedAt));

        Assert.AreEqual("macshot-settings", envelope["type"]!.GetValue<string>());
        Assert.AreEqual(1, envelope["schemaVersion"]!.GetValue<int>());
        Assert.AreEqual("3.9.1", envelope["appVersion"]!.GetValue<string>());
        StringAssert.StartsWith(envelope["exportedAt"]!.GetValue<string>(), "2026-08-01T09:30:00");
    }

    [TestMethod]
    public void Export_OmitsTheGettersThatCannotBeReadBack()
    {
        // CaptureSettings computes the parsed hotkey bindings and the effective history
        // size. They serialize, and writing them into the file would put values in it
        // that an import can only ignore.
        var written = Settings(SettingsPortability.Export(CaptureSettings.Default, "1.0", ExportedAt));

        Assert.IsFalse(written.ContainsKey("effectiveHistorySize"));
        Assert.IsFalse(written.ContainsKey("captureAreaBinding"));
    }

    [TestMethod]
    public void RoundTrip_BringsEveryPortableSettingBack()
    {
        var settings = CaptureSettings.Default with
        {
            Format = CaptureImageFormat.Jpeg,
            Quality = 44,
            CopyToClipboard = false,
            AutoSave = false,
            ShowThumbnail = false,
            ThumbnailSeconds = 21,
            DelaySeconds = 9,
            HistorySize = 77,
            HistoryUnlimited = true,
            RecordingFrameRate = 45,
            GifFrameRate = 17,
            RecordingFormat = RecordingFormat.Gif,
            FilenameTemplate = "{window} {date}",
            RecordingFilenameTemplate = "Clip {time}",
            AnnotationColor = "#FF2200",
            AnnotationStrokeWidth = 7,
            AnnotationLineStyle = LineStyle.Dashed,
            AnnotationArrowStyle = ArrowStyle.Open,
            AnnotationCornerRadius = 12,
            SmoothPencilStrokes = false,
            RememberLastSelection = true,
            WindowSnapEnabled = false,
            HideCaptureInstructions = true,
            VerboseLogging = true,
            TranslateTargetLanguage = "ja",
            HiddenTools = ["Loupe", "Measure"],
            BeautifyStyleIndex = 12,
            BeautifyPadding = 48,
        };

        var imported = SettingsPortability.Import(
            SettingsPortability.Export(settings, "1.0", ExportedAt).Json,
            CaptureSettings.Default);

        Assert.IsTrue(imported.Succeeded, imported.Failure);

        // Compared as written rather than with record equality: CaptureSettings holds a
        // list, and a record compares that by reference, so two identical settings are
        // never equal to each other.
        Assert.AreEqual(
            SettingsPortability.Export(settings.Normalized(), "1.0", ExportedAt).Json,
            SettingsPortability.Export(imported.Settings!, "1.0", ExportedAt).Json);
    }

    [TestMethod]
    public void Import_KeepsThisMachineSOwnValuesRatherThanTheFileS()
    {
        // The selection and its display travel together — Normalized drops one without
        // the other, since a rectangle with no screen named for it means nothing.
        var here = CaptureSettings.Default with
        {
            SaveDirectory = @"D:\Shots",
            LastSelection = new CaptureRegion(4, 8, 640, 480),
            LastSelectionDisplay = @"\\.\DISPLAY2",
        };

        // A hand-edited file naming a save directory must not be able to redirect where
        // captures land on the machine it is imported into.
        var tampered = WithSetting(
            SettingsPortability.Export(CaptureSettings.Default, "1.0", ExportedAt).Json,
            "saveDirectory",
            @"\\attacker\share");

        var imported = SettingsPortability.Import(tampered, here);

        Assert.IsTrue(imported.Succeeded, imported.Failure);
        Assert.AreEqual(@"D:\Shots", imported.Settings!.SaveDirectory);
        Assert.AreEqual(@"\\.\DISPLAY2", imported.Settings.LastSelectionDisplay);
        CollectionAssert.Contains(imported.SkippedKeys.ToArray(), "saveDirectory");
    }

    [TestMethod]
    public void Import_ReturnsASettingTheFileIsSilentAboutToItsDefault()
    {
        // Replace-portable, not merge: a user who exported a tidy configuration should
        // not get their old mess back wherever the file happens to say nothing.
        var here = CaptureSettings.Default with { Quality = 30, DelaySeconds = 22 };
        var file = SettingsPortability.Export(CaptureSettings.Default with { Quality = 55 }, "1.0", ExportedAt).Json;
        var withoutDelay = Without(file, "delaySeconds");

        var imported = SettingsPortability.Import(withoutDelay, here);

        Assert.IsTrue(imported.Succeeded, imported.Failure);
        Assert.AreEqual(55, imported.Settings!.Quality);
        Assert.AreEqual(CaptureSettings.Default.DelaySeconds, imported.Settings.DelaySeconds);
    }

    [TestMethod]
    public void Import_DropsOneUnreadableValueRatherThanTheWholeFile()
    {
        // An exported file is something a user may edit by hand. Refusing all of it over
        // one bad number is the least useful answer available.
        var file = WithSetting(
            SettingsPortability.Export(CaptureSettings.Default with { Format = CaptureImageFormat.Jpeg }, "1.0", ExportedAt).Json,
            "quality",
            "very high");

        var imported = SettingsPortability.Import(file, CaptureSettings.Default);

        Assert.IsTrue(imported.Succeeded, imported.Failure);
        Assert.AreEqual(CaptureImageFormat.Jpeg, imported.Settings!.Format);
        Assert.AreEqual(CaptureSettings.Default.Quality, imported.Settings.Quality);
        CollectionAssert.Contains(imported.SkippedKeys.ToArray(), "quality");
    }

    [TestMethod]
    public void Import_IgnoresAKeyThisVersionDoesNotHave()
    {
        var file = WithSetting(
            SettingsPortability.Export(CaptureSettings.Default, "9.0", ExportedAt).Json,
            "somethingFromTheFuture",
            "yes");

        var imported = SettingsPortability.Import(file, CaptureSettings.Default);

        Assert.IsTrue(imported.Succeeded, imported.Failure);
        CollectionAssert.Contains(imported.SkippedKeys.ToArray(), "somethingFromTheFuture");
        Assert.AreEqual("9.0", imported.SourceAppVersion);
    }

    [TestMethod]
    public void Import_RefusesAFileThatIsNotThisKindOfFile()
    {
        foreach (var body in new string?[]
        {
            null,
            "",
            "   ",
            "not json at all",
            "[]",
            """{"type":"someone-elses-settings","settings":{"quality":10}}""",
            """{"type":"macshot-settings","schemaVersion":1}""",
            """{"type":"macshot-settings","schemaVersion":1,"settings":{}}""",
        })
        {
            var imported = SettingsPortability.Import(body, CaptureSettings.Default);

            Assert.IsFalse(imported.Succeeded, body ?? "null");
            Assert.IsFalse(string.IsNullOrWhiteSpace(imported.Failure), body ?? "null");
        }
    }

    [TestMethod]
    public void Import_RefusesAFileFromAVersionThatKnowsMoreThanThisOne()
    {
        // Applying half of a newer file would leave settings this version cannot see in
        // whatever state they happened to be, which is worse than not importing.
        var imported = SettingsPortability.Import(
            """{"type":"macshot-settings","schemaVersion":2,"settings":{"quality":10}}""",
            CaptureSettings.Default);

        Assert.IsFalse(imported.Succeeded);
        StringAssert.Contains(imported.Failure!, "newer version");
    }

    [TestMethod]
    public void LooksSecret_FailsClosedForACredentialNobodyHasAddedYet()
    {
        // The guarantee worth keeping from macshot: a setting named like a secret is
        // excluded the day someone adds it, without this file being touched. The
        // translation key that used to live in CaptureSettings is exactly the kind of
        // thing that comes back.
        Assert.IsTrue(SettingsPortability.LooksSecret("dropboxToken"));
        Assert.IsTrue(SettingsPortability.LooksSecret("imgbbApiKey"));
        Assert.IsTrue(SettingsPortability.LooksSecret("s3Bucket"));
        Assert.IsTrue(SettingsPortability.LooksSecret("saveDirectoryBookmark"));
        Assert.IsFalse(SettingsPortability.IsPortable("googleDriveRefreshToken"));

        Assert.IsFalse(SettingsPortability.LooksSecret("quality"));
        Assert.IsTrue(SettingsPortability.IsPortable("quality"));
    }

    private static JsonObject Envelope(SettingsExport export) =>
        JsonNode.Parse(export.Json) as JsonObject ?? throw new JsonException("not an object");

    private static JsonObject Settings(SettingsExport export) =>
        Envelope(export)["settings"] as JsonObject ?? throw new JsonException("no settings");

    private static string WithSetting(string json, string key, string value)
    {
        var envelope = JsonNode.Parse(json)!.AsObject();
        envelope["settings"]!.AsObject()[key] = value;
        return envelope.ToJsonString();
    }

    private static string Without(string json, string key)
    {
        var envelope = JsonNode.Parse(json)!.AsObject();
        envelope["settings"]!.AsObject().Remove(key);
        return envelope.ToJsonString();
    }
}
