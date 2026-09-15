namespace Macshot.Windows.Core.Capture;

/// <summary>A device a recording can be refused, and which Windows page turns it back on.</summary>
public enum GuardedDevice
{
    Microphone,

    Camera,
}

/// <summary>Why a recording device could not be opened.</summary>
public enum DeviceAccess
{
    /// <summary>It opened.</summary>
    Opened,

    /// <summary>
    /// Windows refused it. This is the one worth interrupting the user for, because it is
    /// the only one they can do something about.
    /// </summary>
    Blocked,

    /// <summary>
    /// There is no such device. Nothing to turn on and nothing to say — a machine with no
    /// microphone is an ordinary machine, and macshot records without one.
    /// </summary>
    Missing,

    /// <summary>It is there, it is allowed, and it still would not open.</summary>
    Unusable,
}

/// <summary>
/// Reads Windows' answer to "may I open this device", and says where the user would turn
/// it back on.
/// </summary>
/// <remarks>
/// <para>
/// Windows has no way for an unpackaged desktop app to <em>ask</em> for the microphone or
/// the camera, and no way to read the setting either — unlike macOS, where
/// <c>AVCaptureDevice.authorizationStatus</c> answers before anything is opened and the
/// system puts the request to the user itself. Here the only way to find out is to open
/// the device and read the failure, which makes telling the three failures apart the whole
/// of the problem: a blocked device and an absent one are the same <c>null</c> otherwise,
/// and only one of them has a page to send anyone to.
/// </para>
/// <para>
/// In Core because it is a lookup table with a user-visible consequence and no window in
/// sight. Screen capture is deliberately not here: an unpackaged desktop app needs no
/// permission for it, which is the opposite of macOS and is why the Mac app carries a whole
/// onboarding window that this port does not.
/// </para>
/// </remarks>
public static class DevicePrivacy
{
    /// <summary>
    /// <c>E_ACCESSDENIED</c>. What both a privacy setting turned off for the whole machine
    /// and one turned off for macshot alone come back as — the two are indistinguishable
    /// from here, so the message names both and the settings page shows which it was.
    /// </summary>
    public const int AccessDenied = unchecked((int)0x80070005);

    /// <summary><c>E_NOTFOUND</c>, which is what an empty endpoint collection answers.</summary>
    public const int NotFound = unchecked((int)0x80070490);

    /// <summary>What <paramref name="code"/> says about the device it came from.</summary>
    public static DeviceAccess FromHResult(int code) => code switch
    {
        0 => DeviceAccess.Opened,
        AccessDenied => DeviceAccess.Blocked,
        NotFound => DeviceAccess.Missing,
        _ => DeviceAccess.Unusable,
    };

    /// <summary>
    /// The <c>ms-settings:</c> page carrying the switch for <paramref name="device"/>.
    /// </summary>
    /// <remarks>
    /// Both pages hold two switches — one for the whole machine and one per app, with
    /// desktop apps gathered under a single entry rather than listed — and the user may
    /// need either, so this opens the page rather than trying to name a switch.
    /// </remarks>
    public static string PageFor(GuardedDevice device) => device switch
    {
        GuardedDevice.Microphone => "ms-settings:privacy-microphone",
        GuardedDevice.Camera => "ms-settings:privacy-webcam",
        _ => throw new ArgumentOutOfRangeException(nameof(device), device, "No page for this device."),
    };
}
