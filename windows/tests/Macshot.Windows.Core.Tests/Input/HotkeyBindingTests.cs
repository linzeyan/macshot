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
            HotkeyBinding.RecordScreen,
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
        Assert.AreEqual("Ctrl+Shift+X", HotkeyBinding.CaptureArea.ToString());
        Assert.AreEqual("Ctrl+Shift+F", HotkeyBinding.CaptureAllScreens.ToString());
        Assert.AreEqual("Ctrl+Shift+R", HotkeyBinding.RecordScreen.ToString());
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
            RecordScreenHotkey = "not a shortcut",
        }).Normalized();

        Assert.AreEqual(HotkeyBinding.CaptureArea.ToString(), normalized.CaptureAreaHotkey);
        Assert.AreEqual(HotkeyBinding.RecordScreen.ToString(), normalized.RecordScreenHotkey);
    }
}
