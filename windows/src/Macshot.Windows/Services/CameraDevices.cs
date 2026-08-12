using Macshot.Windows.Core.Capture;

using Windows.Media.Capture.Frames;

namespace Macshot.Windows.Services;

/// <summary>
/// Which cameras the machine has, and which one the webcam bubble opens.
/// </summary>
/// <remarks>
/// <para>
/// A camera is named here by its <see cref="MediaFrameSourceGroup"/> id, because that is
/// what opening one takes: <c>MediaCaptureInitializationSettings.SourceGroup</c> is handed
/// the group itself, so remembering anything else would mean translating between two
/// naming schemes every time. It is the port's <c>selectedCameraDeviceUID</c>.
/// </para>
/// <para>
/// Only groups that carry a colour source are offered, for the reason
/// <see cref="WebcamWindow"/> gives: a machine can have an infrared camera for Windows
/// Hello alongside the colour one, and it enumerates first on some of them. Offering it
/// would put a greyscale face-recognition feed in the menu, and in the recording of anyone
/// who picked it.
/// </para>
/// </remarks>
internal static class CameraDevices
{
    /// <summary>Every camera that can be recorded, in the order the machine lists them.</summary>
    public static async Task<IReadOnlyList<RecordingDevice>> ListAsync() =>
        [.. (await ColourGroupsAsync()).Select(Named)];

    /// <summary>
    /// The camera to open: the remembered one while it is still plugged in, and otherwise
    /// the first the machine offers.
    /// </summary>
    /// <remarks>
    /// The fallback is macshot's (<c>WebcamOverlay.swift:92</c>): a UID that no longer
    /// resolves opens the default camera rather than nothing. A recording with the wrong
    /// camera in the corner is a mistake anyone can see and fix; a recording that refused
    /// to start because a webcam was unplugged is one they cannot.
    /// </remarks>
    public static async Task<MediaFrameSourceGroup?> ChosenAsync(string? rememberedId)
    {
        var groups = await ColourGroupsAsync();
        var chosen = RecordingDevices.Chosen(rememberedId, [.. groups.Select(Named)]);

        return groups.FirstOrDefault(group => string.Equals(group.Id, chosen, StringComparison.Ordinal))
            ?? groups.FirstOrDefault();
    }

    private static RecordingDevice Named(MediaFrameSourceGroup group) =>
        new(group.Id, group.DisplayName);

    /// <summary>
    /// The groups that give colour pictures. Chosen by what a group says it has before
    /// anything is opened, so a machine whose infrared camera enumerates first is not
    /// opened and then rejected.
    /// </summary>
    private static async Task<IReadOnlyList<MediaFrameSourceGroup>> ColourGroupsAsync()
    {
        try
        {
            var groups = await MediaFrameSourceGroup.FindAllAsync();
            return
            [
                .. groups.Where(group => group.SourceInfos.Any(
                    info => info.SourceKind is MediaFrameSourceKind.Color)),
            ];
        }
        catch (Exception exception)
        {
            // A machine that will not say what cameras it has offers none, which is the
            // same answer as a machine with none — and is what Windows' camera privacy
            // setting looks like from here.
            DiagnosticLog.Write($"Could not list the cameras: {exception.Message}");
            return [];
        }
    }
}
