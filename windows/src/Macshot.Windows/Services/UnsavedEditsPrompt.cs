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
/// you close without saving.", answered by "Save &amp; Close", "Discard" and "Cancel"
/// (<c>DetachedEditorWindowController.swift:283-289</c>). All five strings are already in
/// the vendored translations, so this asks in the user's language without adding a key.
/// </para>
/// <para>
/// The labels are macshot's rather than the shell's Yes/No/Cancel because this is the one
/// prompt in the app where guessing wrong destroys work: "No" answering "Save changes?"
/// can be read as discard or as don't-close, and only one of those readings loses the
/// marks. <c>UploadConfirm</c> can take the shell's OK for macshot's "Upload" — nothing is
/// lost by getting that one wrong.
/// </para>
/// <para>
/// A Win32 task dialog rather than a <c>ContentDialog</c>, which is what this port uses for
/// every other question it asks — and here the native one buys something the XAML dialog
/// cannot: it is modal and synchronous, so the answer is known before the closing handler
/// returns. A <c>ContentDialog</c> would have to cancel the close, await, and then close
/// again from its continuation, which means the window closing itself from inside its own
/// close notification.
/// </para>
/// </remarks>
public static class UnsavedEditsPrompt
{
    /// <summary>
    /// TDF_ALLOW_DIALOG_CANCELLATION. All three buttons are custom, and without this flag a
    /// task dialog whose common-button set is empty has no close box and ignores Esc — the
    /// user would be able to reach "Discard" but not to back out.
    /// </summary>
    private const uint AllowDialogCancellation = 0x0008;

    /// <summary>
    /// TDF_POSITION_RELATIVE_TO_WINDOW. macOS asks this as a sheet on the editor window;
    /// centring on the owner is as close as Windows gets to attaching it to one.
    /// </summary>
    private const uint PositionRelativeToWindow = 0x1000;

    /// <summary>
    /// TD_WARNING_ICON, matching the alert's <c>alertStyle = .warning</c>. It is
    /// <c>MAKEINTRESOURCEW(-1)</c>, and the -1 narrows to a WORD before it becomes a
    /// pointer — so the value the struct carries is 0xFFFF, not a sign-extended ~0.
    /// </summary>
    private const nint WarningIcon = 0xFFFF;

    /// <summary>
    /// Ids for the three custom buttons. They must miss the common ones (IDOK through
    /// IDCONTINUE, 1-11): <see cref="AllowDialogCancellation"/> reports Esc and the close
    /// box as IDCANCEL, so an id that collided with 2 would make dismissal indistinguishable
    /// from a deliberate press.
    /// </summary>
    private const int IdSaveAndClose = 101;

    /// <inheritdoc cref="IdSaveAndClose"/>
    private const int IdDiscard = 102;

    /// <inheritdoc cref="IdSaveAndClose"/>
    private const int IdCancel = 103;

    private const uint MessageBoxYesNoCancel = 0x00000003;

    private const uint MessageBoxIconWarning = 0x00000030;

    private const int MessageBoxYes = 6;

    private const int MessageBoxNo = 7;

    /// <summary>Asks, and says what to do about the window that is trying to close.</summary>
    /// <remarks>
    /// Every way of failing lands on staying open or on the older message box, never on
    /// discarding: losing a capture's marks because a dialog would not show would be the
    /// worst of the three outcomes to arrive at by accident.
    /// </remarks>
    public static UnsavedEdits Ask(nint owner)
    {
        try
        {
            return AskWithTaskDialog(owner);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // TaskDialogIndirect is exported by comctl32 version 6 only, which the process
            // reaches through the dependency in app.manifest. If that ever stops resolving
            // the question still has to be asked, so fall back rather than propagate.
            DiagnosticLog.Write($"Task dialog unavailable, asking with a message box: {exception.Message}");
            return AskWithMessageBox(owner);
        }
    }

