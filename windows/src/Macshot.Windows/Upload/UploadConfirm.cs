#if !OFFLINE
using Macshot.Windows.Core.Upload;
using Macshot.Windows.Services;

namespace Macshot.Windows.Upload;

/// <summary>
/// The question macshot asks before a capture leaves the machine, when it is set to ask.
/// </summary>
/// <remarks>
/// <para>
/// macshot's alert in <c>OverlayView.swift</c>: the provider's own title, one line of
/// explanation, and Upload against Cancel. Off by default in both products — the button
/// is already a deliberate act — and turned on from the right-click on that button.
/// </para>
/// <para>
/// A shell alert rather than a <c>ContentDialog</c>: a dialog inside the overlay would be
/// drawn into the capture the user is about to send.
/// </para>
/// </remarks>
internal static class UploadConfirm
{
    /// <summary>Asks, and says whether the upload should go ahead.</summary>
    /// <remarks>
    /// Owned by the overlay so the box is drawn above it. A question that could not be put
    /// is treated as a no: an upload nobody confirmed must not happen because a dialog
    /// could not be shown.
    /// </remarks>
    public static bool Ask(nint owner, UploadProvider provider) =>
        Alert.Show(
            owner,
            Localization.L(UploadProviders.ConfirmTitle(provider)),
            Localization.L("Your screenshot will be uploaded."),
            Alert.Icon.Information,
            Localization.L("Upload"),
            Localization.L("Cancel")) == 0;
}
#endif
