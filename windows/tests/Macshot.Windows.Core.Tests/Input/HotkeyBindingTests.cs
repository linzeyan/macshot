using Macshot.Windows.Core.Input;
using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Input;

[TestClass]
public sealed class HotkeyBindingTests
{
    [TestMethod]
    public void ToString_WritesTheFormTheSettingsFileHolds()
    {
        Assert.AreEqual("Ctrl+Shift+X", HotkeyBinding.CaptureArea.ToString());
    }

    [TestMethod]
    public void TryParse_ReadsBackWhatToStringWrote()
    {
        foreach (var original in new[]
        {
            HotkeyBinding.CaptureArea,
            HotkeyBinding.CaptureAllScreens,
            HotkeyBinding.RecordArea,
            new HotkeyBinding(HotkeyModifiers.Alt | HotkeyModifiers.Windows, 0x7B),
        })
        {
            Assert.IsTrue(HotkeyBinding.TryParse(original.ToString(), out var parsed), original.ToString());
            Assert.AreEqual(original, parsed);
        }
    }

    [TestMethod]
    public void TryParse_TakesTheModifiersInAnyOrder()
    {
        Assert.IsTrue(HotkeyBinding.TryParse("Shift + Ctrl + X", out var parsed));

        Assert.AreEqual(HotkeyBinding.CaptureArea, parsed);
    }

    [TestMethod]
    public void TryParse_TakesTheNamesPeopleActuallyType()
    {
        Assert.IsTrue(HotkeyBinding.TryParse("control+shift+x", out var spelledOut));
        Assert.IsTrue(HotkeyBinding.TryParse("CMD+F1", out var fromAMacHabit));

        Assert.AreEqual(HotkeyBinding.CaptureArea, spelledOut);
        Assert.AreEqual(new HotkeyBinding(HotkeyModifiers.Windows, 0x70), fromAMacHabit);
    }

    [TestMethod]
    public void TryParse_ReadsTheKeysWithNoPrintableName()
    {
        Assert.IsTrue(HotkeyBinding.TryParse("Ctrl+PrintScreen", out var parsed));

        Assert.AreEqual(0x2Cu, parsed.Key);
        Assert.AreEqual("Ctrl+PrintScreen", parsed.ToString());
    }

    [TestMethod]
    public void TryParse_RefusesABareKey()
    {
        // A global hotkey with no modifier swallows that key everywhere on the
        // machine — including in the box the user would have to type it into to
        // change it back.
        Assert.IsFalse(HotkeyBinding.TryParse("X", out _));
    }

    [TestMethod]
    public void TryParse_RefusesModifiersWithNothingToPress()
    {
        Assert.IsFalse(HotkeyBinding.TryParse("Ctrl+Shift", out _));
    }

    [TestMethod]
    public void TryParse_RefusesTwoKeys()
    {
        Assert.IsFalse(HotkeyBinding.TryParse("Ctrl+X+Y", out _));
    }

    [TestMethod]
    public void TryParse_RefusesWhatIsNotAShortcut()
    {
        Assert.IsFalse(HotkeyBinding.TryParse("Ctrl+Banana", out _));
        Assert.IsFalse(HotkeyBinding.TryParse("   ", out _));
        Assert.IsFalse(HotkeyBinding.TryParse(null, out _));
    }

    [TestMethod]
    public void TryParse_IsCaseInsensitiveAboutTheKeyItself()
    {
        Assert.IsTrue(HotkeyBinding.TryParse("Ctrl+Shift+x", out var lower));

        Assert.AreEqual('X', (char)lower.Key);
    }

    [TestMethod]
    public void ParseOrDefault_KeepsTheUserAbleToCaptureWhenTheFileIsWrong()
    {
        var resolved = HotkeyBinding.ParseOrDefault("nonsense", HotkeyBinding.CaptureArea);

        Assert.AreEqual(HotkeyBinding.CaptureArea, resolved);
    }

    [TestMethod]
    public void Modifiers_CarryTheValuesRegisterHotKeyExpects()
    {
        // These reach RegisterHotKey unchanged, so they are the API's own numbers
        // rather than an enum of ours that would need translating.
        Assert.AreEqual(0x0001, (int)HotkeyModifiers.Alt);
        Assert.AreEqual(0x0002, (int)HotkeyModifiers.Control);
        Assert.AreEqual(0x0004, (int)HotkeyModifiers.Shift);
        Assert.AreEqual(0x0008, (int)HotkeyModifiers.Windows);
    }

    [TestMethod]
    public void Defaults_AreTheShortcutsTheAppAlreadyRegisters()
    {
        // macshot's own keys, with Control where macOS holds Command
        // (HotkeyManager.swift:81).
        Assert.AreEqual("Ctrl+Shift+X", HotkeyBinding.CaptureArea.ToString());
        Assert.AreEqual("Ctrl+Shift+F", HotkeyBinding.CaptureAllScreens.ToString());
        Assert.AreEqual("Ctrl+Shift+R", HotkeyBinding.RecordArea.ToString());
        Assert.AreEqual("Ctrl+Shift+H", HotkeyBinding.History.ToString());
        Assert.AreEqual("Ctrl+Shift+T", HotkeyBinding.CaptureText.ToString());
        Assert.AreEqual("Ctrl+Shift+S", HotkeyBinding.QuickCapture.ToString());
    }

