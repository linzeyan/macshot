using System.Text.Json;
using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Core.Output;
using Macshot.Windows.Core.Recognition;

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
            LoupeSize = 200,
            StampSize = 96,
            DimOpacity = 0.8,
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
        Assert.AreEqual(style.LoupeSize, restored.LoupeSize);
        Assert.AreEqual(style.StampSize, restored.StampSize);
        Assert.AreEqual(style.DimOpacity, restored.DimOpacity);
    }

    /// <summary>
    /// Everything the text tool's row sets survives to the next capture.
    /// </summary>
    /// <remarks>
    /// A label is not restyled from scratch each time — someone who sets 28-point bold
    /// centred with a white line round the glyphs is captioning a series of screenshots,
    /// and having to set all five again on the second one is the whole feature failing.
    /// Any of these left out of the round trip would work for exactly one capture and reset
    /// on the next, which reads as the row forgetting at random rather than as a bug.
    /// </remarks>
    [TestMethod]
    public void TheLabelsWholeAppearance_RoundTripsThroughTheSettings()
    {
        var style = AnnotationStyle.Default with
        {
            FontSize = 28,
            FontFamily = "Cascadia Code",
            Bold = true,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            TextAlignment = LabelAlignment.Centre,
            TextBackground = new AnnotationColor(1, 2, 3, 200),
            TextOutline = new AnnotationColor(4, 5, 6, 210),
            TextGlyphStroke = new AnnotationColor(255, 255, 255),
        };

        var restored = CaptureSettings.Default.WithAnnotationStyle(style).Normalized().ToAnnotationStyle();

        Assert.AreEqual(style.FontSize, restored.FontSize);
        Assert.AreEqual(style.FontFamily, restored.FontFamily);
        Assert.AreEqual(style.Bold, restored.Bold);
        Assert.AreEqual(style.Italic, restored.Italic);
        Assert.AreEqual(style.Underline, restored.Underline);
        Assert.AreEqual(style.Strikethrough, restored.Strikethrough);
        Assert.AreEqual(style.TextAlignment, restored.TextAlignment);
        Assert.AreEqual(style.TextBackground, restored.TextBackground);
        Assert.AreEqual(style.TextOutline, restored.TextOutline);
        Assert.AreEqual(style.TextGlyphStroke, restored.TextGlyphStroke);
    }

    /// <summary>
    /// The four style switches are four switches, not one choice between four.
    /// </summary>
    /// <remarks>
    /// This is the difference between the row macshot has and the two-way weight picker the
    /// port used to carry: bold and underlined at once is an ordinary thing to want from a
    /// heading typed onto a screenshot, and a setting that could only remember one of them
    /// would silently drop the other on the way to the next capture.
    /// </remarks>
    [TestMethod]
    public void TheLabelsStyleSwitches_AreRememberedIndependently()
    {
        var style = AnnotationStyle.Default with { Bold = true, Strikethrough = true };

        var restored = CaptureSettings.Default.WithAnnotationStyle(style).Normalized().ToAnnotationStyle();

        Assert.IsTrue(restored.Bold);
        Assert.IsTrue(restored.Strikethrough);
        Assert.IsFalse(restored.Italic);
        Assert.IsFalse(restored.Underline);
    }

    /// <summary>
    /// A colour the file cannot express means the line round the glyphs is off, not that it
    /// is some other colour.
    /// </summary>
    /// <remarks>
    /// Off is a state the user chose, and it is the state every file written before the
    /// control existed is in. Defaulting an unreadable colour to white instead would put an
    /// outline round every label typed after an upgrade — a change to the drawing nobody
    /// asked for, which is worse than losing a setting they did.
    /// </remarks>
    [TestMethod]
    public void Normalized_LeavesTheGlyphOutlineOffWhenItsColourCannotBeRead()
    {
        var settings = (CaptureSettings.Default with
        {
            AnnotationTextGlyphStroke = "not a colour",
            AnnotationTextAlignment = (LabelAlignment)42,
        }).Normalized();

        Assert.AreEqual(string.Empty, settings.AnnotationTextGlyphStroke);
        Assert.IsNull(settings.ToAnnotationStyle().TextGlyphStroke);

        // And an alignment the enum does not know lands on the edge typing already gives.
        Assert.AreEqual(LabelAlignment.Left, settings.AnnotationTextAlignment);
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
            LoupeSize = 0,
            StampSize = 0,
            NumberStartAt = 0,
            NumberFormat = (NumberFormat)42,

            // Every file written before the spotlight had a slider says zero, which is no
            // dim at all — the tool would open having apparently stopped working.
            DimOpacity = 0,
        }).Normalized();

        Assert.AreEqual(AnnotationStyle.DefaultLoupeMagnification, settings.LoupeMagnification);

        // A loupe of no width is not a small loupe: it is a click that puts nothing on the
        // capture, and the tool would look broken rather than badly configured.
        Assert.AreEqual(AnnotationStyle.DefaultLoupeSize, settings.LoupeSize);
        Assert.AreEqual(AnnotationStyle.DefaultStampSize, settings.StampSize);
        Assert.AreEqual(AnnotationStyle.DefaultDimOpacity, settings.DimOpacity);
        Assert.AreEqual(1, settings.NumberStartAt);
        Assert.AreEqual(NumberFormat.Decimal, settings.NumberFormat);
    }

    /// <summary>
    /// The kinds of secret are stored by what is switched off, so a new pattern redacts for
    /// everyone the day it ships.
    /// </summary>
    /// <remarks>
    /// The same reasoning as the hidden tools, with a sharper consequence: stored the other
    /// way round, a list written before a pattern existed could not name it, and every
    /// existing user would go on publishing that one kind of secret without ever being told
    /// the feature had learned to spot it.
    /// </remarks>
    [TestMethod]
    public void SwitchingOffAPiiKindLeavesTheRestAndANewOneArrivesOn()
    {
        Assert.AreEqual(0, CaptureSettings.Default.HiddenPiiKinds.Count);
        Assert.AreEqual(Enum.GetValues<PiiKind>().Length, CaptureSettings.Default.RedactedPiiKinds().Count);

        var settings = (CaptureSettings.Default with
        {
            // The second is a name from a build that had a pattern this one does not, and
            // must be dropped rather than written back for ever.
            HiddenPiiKinds = ["Phone", "Astrology"],
        }).Normalized();

        CollectionAssert.AreEqual(new[] { "Phone" }, settings.HiddenPiiKinds.ToArray());

        var wanted = settings.RedactedPiiKinds();
        Assert.IsFalse(wanted.Contains(PiiKind.Phone));
        Assert.IsTrue(wanted.Contains(PiiKind.Email));
    }

    /// <summary>
    /// Every one of them can be switched off at once, unlike the tools.
    /// </summary>
    /// <remarks>
    /// A toolbar with no tools is a broken window and is repaired; a redactor asked to find
    /// nothing is a coherent thing to want, and the button that then finds nothing says so.
    /// </remarks>
    [TestMethod]
    public void ASettingsFileThatSwitchesOffEveryPiiKindIsHonoured()
    {
        var settings = (CaptureSettings.Default with
        {
            HiddenPiiKinds = [.. Enum.GetValues<PiiKind>().Select(kind => kind.ToString())],
        }).Normalized();

        Assert.AreEqual(0, settings.RedactedPiiKinds().Count);
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

    /// <summary>
    /// The Adjust popover is where a whole capture's look is decided, and macshot keeps
    /// that decision beside the app rather than in the capture — a look chosen once is
    /// what the next capture starts in. Kept here so the round trip through the file
    /// cannot quietly drop one of the five.
    /// </summary>
    [TestMethod]
    public void ImageEffectsSurviveTheSettingsFile()
    {
        var chosen = new ImageEffectsOptions(ImageEffectPreset.Noir, 0.2, 1.4, 0.6, 1.1);

        var carried = CaptureSettings.Default.WithImageEffects(chosen).Normalized().ToImageEffectsOptions();

        Assert.AreEqual(chosen, carried);
    }

    /// <summary>
    /// Nothing downstream may assume the file is sane, and this is the one setting whose
    /// out-of-range value reaches every pixel: a contrast of forty is a capture of two
    /// colours, and the user would have no way of telling that the file was the cause.
    /// </summary>
    [TestMethod]
    public void Normalized_PullsAHandEditedAdjustmentBackIntoTheSlidersRange()
    {
        var settings = (CaptureSettings.Default with
        {
            EffectsPreset = (ImageEffectPreset)99,
            EffectsBrightness = 40,
            EffectsContrast = 40,
            EffectsSaturation = -3,
            EffectsSharpness = 9,
        }).Normalized();

        Assert.AreEqual(ImageEffectPreset.None, settings.EffectsPreset, "a look this build has not got");
        Assert.AreEqual(0.5, settings.EffectsBrightness);
        Assert.AreEqual(2, settings.EffectsContrast);
        Assert.AreEqual(0, settings.EffectsSaturation);
        Assert.AreEqual(2, settings.EffectsSharpness);
    }

    /// <summary>
    /// A settings file written before this was remembered has none of the five keys, and
    /// what those users get is the state the popover opens in — no look and every slider
    /// centred. Anything else would tint the first capture after an upgrade.
    /// </summary>
    [TestMethod]
    public void AFileWithNoAdjustmentAsksForNothing()
    {
        Assert.IsTrue(CaptureSettings.Default.ToImageEffectsOptions().IsIdentity);
    }

    /// <summary>
    /// The camera bubble's size is a number the user drags now, and the only numbers that
    /// can reach the file from outside the slider are a hand edit or an import. Either can
    /// name a bubble the settings window cannot show — a 4000-pixel one has no thumb
    /// position and no way back.
    /// </summary>
    [TestMethod]
    public void Normalized_HoldsTheCameraBubbleToTheSizesTheSliderCanShow()
    {
        Assert.AreEqual(
            WebcamInset.MaximumSide,
            (CaptureSettings.Default with { WebcamSizePoints = 4000 }).Normalized().WebcamSizePoints);

        Assert.AreEqual(
            WebcamInset.MinimumSide,
            (CaptureSettings.Default with { WebcamSizePoints = 0 }).Normalized().WebcamSizePoints);
    }

    /// <summary>
    /// The size used to be one of four names and is now a number, and the old name is why
    /// the key changed with it. A file written by any earlier build still says
    /// <c>"webcamSize": "Medium"</c> — a string where a number would now be read — and
    /// under the same key that is not a setting that fails, it is a parse that fails, and
    /// every other preference in the file goes down with it. Under the new key the old
    /// entry is one nothing reads, and what arrives is the default, which is the size
    /// Medium was.
    /// </summary>
    [TestMethod]
    public void ASettingsFileNamingOneOfTheOldWebcamSizesStillLoads()
    {
        var stored = JsonSerializer.Deserialize<CaptureSettings>(
            """{ "webcamSize": "ExtraLarge", "quality": 71 }""",
            CaptureSettingsJson.Options);

        Assert.IsNotNull(stored);
        Assert.AreEqual(71, stored.Quality, "the rest of the file has to survive the dead key");
        Assert.AreEqual(WebcamInset.DefaultSide, stored.WebcamSizePoints);
    }

    /// <summary>
    /// A caption's look is remembered across recordings, not merely within one editor
    /// window. Someone who captions every clip in the same face and colour sets it up once;
    /// dropping it at the end of the session would mean doing that work again tomorrow.
    /// </summary>
    [TestMethod]
    public void ACaptionsLookSurvivesTheSettingsFileAndDressesTheNextOnePlaced()
    {
        var styled = VideoTextSegment.Placed(1, 30) with
        {
            FontSize = 72,
            Bold = false,
            Italic = true,
            FontFamily = "Impact",
            TextColor = new AnnotationColor(255, 204, 0),
            Background = VideoTextBackground.Solid,
            BackgroundColor = new AnnotationColor(0, 0, 0, 128),
            OutlineEnabled = true,
            OutlineColor = new AnnotationColor(255, 0, 0),
            OutlineWidth = 5,
            Alignment = VideoTextAlignment.Right,
        };

        // Through the file, not just the record: the colours are stored as hex and a
        // remembered style that could not survive being written down is not remembered.
        var stored = JsonSerializer.Deserialize<CaptureSettings>(
            JsonSerializer.Serialize(
                CaptureSettings.Default.WithCaptionStyle(styled),
                CaptureSettingsJson.Options),
            CaptureSettingsJson.Options);

        Assert.IsNotNull(stored);
        var dressed = stored.Normalized().CaptionStyled(VideoTextSegment.Placed(12, 30));

        Assert.AreEqual(styled.FontSize, dressed.FontSize, 0.001);
        Assert.IsFalse(dressed.Bold);
        Assert.IsTrue(dressed.Italic);
        Assert.AreEqual("Impact", dressed.FontFamily);
        Assert.AreEqual(styled.TextColor, dressed.TextColor);
        Assert.AreEqual(VideoTextBackground.Solid, dressed.Background);
        Assert.AreEqual(styled.BackgroundColor, dressed.BackgroundColor);
        Assert.IsTrue(dressed.OutlineEnabled);
        Assert.AreEqual(styled.OutlineColor, dressed.OutlineColor);
        Assert.AreEqual(styled.OutlineWidth, dressed.OutlineWidth, 0.001);
        Assert.AreEqual(VideoTextAlignment.Right, dressed.Alignment);

        // And the placement is the new caption's own — the memory is of a look, not of a
        // caption.
        Assert.AreEqual(VideoTextSegment.DefaultText, dressed.Text);
        Assert.IsTrue(dressed.Start >= 10);
    }

    /// <summary>
    /// A hand-edited or upgraded file must not be able to hand the editor a caption it
    /// cannot draw. An unreadable colour defaults rather than switching anything off — a
    /// caption always has glyphs, so there is no reading of a broken colour that leaves
    /// them uncoloured.
    /// </summary>
    [TestMethod]
    public void Normalized_RepairsARememberedCaptionStyle()
    {
        var settings = (CaptureSettings.Default with
        {
            VideoCaptionFontSize = 4000,
            VideoCaptionOutlineWidth = double.NaN,
            VideoCaptionFontFamily = "   ",
            VideoCaptionTextColor = "not a colour",
            VideoCaptionBackground = (VideoTextBackground)42,
            VideoCaptionAlignment = (VideoTextAlignment)42,
        }).Normalized();

        Assert.AreEqual(VideoTextSegment.MaxFontSize, settings.VideoCaptionFontSize, 0.001);
        Assert.AreEqual(VideoTextSegment.DefaultOutlineWidth, settings.VideoCaptionOutlineWidth, 0.001);
        Assert.AreEqual(VideoTextSegment.SystemFontFamily, settings.VideoCaptionFontFamily);
        Assert.AreEqual(VideoTextSegment.DefaultTextColor.ToHex(), settings.VideoCaptionTextColor);
        Assert.AreEqual(VideoTextBackground.Rounded, settings.VideoCaptionBackground);
        Assert.AreEqual(VideoTextAlignment.Centre, settings.VideoCaptionAlignment);
    }

    /// <summary>
    /// A machine with no remembered caption style has to place macshot's own caption. The
    /// defaults are stated twice — once on the segment, once in the settings — and a
    /// disagreement would mean the first caption after an upgrade came up different from
    /// the first caption on a fresh install.
    /// </summary>
    [TestMethod]
    public void ACaptionPlacedWithNothingRememberedIsMacshotsOwn()
    {
        var placed = VideoTextSegment.Placed(5, 30);

        Assert.AreEqual(placed, CaptureSettings.Default.CaptionStyled(placed));
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
