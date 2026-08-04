using System.Runtime.InteropServices;

namespace Macshot.Windows.Services;

/// <summary>What the user chose when told the editor still holds unsaved marks.</summary>
public enum UnsavedEdits
{
    /// <summary>Write them back over the capture, then let the window go.</summary>
    Keep,

    /// <summary>Throw them away and let the window go.</summary>
    Discard,

    /// <summary>Stay in the editor.</summary>
    Stay,
}

/// <summary>
/// The question the editor asks before a window carrying unsaved marks closes.
/// </summary>
/// <remarks>
/// <para>
/// macshot's sheet, word for word: "Save changes?" over "Your annotations will be lost if
/// you close without saving." (<c>DetachedEditorWindowController.swift:283-289</c>). Both
/// strings are already in the vendored translations, so this asks in the user's language
/// without adding a key.
/// </para>
/// <para>
/// A Win32 message box rather than a <c>ContentDialog</c>, which is what this port already
/// uses for every other question it has to ask — and here it buys something a dialog
/// cannot: it is modal and synchronous, so the answer is known before the closing handler
/// returns. A <c>ContentDialog</c> would have to cancel the close, await, and then close
/// again from its continuation, which means the window closes itself from inside its own
/// close notification.
/// </para>
/// <para>
/// Its three buttons are the shell's Yes, No and Cancel rather than macshot's "Save &amp;
/// Close", "Discard" and "Cancel". "Save changes?" answered Yes/No/Cancel is how Windows
/// has asked this for thirty years, and the port already takes the shell's labels where
/// macshot names its own — <see cref="Macshot.Windows.Upload.UploadConfirm"/> does the
/// same with OK for macshot's "Upload".
/// </para>
/// </remarks>
public static class UnsavedEditsPrompt
{
    private const uint YesNoCancel = 0x00000003;

    private const uint IconWarning = 0x00000030;

    private const int IdCancel = 2;

    private const int IdYes = 6;

    private const int IdNo = 7;

    /// <summary>Asks, and says what to do about the window that is trying to close.</summary>
    /// <remarks>
    /// A box that could not be shown — which <c>MessageBox</c> reports as zero, not as an
    /// error — is answered by staying open. Losing a capture's marks because a dialog
    /// failed would be the worst of the three outcomes chosen by accident.
    /// </remarks>
    public static UnsavedEdits Ask(nint owner)
    {
        var text = Localization.L("Save changes?")
            + Environment.NewLine
            + Environment.NewLine
            + Localization.L("Your annotations will be lost if you close without saving.");

        return MessageBox(owner, text, "macshot", YesNoCancel | IconWarning) switch
        {
            IdYes => UnsavedEdits.Keep,
            IdNo => UnsavedEdits.Discard,
            IdCancel => UnsavedEdits.Stay,
            _ => UnsavedEdits.Stay,
        };
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(nint owner, string text, string caption, uint type);
}