    [TestMethod]
    public void Settings_KeepAShortcutTheUserChose()
    {
        var normalized = (CaptureSettings.Default with { CaptureAreaHotkey = " alt + f9 " }).Normalized();

        Assert.AreEqual("Alt+F9", normalized.CaptureAreaHotkey);
        Assert.AreEqual(new HotkeyBinding(HotkeyModifiers.Alt, 0x78), normalized.CaptureAreaBinding);
    }

    [TestMethod]
    public void Settings_ReplaceAShortcutThatCannotBeRegistered()
    {
        // A bare key is the dangerous one: it would be taken from every program on
        // the machine, including the one the user would need in order to change it
        // back. Normalizing has to refuse it rather than pass it through.
        var normalized = (CaptureSettings.Default with
        {
            CaptureAreaHotkey = "Q",
            RecordAreaHotkey = "not a shortcut",
        }).Normalized();

        Assert.AreEqual(HotkeyBinding.CaptureArea.ToString(), normalized.CaptureAreaHotkey);
        Assert.AreEqual(HotkeyBinding.RecordArea.ToString(), normalized.RecordAreaHotkey);
    }

    [TestMethod]
    public void Settings_LeaveHalfTheShortcutsUnbound()
    {
        // macshot ships these six bound to nothing (HotkeyManager.swift:81). Guessing a
        // key for them is worse than leaving them off: a global shortcut nobody asked
        // for takes that combination away from every other program on the machine.
        var settings = CaptureSettings.Default.Normalized();

        Assert.AreEqual(string.Empty, settings.RecordScreenHotkey);
        Assert.AreEqual(string.Empty, settings.ScrollCaptureHotkey);
        Assert.AreEqual(string.Empty, settings.OpenFromClipboardHotkey);
        Assert.AreEqual(string.Empty, settings.CaptureLastAreaHotkey);
        Assert.AreEqual(string.Empty, settings.PinFromClipboardHotkey);
        Assert.AreEqual(string.Empty, settings.ClearHistoryHotkey);

        Assert.IsNull(settings.RecordScreenBinding);
    }

    [TestMethod]
    public void Settings_LetAShortcutBeTakenOff()
    {
        // Blank has to survive normalizing. Falling back to the default here would hand
        // back the shortcut the user just cleared, and it could never be cleared at all.
        var normalized = (CaptureSettings.Default with { CaptureAreaHotkey = string.Empty }).Normalized();

        Assert.AreEqual(string.Empty, normalized.CaptureAreaHotkey);
        Assert.IsNull(normalized.CaptureAreaBinding);
    }

    [TestMethod]
    public void Settings_DropTheSecondClaimOnOneCombination()
    {
        // The recording shortcut was once a single entry named for the screen and bound
        // to Ctrl+Shift+R. macshot gives that to recording an area, so a file written
        // before the two were told apart asks for it twice — and Windows would give it
        // to one and refuse the other, naming a shortcut the user never typed.
        var normalized = (CaptureSettings.Default with
        {
            RecordScreenHotkey = "Ctrl+Shift+R",
        }).Normalized();

        Assert.AreEqual("Ctrl+Shift+R", normalized.RecordAreaHotkey);
        Assert.AreEqual(string.Empty, normalized.RecordScreenHotkey);
    }

    [TestMethod]
    public void PunctuationKeys_HaveNamesTheyCanBeWrittenWith()
    {
        // A shortcut recorder reports a virtual key code, and a code with no name cannot
        // be stored. VK_OEM_1 is the semicolon on a US layout, which is the only thing a
        // layout-independent code can be named after.
        Assert.IsTrue(HotkeyBinding.TryParse("Ctrl+Shift+;", out var parsed));
        Assert.AreEqual(0xBAu, parsed.Key);
        Assert.AreEqual("Ctrl+Shift+;", parsed.ToString());
    }

    [TestMethod]
    public void CanBeStored_IsTrueForAKeyWithAName()
    {
        Assert.IsTrue(new HotkeyBinding(HotkeyModifiers.Control, 0xBB).CanBeStored);
    }

    [TestMethod]
    public void CanBeStored_IsFalseForAKeyWithNoName()
    {
        // 0xE2 is the extra key on an ISO keyboard. Windows reports it, macshot has no
        // name for it, and the recorder has to refuse it at the press rather than write
        // down something that reads back as a different shortcut.
        Assert.IsFalse(new HotkeyBinding(HotkeyModifiers.Control, 0xE2).CanBeStored);
    }

    [TestMethod]
    public void CanBeStored_IsFalseForAShortcutWithNoModifier()
    {
        Assert.IsFalse(new HotkeyBinding(HotkeyModifiers.None, 'X').CanBeStored);
    }
}
