using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class DevicePrivacyTests
{
    [TestMethod]
    public void FromHResult_TellsARefusalApartFromAMachineWithNoSuchDevice()
    {
        // The whole point of reading the code at all. Both came back as a null endpoint
        // before, so a recording made silent by a privacy switch and one made silent by a
        // machine with no microphone were the same event — and only the first has anything
        // the user can do about it.
        Assert.AreEqual(DeviceAccess.Blocked, DevicePrivacy.FromHResult(DevicePrivacy.AccessDenied));
        Assert.AreEqual(DeviceAccess.Missing, DevicePrivacy.FromHResult(DevicePrivacy.NotFound));
    }

    [TestMethod]
    public void FromHResult_CallsAnythingElseUnusableRatherThanBlocked()
    {
        // Guessing the other way would send someone to a settings page over a format the
        // endpoint would not take, and leave them looking for a switch that is already on.
        Assert.AreEqual(DeviceAccess.Opened, DevicePrivacy.FromHResult(0));
        Assert.AreEqual(DeviceAccess.Unusable, DevicePrivacy.FromHResult(unchecked((int)0x88890008)));
    }

    [TestMethod]
    public void PageFor_NamesThePageEachDevicesSwitchIsOn()
    {
        // These are the strings the shell resolves; a typo opens Settings on its home page
        // and the user is left to find it, which is the thing this was built to avoid.
        Assert.AreEqual("ms-settings:privacy-microphone", DevicePrivacy.PageFor(GuardedDevice.Microphone));
        Assert.AreEqual("ms-settings:privacy-webcam", DevicePrivacy.PageFor(GuardedDevice.Camera));
    }
}
