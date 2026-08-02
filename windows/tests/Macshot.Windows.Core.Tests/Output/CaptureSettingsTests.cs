using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
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

    /// <summary>
    /// Both of these decide what the overlay does before the user has touched anything,
    /// and both are read from a settings file that predates them — a file written by an
    /// older build has neither key, so the defaults here are what those users get.
    /// Window snap off would change what a click means, and instructions hidden would
    /// leave a first-time user looking at a dimmed screen with nothing telling them what
    /// to do with it.
    /// </summary>
    [TestMethod]
    public void ByDefaultWindowsAreOfferedAndTheInstructionsAreShown()
    {
        Assert.IsTrue(CaptureSettings.Default.WindowSnapEnabled);
        Assert.IsFalse(CaptureSettings.Default.HideCaptureInstructions);
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
        var style = new AnnotationStyle(new AnnotationColor(255, 0, 0, 128), 7, LineStyle.Dotted)
        {
            NumberFormat = NumberFormat.AlphaLower,
            MeasureInPoints = true,
            LoupeMagnification = 4.5,
        };

        var restored = CaptureSettings.Default.WithAnnotationStyle(style).Normalized().ToAnnotationStyle();

        Assert.AreEqual(style.Color, restored.Color);
        Assert.AreEqual(style.StrokeWidth, restored.StrokeWidth);
        Assert.AreEqual(style.LineStyle, restored.LineStyle);

        // The tool settings the options row remembers between captures. Left off
        // WithAnnotationStyle they would appear to work for one capture and reset on the
        // next, which reads as the row forgetting at random.
        Assert.AreEqual(style.NumberFormat, restored.NumberFormat);
        Assert.AreEqual(style.MeasureInPoints, restored.MeasureInPoints);
        Assert.AreEqual(style.LoupeMagnification, restored.LoupeMagnification);
    }

    /// <summary>
    /// The settings file is hand-editable and can be stale after an upgrade. A loupe left
    /// at no magnification would be a circle drawn on the capture for no reason, and a
    /// sequence starting at zero would put an empty badge on it.
    /// </summary>
    [TestMethod]
    public void Normalized_RepairsTheOptionsRowsOwnSettings()
    {
        var settings = (CaptureSettings.Default with
        {
            LoupeMagnification = 0,
            NumberStartAt = 0,
            NumberFormat = (NumberFormat)42,
        }).Normalized();

        Assert.AreEqual(AnnotationStyle.DefaultLoupeMagnification, settings.LoupeMagnification);
        Assert.AreEqual(1, settings.NumberStartAt);
        Assert.AreEqual(NumberFormat.Decimal, settings.NumberFormat);
    }

    [TestMethod]
    public void HidingAToolTakesItOffTheToolbarAndLeavesTheRest()
    {
        var settings = (CaptureSettings.Default with { HiddenTools = ["Loupe", "Measure"] }).Normalized();

        var tools = settings.EnabledTools();

        Assert.IsFalse(tools.Contains(AnnotationTool.Loupe));
        Assert.IsFalse(tools.Contains(AnnotationTool.Measure));
        Assert.IsTrue(tools.Contains(AnnotationTool.Arrow));
    }

    [TestMethod]
    public void ToolsAreStoredByWhatIsHiddenSoANewOneArrivesSwitchedOn()
    {
        // A list of what is wanted, written before a tool existed, cannot contain it —
        // so every existing user would have the next version's tool hidden from them.
        Assert.AreEqual(0, CaptureSettings.Default.HiddenTools.Count);
        Assert.AreEqual(ToolbarActions.ToolOrder.Count, CaptureSettings.Default.EnabledTools().Count);
    }

    [TestMethod]
    public void AFileThatHidesEveryToolIsTreatedAsHidingNone()
    {
        // A toolbar with no tools on it is not a preference, it is a broken window.
        var settings = (CaptureSettings.Default with
        {
            HiddenTools = [.. ToolbarActions.ToolOrder.Select(tool => tool.ToString())],
        }).Normalized();

        Assert.AreEqual(0, settings.HiddenTools.Count);
    }

    [TestMethod]
    public void AToolNameNothingKnowsIsDropped()
    {
        var settings = (CaptureSettings.Default with { HiddenTools = ["Loupe", "Telepathy", "loupe"] }).Normalized();

        CollectionAssert.AreEqual(new[] { "Loupe" }, settings.HiddenTools.ToArray());
    }

    [TestMethod]
    public void ASavedColourThatCannotBeReadEmptiesItsSlotRatherThanShiftingTheRest()
    {
        // Dropping the bad entry would move every later colour into a different square,
        // so the one the user reaches for by position would no longer be there.
        var settings = (CaptureSettings.Default with
        {
            CustomColors = ["#FFFF0000", "not a colour", "#FF00FF00"],
        }).Normalized();

        Assert.AreEqual(3, settings.CustomColors.Count);
        Assert.AreEqual(string.Empty, settings.CustomColors[1]);
        Assert.AreEqual("#FF00FF00", settings.CustomColors[2]);
    }

    [TestMethod]
    public void AHandEditedFileCannotGrowTheColourPicker()
    {
        var settings = (CaptureSettings.Default with
        {
            CustomColors = [.. Enumerable.Repeat("#FFFF0000", CaptureSettings.CustomColorSlots + 4)],
        }).Normalized();

        Assert.AreEqual(CaptureSettings.CustomColorSlots, settings.CustomColors.Count);
    }

    [TestMethod]
    public void AnUnreadableToolbarColourComesBackAsTheDefault()
    {
        var settings = (CaptureSettings.Default with { ToolbarAccentColor = "rhubarb" }).Normalized();

        Assert.AreEqual(ToolbarColors.DefaultAccent, settings.ToToolbarColors().Accent);
    }

    [TestMethod]
    public void HoverAndPressAreWorkedOutFromTheColoursThatWereChosen()
    {
        // Three colours, not thirty: a palette where they can disagree is one somebody can
        // make unreadable, and the toolbar is over a screenshot where that means invisible.
        var colors = new ToolbarColors(
            new AnnotationColor(0, 0, 0),
            new AnnotationColor(10, 20, 30),
            new AnnotationColor(200, 210, 220));

        Assert.AreEqual(new AnnotationColor(200, 210, 220, 31), colors.Hover);
        Assert.AreEqual(new AnnotationColor(10, 20, 30, 153), colors.Pressed);
    }

    [TestMethod]
    public void Default_DeliversToTheClipboardAndDisk()
    {
        Assert.IsTrue(CaptureSettings.Default.CopyToClipboard);
        Assert.IsTrue(CaptureSettings.Default.AutoSave);
        Assert.AreEqual(CaptureImageFormat.Png, CaptureSettings.Default.Format);
    }

    [TestMethod]
    public void Normalized_PullsTheRecordingRatesIntoWhatThePlansCanEncode()
    {
        var settings = (CaptureSettings.Default with
        {
            RecordingFrameRate = 500,
            GifFrameRate = 0,
        }).Normalized();

        // The file is hand-edited, so these arrive from a person rather than from a
        // control that could not offer 500 in the first place.
        Assert.AreEqual(RecordingPlan.MaxFrameRate, settings.RecordingFrameRate);
        Assert.AreEqual(GifRecordingPlan.MinFrameRate, settings.GifFrameRate);
    }

    [TestMethod]
    public void Default_RecordsAtTheRateThePlanRecordsAt()
    {
        // The setting exists so that the rate can be changed, not so that there are two
        // places to disagree about what it is when nobody has.
        Assert.AreEqual(RecordingPlan.DefaultFrameRate, CaptureSettings.Default.RecordingFrameRate);
        Assert.AreEqual(GifRecordingPlan.DefaultFrameRate, CaptureSettings.Default.GifFrameRate);
    }

    [TestMethod]
    public void Default_RecordsNoSound()
    {
        // Both off, as macshot has them. A recording that carries the room or whatever
        // was playing without having been asked is a surprise in a file that gets shared,
        // and the surprise is only found once it has been sent.
        Assert.IsFalse(CaptureSettings.Default.RecordSystemAudio);
        Assert.IsFalse(CaptureSettings.Default.RecordMicAudio);
    }

    [TestMethod]
    public void EffectiveHistorySize_LetsUnlimitedOverrideACountOfNone()
    {
        // Someone who asked to keep everything has not asked for history to be off, and
        // reading it the other way would turn the switch into one that deletes.
        var unlimited = CaptureSettings.Default with { HistorySize = 0, HistoryUnlimited = true };

        Assert.AreEqual(int.MaxValue, unlimited.EffectiveHistorySize);
        Assert.AreEqual(0, (unlimited with { HistoryUnlimited = false }).EffectiveHistorySize);
    }
    [TestMethod]
    public void Default_LooksForUpdatesButNotForBetas()
    {
        // macshot's defaults: SUEnableAutomaticChecks on, betaUpdatesEnabled off. A tool
        // that never mentions an update stays on the version with the bug in it; a beta
        // is something a user opts into.
        Assert.IsTrue(CaptureSettings.Default.AutomaticUpdateChecks);
        Assert.IsFalse(CaptureSettings.Default.BetaUpdates);
    }

    [TestMethod]
    public void UpdateChoicesTravelToAnotherMachine()
    {
        // They are preferences, not machine state, so an export carries them.
        Assert.IsTrue(SettingsPortability.IsPortable("automaticUpdateChecks"));
        Assert.IsTrue(SettingsPortability.IsPortable("betaUpdates"));
    }

    [TestMethod]
    public void Normalized_DropsAShortcutForSomethingThatNoLongerExists()
    {
        // A binding nobody can reach is worse than none: the settings window would draw a
        // row for it, and the key would appear to be taken.
        var settings = (CaptureSettings.Default with
        {
            ToolShortcuts = new Dictionary<string, string> { ["telepathy"] = "k", ["pencil"] = "k" },
        }).Normalized();

        Assert.IsFalse(settings.ToolShortcuts.ContainsKey("telepathy"));
        Assert.AreEqual("k", settings.ToolShortcuts["pencil"]);
    }

    [TestMethod]
    public void Normalized_KeepsAShortcutTheUserTookOff()
    {
        // The one entry that must survive being tidied: empty means "not on any key", and
        // dropping it would hand the default straight back.
        var settings = (CaptureSettings.Default with
        {
            ToolShortcuts = new Dictionary<string, string> { ["pencil"] = "" },
        }).Normalized();

        Assert.AreEqual(string.Empty, settings.ToolShortcuts["pencil"]);
        Assert.AreEqual(
            ToolShortcuts.Unbound,
            ToolShortcuts.KeyFor(ToolShortcuts.All.First(s => s.Id == "pencil"), settings.ToolShortcuts));
    }

    [TestMethod]
    public void Normalized_TurnsAKeyNoPressCouldMatchIntoNoKeyAtAll()
    {
        // The settings file is hand-editable, and "Ctrl+P" in it would sit in the window
        // looking assigned while no keypress ever matched it.
        var settings = (CaptureSettings.Default with
        {
            ToolShortcuts = new Dictionary<string, string> { ["arrow"] = "Ctrl+P", ["line"] = "L" },
        }).Normalized();

        Assert.AreEqual(string.Empty, settings.ToolShortcuts["arrow"]);
        Assert.AreEqual("l", settings.ToolShortcuts["line"]);
    }

    [TestMethod]
    public void Normalized_ForgetsAHeldShapeThatIsNotAShape()
    {
        // A ratio of zero or below is not something a region can be held to, and letting
        // one through would make the next drag collapse to nothing.
        foreach (var value in new[] { 0, -1.5, double.NaN, double.PositiveInfinity })
        {
            var settings = (CaptureSettings.Default with { KeepAspectRatioValue = value }).Normalized();

            Assert.AreEqual(0, settings.KeepAspectRatioValue, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.AreEqual(
            16d / 9,
            (CaptureSettings.Default with { KeepAspectRatioValue = 16d / 9 }).Normalized().KeepAspectRatioValue);
    }

    [TestMethod]
    public void ShortcutsTravelToAnotherMachine()
    {
        // Which key picks the pencil is a preference about the person, not about the
        // machine, so it goes in the file they carry.
        Assert.IsTrue(SettingsPortability.IsPortable("toolShortcuts"));
        Assert.IsTrue(SettingsPortability.IsPortable("showShortcutsInTooltips"));
    }
}
