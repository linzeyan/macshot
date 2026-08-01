#if !OFFLINE
using System.Runtime.InteropServices;
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
/// A Win32 message box rather than a <c>ContentDialog</c>, which is what this port
/// already uses for the failures it has to report. A dialog inside the overlay would be
/// drawn into the capture the user is about to send.
/// </para>
/// </remarks>
internal static class UploadConfirm
{
    private const uint OkCancel = 0x00000001;

    private const uint IconInformation = 0x00000040;

    /// <summary>What MessageBox returns for OK. Anything else is a refusal.</summary>
    private const int Ok = 1;

    /// <summary>Asks, and says whether the upload should go ahead.</summary>
    public static bool Ask(nint owner, UploadProvider provider)
    {
        var text = Localization.L(UploadProviders.ConfirmTitle(provider))
            + Environment.NewLine
            + Environment.NewLine
            + Localization.L("Your screenshot will be uploaded.");

        // Owned by the overlay so the box is drawn above it. A refusal — which is what a
        // zero return means — is treated as a no: an upload nobody confirmed must not
        // happen because a dialog could not be shown.
        return MessageBox(owner, text, "macshot", OkCancel | IconInformation) == Ok;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(nint owner, string text, string caption, uint type);
}
#endif
