using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class RecordingDevicesTests
{
    private static readonly RecordingDevice BuiltIn = new("{0.0.1.00000000}.{built-in}", "Microphone Array");
    private static readonly RecordingDevice Headset = new("{0.0.1.00000000}.{headset}", "Headset (WH-1000XM4)");

    /// <summary>
    /// The rule the whole feature turns on: a device that has been unplugged since it was
    /// chosen must not be the reason a recording comes out silent. macshot resolves the
    /// remembered UID and falls through to the default when it does not
    /// (<c>RecordingEngine.swift:278</c>).
    /// </summary>
    [TestMethod]
    public void Chosen_FallsBackToTheDefaultWhenTheRememberedDeviceIsGone()
    {
        Assert.IsNull(RecordingDevices.Chosen(Headset.Id, [BuiltIn]));
    }

    /// <summary>
    /// The other half of the same rule: while the chosen device is still there it is used,
    /// however the system feels about which one is the default. Someone who picked their
    /// headset picked it against Windows' own answer.
    /// </summary>
    [TestMethod]
    public void Chosen_KeepsTheRememberedDeviceWhileItIsStillPresent()
    {
        Assert.AreEqual(Headset.Id, RecordingDevices.Chosen(Headset.Id, [BuiltIn, Headset]));
    }

    /// <summary>
    /// Never having opened the menu means following the system, not the first device that
    /// happens to enumerate — which is why "no choice" is empty rather than an id.
    /// </summary>
    [TestMethod]
    public void Chosen_AsksForNothingInParticularWhenNoChoiceWasEverMade()
    {
        Assert.IsNull(RecordingDevices.Chosen(string.Empty, [BuiltIn, Headset]));
        Assert.IsNull(RecordingDevices.Chosen(null, [BuiltIn, Headset]));
    }

    /// <summary>
    /// The tick has to say what a recording started now would actually use, so with no
    /// choice made it sits on the system default rather than on nothing — otherwise the
    /// menu reads as though the switch were off.
    /// </summary>
    [TestMethod]
    public void Menu_TicksTheSystemDefaultWhenNothingWasEverChosen()
    {
        var rows = RecordingDevices.Menu([BuiltIn, Headset], string.Empty, Headset.Id, on: true);

        Assert.AreEqual(2, rows.Count);
        Assert.IsFalse(rows[0].IsChosen);
        Assert.IsTrue(rows[1].IsChosen);
    }

    /// <summary>
    /// And with a device gone the tick moves to the default with the recording, rather
    /// than staying on a name that is no longer in the list — a menu that ticked nothing
    /// would leave the user unable to tell what they are about to record.
    /// </summary>
    [TestMethod]
    public void Menu_TicksTheDeviceTheFallbackWouldOpen()
    {
        var rows = RecordingDevices.Menu([BuiltIn], Headset.Id, BuiltIn.Id, on: true);

        Assert.AreEqual(1, rows.Count);
        Assert.IsTrue(rows[0].IsChosen);
    }

    /// <summary>
    /// While the switch is off nothing is being recorded, and macshot puts the tick on the
    /// None row instead (<c>OverlayView.swift:7622</c>). A device still ticked under an
    /// off switch would claim a recording would carry it.
    /// </summary>
    [TestMethod]
    public void Menu_TicksNoDeviceWhileTheSwitchIsOff()
    {
        var rows = RecordingDevices.Menu([BuiltIn, Headset], Headset.Id, BuiltIn.Id, on: false);

        Assert.IsTrue(rows.All(row => !row.IsChosen));
    }

    /// <summary>
    /// The order is the machine's, which is the order the system prefers its devices in.
    /// Sorting by name would put the built-in microphone above the headset that was
    /// plugged in to be used.
    /// </summary>
    [TestMethod]
    public void Menu_KeepsTheOrderTheMachineListedTheDevicesIn()
    {
        var rows = RecordingDevices.Menu([Headset, BuiltIn], string.Empty, null, on: true);

        Assert.AreEqual(Headset.Id, rows[0].Device.Id);
        Assert.AreEqual(BuiltIn.Id, rows[1].Device.Id);
    }

    /// <summary>
    /// A row that can be picked and not remembered does nothing, so a device the platform
    /// could not name an id for is not offered at all.
    /// </summary>
    [TestMethod]
    public void Menu_LeavesOutADeviceWithNoIdToRememberItBy()
    {
        var rows = RecordingDevices.Menu([new RecordingDevice(string.Empty, "Nameless"), BuiltIn], null, null, on: true);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(BuiltIn.Id, rows[0].Device.Id);
    }

    /// <summary>
    /// A machine with no microphone at all is an ordinary machine: the menu is then just
    /// its None row, and nothing here may throw on the way to finding that out.
    /// </summary>
    [TestMethod]
    public void Menu_IsEmptyWhenTheMachineHasNoDeviceOfThatKind()
    {
        Assert.AreEqual(0, RecordingDevices.Menu([], Headset.Id, null, on: true).Count);
    }
}
