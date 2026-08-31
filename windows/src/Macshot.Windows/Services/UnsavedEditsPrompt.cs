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
/// you close without saving.", answered by "Save &amp; Close", "Discard" and "Cancel"
/// (<c>DetachedEditorWindowController.swift:283-289</c>). All five strings are already in
/// the vendored translations, so this asks in the user's language without adding a key.
/// </para>
/// <para>
/// The three labels are why <see cref="Alert"/> exists. This is the one prompt in the app
/// where guessing wrong destroys work: "No" answering "Save changes?" can be read as
/// discard or as don't-close, and only one of those readings loses the marks.
/// </para>
/// </remarks>
public static class UnsavedEditsPrompt
{
    /// <summary>Asks, and says what to do about the window that is trying to close.</summary>
    /// <remarks>
    /// Every way of failing lands on staying open or on the shell's Yes/No/Cancel, never on
    /// discarding: losing a capture's marks because a dialog would not show would be the
    /// worst of the three outcomes to arrive at by accident.
    /// </remarks>
    public static UnsavedEdits Ask(nint owner)
    {
        var pressed = Alert.Show(
            owner,
            Localization.L("Save changes?"),
            Localization.L("Your annotations will be lost if you close without saving."),

            // Matching the alert's alertStyle = .warning.
            Alert.Icon.Warning,
            Localization.L("Save & Close"),
            Localization.L("Discard"),
            Localization.L("Cancel"));

        return pressed switch
        {
            0 => UnsavedEdits.Keep,
            1 => UnsavedEdits.Discard,

            // Cancel, Esc, the close box, and a box that could not be shown at all.
            _ => UnsavedEdits.Stay,
        };
    }
}