    private static UnsavedEdits AskWithTaskDialog(nint owner)
    {
        var owned = new List<nint>(4);
        var buttons = nint.Zero;

        try
        {
            var stride = Marshal.SizeOf<TaskDialogButton>();
            buttons = Marshal.AllocHGlobal(stride * 3);

            // Written through StructureToPtr rather than pinned as a managed array, because
            // this is the unmanaged layout by construction — nothing has to be assumed
            // about how the runtime packs the managed copy.
            Marshal.StructureToPtr(
                new TaskDialogButton
                {
                    ButtonId = IdSaveAndClose,
                    Text = Allocate(owned, Literal(Localization.L("Save & Close"))),
                },
                buttons,
                false);

            Marshal.StructureToPtr(
                new TaskDialogButton
                {
                    ButtonId = IdDiscard,
                    Text = Allocate(owned, Literal(Localization.L("Discard"))),
                },
                buttons + stride,
                false);

            Marshal.StructureToPtr(
                new TaskDialogButton
                {
                    ButtonId = IdCancel,
                    Text = Allocate(owned, Literal(Localization.L("Cancel"))),
                },
                buttons + (stride * 2),
                false);

            var config = new TaskDialogConfig
            {
                Size = (uint)Marshal.SizeOf<TaskDialogConfig>(),
                Parent = owner,
                Instance = nint.Zero,
                Flags = AllowDialogCancellation | PositionRelativeToWindow,
                CommonButtons = 0,
                WindowTitle = Allocate(owned, "macshot"),
                MainIcon = WarningIcon,
                MainInstruction = Allocate(owned, Localization.L("Save changes?")),
                Content = Allocate(
                    owned,
                    Localization.L("Your annotations will be lost if you close without saving.")),
                ButtonCount = 3,
                Buttons = buttons,

                // macOS's first button is its default, and its first button is Save & Close.
                DefaultButton = IdSaveAndClose,
                RadioButtonCount = 0,
                RadioButtons = nint.Zero,
                DefaultRadioButton = 0,
                VerificationText = nint.Zero,
                ExpandedInformation = nint.Zero,
                ExpandedControlText = nint.Zero,
                CollapsedControlText = nint.Zero,
                FooterIcon = nint.Zero,
                Footer = nint.Zero,
                Callback = nint.Zero,
                CallbackData = nint.Zero,
                Width = 0,
            };

            var result = TaskDialogIndirect(in config, out var pressed, nint.Zero, nint.Zero);
            if (result < 0)
            {
                DiagnosticLog.Write($"Task dialog refused to show (0x{result:X8}), asking with a message box.");
                return AskWithMessageBox(owner);
            }

            return pressed switch
            {
                IdSaveAndClose => UnsavedEdits.Keep,
                IdDiscard => UnsavedEdits.Discard,

                // Cancel, Esc and the close box all land here, as does anything unexpected.
                _ => UnsavedEdits.Stay,
            };
        }
        finally
        {
            if (buttons != nint.Zero)
            {
                Marshal.FreeHGlobal(buttons);
            }

            foreach (var pointer in owned)
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
    }

    /// <summary>The shell's own question, for a process that cannot raise a task dialog.</summary>
    /// <remarks>
    /// Yes/No/Cancel loses the distinction the labels above exist to make, which is why it
    /// is the fallback and not the answer. A box that could not be shown — which
    /// <c>MessageBox</c> reports as zero, not as an error — is answered by staying open.
    /// </remarks>
    private static UnsavedEdits AskWithMessageBox(nint owner)
    {
        var text = Localization.L("Save changes?")
            + Environment.NewLine
            + Environment.NewLine
            + Localization.L("Your annotations will be lost if you close without saving.");

        return MessageBox(owner, text, "macshot", MessageBoxYesNoCancel | MessageBoxIconWarning) switch
        {
            MessageBoxYes => UnsavedEdits.Keep,
            MessageBoxNo => UnsavedEdits.Discard,
            _ => UnsavedEdits.Stay,
        };
    }

    private static nint Allocate(List<nint> owned, string text)
    {
        var pointer = Marshal.StringToHGlobalUni(text);
        owned.Add(pointer);
        return pointer;
    }

    /// <summary>
    /// A task dialog's buttons are ordinary push buttons, so a lone ampersand in their text
    /// is eaten as the mnemonic prefix and macshot's "Save &amp; Close" would read
    /// "Save  Close". Doubling is how Win32 spells a literal one, and it has to happen after
    /// translation because any language's label may contain an ampersand of its own.
    /// </summary>
    private static string Literal(string label) => label.Replace("&", "&&");

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern int TaskDialogIndirect(
        in TaskDialogConfig config,
        out int pressedButton,
        nint radioButton,
        nint verificationChecked);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(nint owner, string text, string caption, uint type);

    /// <summary>
    /// TASKDIALOG_BUTTON. Packed to 1 because commctl.h wraps both of these in
    /// <c>pshpack1.h</c>, which is what puts the text pointer at offset 4 rather than 8.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TaskDialogButton
    {
        public int ButtonId;

        public nint Text;
    }

    /// <summary>
    /// TASKDIALOGCONFIG, packed to 1 for the same reason as
    /// <see cref="TaskDialogButton"/>. Every string is a raw pointer rather than a
    /// marshalled <see cref="string"/> so the whole struct stays blittable: the call then
    /// pins one value instead of building a native copy, and the lifetime of each string is
    /// this file's to end rather than the marshaller's to guess.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TaskDialogConfig
    {
        public uint Size;

        public nint Parent;

        public nint Instance;

        public uint Flags;

        public uint CommonButtons;

        public nint WindowTitle;

        public nint MainIcon;

        public nint MainInstruction;

        public nint Content;

        public uint ButtonCount;

        public nint Buttons;

        public int DefaultButton;

        public uint RadioButtonCount;

        public nint RadioButtons;

        public int DefaultRadioButton;

        public nint VerificationText;

        public nint ExpandedInformation;

        public nint ExpandedControlText;

        public nint CollapsedControlText;

        public nint FooterIcon;

        public nint Footer;

        public nint Callback;

        public nint CallbackData;

        public uint Width;
    }
}
