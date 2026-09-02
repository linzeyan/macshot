using System.Text.Json;
using Macshot.Windows.Core.Input;
using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Input;

[TestClass]
public sealed class EditorCommandShortcutsTests
{
    private static readonly EditorCommandShortcut Undo =
        EditorCommandShortcuts.All.First(command => command.Command is EditorCommand.Undo);

    private static readonly EditorCommandShortcut Redo =
        EditorCommandShortcuts.All.First(command => command.Command is EditorCommand.Redo);

    private static readonly HotkeyBinding ControlZ = new(HotkeyModifiers.Control, 'Z');

    private static readonly HotkeyBinding ControlShiftZ =
        new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'Z');

    private static readonly HotkeyBinding ControlY = new(HotkeyModifiers.Control, 'Y');

    /// <summary>
    /// The chords macshot ships, with Control where it holds Command. Redo answering to
    /// both is the point: Windows programs are split between Ctrl+Shift+Z and Ctrl+Y, and
    /// shipping only one of them would be wrong for half of everyone.
    /// </summary>
    [TestMethod]
    public void All_ShipsUndoOnOneChordAndRedoOnTwo()
    {
        CollectionAssert.AreEqual(new[] { ControlZ }, Undo.Defaults.ToArray());
        CollectionAssert.AreEqual(new[] { ControlShiftZ, ControlY }, Redo.Defaults.ToArray());

        Assert.AreEqual("undo", Undo.Id, "the identifier a settings file from either product uses");
        Assert.AreEqual("redo", Redo.Id);
    }

    [TestMethod]
    public void Find_AnswersEveryChordACommandShipsWith()
    {
        Assert.AreEqual(EditorCommand.Undo, EditorCommandShortcuts.Find(ControlZ, null)?.Command);
        Assert.AreEqual(EditorCommand.Redo, EditorCommandShortcuts.Find(ControlShiftZ, null)?.Command);
        Assert.AreEqual(EditorCommand.Redo, EditorCommandShortcuts.Find(ControlY, null)?.Command);
    }

    /// <summary>
    /// The modifiers have to match exactly. Ctrl+Alt+Z belongs to whoever bound it, and
    /// Undo swallowing it would make that binding impossible to use.
    /// </summary>
    [TestMethod]
    public void Find_DoesNotAnswerAChordWithAnExtraModifier()
    {
        Assert.IsNull(EditorCommandShortcuts.Find(
            new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'Z'),
            null));
    }

    /// <summary>
    /// A bare letter belongs to the overlay's single-key tool shortcuts. If Z alone could
    /// reach Undo it would take the key away from whichever tool stands on it, and the
    /// user would have no way to see why that tool had stopped working.
    /// </summary>
    [TestMethod]
    public void Find_RefusesAChordWithNoModifier()
    {
        Assert.IsNull(EditorCommandShortcuts.Find(new HotkeyBinding(HotkeyModifiers.None, 'Z'), null));
    }

    /// <summary>
    /// A chord can only resolve to one command. Giving Ctrl+Y to Undo has to take it off
    /// Redo — including when Redo only held it as a default — or the same press would run
    /// whichever command happened to be looked at first.
    /// </summary>
    [TestMethod]
    public void Bind_TakesTheChordOffTheOtherCommand()
    {
        var chosen = EditorCommandShortcuts.Bind(null, Undo, ControlY);

        Assert.AreEqual(EditorCommand.Undo, EditorCommandShortcuts.Find(ControlY, chosen)?.Command);
        CollectionAssert.AreEqual(
            new[] { ControlShiftZ },
            EditorCommandShortcuts.ChordsFor(Redo, chosen).ToArray(),
            "Redo keeps the chord it was not asked for");
        Assert.IsNull(
            EditorCommandShortcuts.Find(ControlZ, chosen),
            "Undo holds the one chord it was given and nothing else");
    }

    /// <summary>
    /// Nothing matches a chord with no modifier, so storing one would leave the settings
    /// window showing an assignment that could never fire.
    /// </summary>
    [TestMethod]
    public void Bind_RefusesAChordWithNoModifier()
    {
        var chosen = EditorCommandShortcuts.Bind(null, Undo, new HotkeyBinding(HotkeyModifiers.None, 'K'));

        CollectionAssert.AreEqual(Undo.Defaults.ToArray(), EditorCommandShortcuts.ChordsFor(Undo, chosen).ToArray());
    }

    /// <summary>
    /// The distinction the two buttons on each row rest on: turning a command off has to
    /// survive, while putting it back has to let a later version's defaults through.
    /// </summary>
    [TestMethod]
    public void Disable_IsNotTheSameAsNeverHavingChosen()
    {
        var off = EditorCommandShortcuts.Disable(null, Undo);

        Assert.AreEqual(0, EditorCommandShortcuts.ChordsFor(Undo, off).Count);
        Assert.IsNull(EditorCommandShortcuts.Find(ControlZ, off));

        var back = EditorCommandShortcuts.Reset(off, Undo);

        CollectionAssert.AreEqual(Undo.Defaults.ToArray(), EditorCommandShortcuts.ChordsFor(Undo, back).ToArray());
        Assert.AreEqual(EditorCommand.Undo, EditorCommandShortcuts.Find(ControlZ, back)?.Command);
    }

    /// <summary>
    /// Only the differences are written down, so a later version changing a default
    /// reaches everyone who never touched that row — while an entry that is present and
    /// empty still says the user turned the command off.
    /// </summary>
    [TestMethod]
    public void Chosen_WritesDownOnlyWhatIsNotTheDefault()
    {
        var unchanged = EditorCommandShortcuts.Chosen(new Dictionary<string, string>
        {
            [Undo.Id] = "Ctrl+Z",
            [Redo.Id] = "Ctrl+Shift+Z / Ctrl+Y",
        });

        Assert.AreEqual(0, unchanged.Count);

        var off = EditorCommandShortcuts.Chosen(EditorCommandShortcuts.Disable(null, Redo));

        Assert.AreEqual(1, off.Count);
        Assert.AreEqual(string.Empty, off[Redo.Id]);
    }

    /// <summary>
    /// The whole feature is worthless if the choice does not outlive the app, and the
    /// settings file is the only place it lives.
    /// </summary>
    [TestMethod]
    public void EditorCommandShortcuts_SurviveTheSettingsFile()
    {
        var settings = CaptureSettings.Default with
        {
            EditorCommandShortcuts = EditorCommandShortcuts.Chosen(
                EditorCommandShortcuts.Disable(
                    EditorCommandShortcuts.Bind(null, Undo, ControlY),
                    Redo)),
        };

        var stored = JsonSerializer.Deserialize<CaptureSettings>(
            JsonSerializer.Serialize(settings, CaptureSettingsJson.Options),
            CaptureSettingsJson.Options)?.Normalized();

        Assert.IsNotNull(stored);
        Assert.AreEqual(
            EditorCommand.Undo,
            EditorCommandShortcuts.Find(ControlY, stored.EditorCommandShortcuts)?.Command);
        Assert.AreEqual(0, EditorCommandShortcuts.ChordsFor(Redo, stored.EditorCommandShortcuts).Count);
    }

    /// <summary>
    /// A hand-edited file with one mistyped chord in it must not leave the user with no
    /// way to undo. Blank means off; unreadable means damaged, and the defaults come back.
    /// </summary>
    [TestMethod]
    public void Normalized_HandsTheDefaultsBackForAnUnreadableChord()
    {
        var stored = (CaptureSettings.Default with
        {
            EditorCommandShortcuts = new Dictionary<string, string>
            {
                [Undo.Id] = "Ctrl+Nonsense",
                [Redo.Id] = string.Empty,
                ["something else entirely"] = "Ctrl+K",
            },
        }).Normalized();

        CollectionAssert.AreEqual(
            Undo.Defaults.ToArray(),
            EditorCommandShortcuts.ChordsFor(Undo, stored.EditorCommandShortcuts).ToArray());
        Assert.AreEqual(0, EditorCommandShortcuts.ChordsFor(Redo, stored.EditorCommandShortcuts).Count);
        Assert.IsFalse(stored.EditorCommandShortcuts.ContainsKey("something else entirely"));
    }
}
