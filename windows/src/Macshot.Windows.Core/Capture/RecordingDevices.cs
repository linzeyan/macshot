namespace Macshot.Windows.Core.Capture;

/// <summary>
/// One microphone or camera the machine is offering, as its menu row shows it.
/// </summary>
/// <param name="Id">
/// What the choice is remembered by — the MMDevice endpoint id for a microphone, the
/// frame source group's id for a camera. macshot remembers an <c>AVCaptureDevice</c>
/// <c>uniqueID</c> for the same purpose; both are strings the platform can be asked to
/// resolve back into a device, and neither survives being carried to another machine.
/// </param>
/// <param name="Name">What the hardware calls itself, which is never translated.</param>
public sealed record RecordingDevice(string Id, string Name);

/// <summary>One device's row in the menu, and whether it carries the tick.</summary>
public sealed record RecordingDeviceRow(RecordingDevice Device, bool IsChosen);

/// <summary>
/// Which device a recording uses, and what its menu shows.
/// </summary>
/// <remarks>
/// <para>
/// macshot keeps the choice in <c>selectedMicDeviceUID</c> and
/// <c>selectedCameraDeviceUID</c> and resolves it at the moment it opens the device
/// (<c>RecordingEngine.swift:278</c>, <c>WebcamOverlay.swift:92</c>). The rule that
/// matters is the one for a device that has gone: a remembered id that no longer resolves
/// falls back to the system default and the recording goes ahead. A headset unplugged
/// since the last recording must not be the reason this one has no sound.
/// </para>
/// <para>
/// Enumerating the devices is the platform's job and cannot happen here. Deciding what to
/// do with the list can, which is why the fallback lives in one tested place rather than
/// once in the microphone's opener and again in the camera's.
/// </para>
/// </remarks>
public static class RecordingDevices
{
    /// <summary>
    /// The id to open, or null to let the platform open whatever it would have.
    /// </summary>
    /// <remarks>
    /// Null rather than the default device's own id, because the two callers reach their
    /// default by different routes — the audio engine has a call that answers "the default
    /// microphone", while a camera is simply the first one enumerated — and naming the
    /// default here would mean asking for it twice.
    /// </remarks>
    public static string? Chosen(string? rememberedId, IReadOnlyList<RecordingDevice> available)
    {
        ArgumentNullException.ThrowIfNull(available);

        if (string.IsNullOrEmpty(rememberedId))
        {
            return null;
        }

        return available.Any(device => string.Equals(device.Id, rememberedId, StringComparison.Ordinal))
            ? rememberedId
            : null;
    }

    /// <summary>
    /// The device rows of the menu, in the order the machine listed them, with the tick on
    /// the one a recording started now would use.
    /// </summary>
    /// <param name="available">Every device of that kind, as the platform enumerated them.</param>
    /// <param name="rememberedId">What was chosen last, or empty for never.</param>
    /// <param name="systemDefaultId">What the platform would open unasked, or null if unknown.</param>
    /// <param name="on">Whether the switch this menu hangs off is on.</param>
    /// <remarks>
    /// <para>
    /// The machine's order rather than alphabetical, which is macshot's
    /// (<c>OverlayView.swift:7631</c>): the order an audio API lists endpoints in is the
    /// order the system prefers them, and sorting it away would put the built-in
    /// microphone above the headset that was plugged in to be used.
    /// </para>
    /// <para>
    /// Nothing is ticked while the switch is off — the tick is then on the menu's None row,
    /// which is the row that turned it off. A device ticked under a switch that is off
    /// would claim a recording would use it.
    /// </para>
    /// <para>
    /// A device with no id is left out. The id is the whole of what a choice is remembered
    /// by, so a row that could be picked and not recalled is a row that does nothing.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<RecordingDeviceRow> Menu(
        IReadOnlyList<RecordingDevice> available,
        string? rememberedId,
        string? systemDefaultId,
        bool on)
    {
        ArgumentNullException.ThrowIfNull(available);

        var listed = available.Where(device => !string.IsNullOrEmpty(device.Id)).ToArray();
        var chosen = Chosen(rememberedId, listed) ?? systemDefaultId;

        return
        [
            .. listed.Select(device => new RecordingDeviceRow(
                device,
                on && string.Equals(device.Id, chosen, StringComparison.Ordinal))),
        ];
    }
}
