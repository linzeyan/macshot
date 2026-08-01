using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

/// <summary>
/// What the keystroke pill says, which is the whole of what a viewer learns from it.
/// </summary>
[TestClass]
public sealed class KeystrokeNamesTests
{
    private const int VirtualC = 0x43;
    private const int VirtualEscape = 0x1B;
    private const int VirtualLeftControl = 0xA2;
    private const int VirtualF7 = 0x76;

    [TestMethod]
    public void AChordReadsTheWayWindowsWritesOne()
    {
        Assert.AreEqual(
            "Ctrl + Shift + C",
            KeystrokeNames.Describe(
                VirtualC,
                'c',
                KeystrokeModifiers.Control | KeystrokeModifiers.Shift));
    }

    /// <summary>
    /// The order is macshot's — control, alt, shift, then the platform key — whichever
    /// order the flags happen to arrive in.
    /// </summary>
    [TestMethod]
    public void TheModifiersComeOutInMacshotsOrder()
    {
        Assert.AreEqual(
            "Ctrl + Alt + Shift + Win",
            KeystrokeNames.DescribeModifiers(
                KeystrokeModifiers.Windows
                | KeystrokeModifiers.Shift
                | KeystrokeModifiers.Alt
                | KeystrokeModifiers.Control));
    }

    [TestMethod]
    public void AKeyWithANameOfItsOwnIgnoresWhatTheLayoutTypes()
    {
        Assert.AreEqual("Esc", KeystrokeNames.Describe(VirtualEscape, '\0', KeystrokeModifiers.None));
        Assert.AreEqual("F7", KeystrokeNames.Describe(VirtualF7, '\0', KeystrokeModifiers.None));
    }

    /// <summary>
    /// A modifier pressed on its own has no key to name, so the chord would be a dangling
    /// "Ctrl + " with nothing after it.
    /// </summary>
    [TestMethod]
    public void AModifierOnItsOwnDescribesNothing()
    {
        Assert.AreEqual(
            string.Empty,
            KeystrokeNames.Describe(VirtualLeftControl, '\0', KeystrokeModifiers.Control));
    }

    /// <summary>
    /// Caps Lock is a state rather than a key being held. In a chord it would describe a
    /// keystroke nobody pressed; on its own it is worth showing that it went on.
    /// </summary>
    [TestMethod]
    public void CapsLockCountsAloneAndNotInAChord()
    {
        Assert.AreEqual(
            "Ctrl + C",
            KeystrokeNames.Describe(
                VirtualC,
                'c',
                KeystrokeModifiers.Control | KeystrokeModifiers.CapsLock));

        Assert.AreEqual(
            "Caps Lock",
            KeystrokeNames.DescribeModifiers(KeystrokeModifiers.CapsLock));
    }

    /// <summary>
    /// The point of the shortcuts-only mode: a recording of someone typing prose should
    /// not be a transcript of the prose.
    /// </summary>
    [TestMethod]
    public void ShortcutsOnlyKeepsOrdinaryTypingOffTheScreen()
    {
        Assert.IsFalse(KeystrokeNames.WorthShowing(VirtualC, KeystrokeModifiers.None, showAll: false));
        Assert.IsFalse(
            KeystrokeNames.WorthShowing(VirtualC, KeystrokeModifiers.Shift, showAll: false),
            "shift is how a capital is typed, not what makes a shortcut");
        Assert.IsTrue(KeystrokeNames.WorthShowing(VirtualC, KeystrokeModifiers.Control, showAll: false));
    }

    [TestMethod]
    public void ShowingEverythingShowsEvenABareModifier()
    {
        Assert.IsTrue(KeystrokeNames.WorthShowing(VirtualLeftControl, KeystrokeModifiers.Control, showAll: true));
        Assert.IsTrue(KeystrokeNames.WorthShowing(VirtualC, KeystrokeModifiers.None, showAll: true));
    }
}
