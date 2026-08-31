using System.Runtime.InteropServices;

namespace Macshot.Windows.Services;

/// <summary>
/// The modal box macshot asks with, in macshot's own language.
/// </summary>
/// <remarks>
/// <para>
/// A <c>MessageBox</c> takes its buttons from the shell, and the shell draws them in the
/// system's language rather than the app's — so macshot set to 正體中文 on an English
/// Windows asked its question in Chinese and offered OK and Cancel to answer it. A task
/// dialog takes its button text as strings, which is the whole reason this exists.
/// </para>
/// <para>
/// Modal and synchronous, unlike a <c>ContentDialog</c>. <see cref="UnsavedEditsPrompt"/>
/// needs the answer before a window's closing handler returns, and the failures worth
/// reporting most are the ones where building a window is what went wrong.
/// </para>
/// <para>
/// Everything below the task dialog — the same question with no owner, then a plain
/// message box, then that with no owner — is there because a box that cannot be shown
/// answers nothing. macshot raises most of these from the message-only window that
/// receives the hotkeys, which cannot own a dialog, and <c>MessageBox</c> reports that
/// refusal as a zero rather than as an error; that is how a whole class of failures came
/// to be reported to nobody.
/// </para>
/// </remarks>
internal static class Alert
{
    /// <summary>Which of the shell's icons the box carries.</summary>
    internal enum Icon
    {
        Information,

        Warning,

        Error,
    }

    /// <summary>
    /// TDF_ALLOW_DIALOG_CANCELLATION. Every button is custom, and without this flag a task
    /// dialog whose common-button set is empty has no close box and ignores Esc — the user
    /// would be able to reach the destructive answer but not to back out.
    /// </summary>
    private const uint AllowDialogCancellation = 0x0008;

    /// <summary>
    /// TDF_POSITION_RELATIVE_TO_WINDOW. macOS asks these as sheets on the window they
    /// belong to; centring on the owner is as close as Windows gets to attaching one.
    /// </summary>
    private const uint PositionRelativeToWindow = 0x1000;

    /// <summary>
    /// The id of the first button; the rest follow it. They must miss the common ones
    /// (IDOK through IDCONTINUE, 1-11): <see cref="AllowDialogCancellation"/> reports Esc
    /// and the close box as IDCANCEL, so an id that collided with 2 would make dismissal
    /// indistinguishable from a deliberate press.
    /// </summary>
    private const int FirstButtonId = 101;

    private const uint MessageBoxOk = 0x00000000;

    private const uint MessageBoxOkCancel = 0x00000001;

    private const uint MessageBoxYesNoCancel = 0x00000003;

    private const int MessageBoxOkPressed = 1;

    private const int MessageBoxYesPressed = 6;

    private const int MessageBoxNoPressed = 7;

    /// <summary>
    /// Asks, and answers with the index of the button pressed — or null when no box could
    /// be raised at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="labels"/> reads the way macOS's alert does: the first is the
    /// default, the last is the way out. Esc, the close box and anything else unexpected
    /// all answer with the last index, which is what lets a caller treat backing out and
    /// pressing Cancel as the same thing without asking which happened.
    /// </para>
    /// <para>
    /// A null is not a refusal — it means the question was never put. A caller that was
    /// going to act on a yes has to decide for itself what silence means, and for every
    /// caller here it means no.
    /// </para>
    /// </remarks>
    /// <param name="instruction">The heading, or null for a box that is all body text.</param>
    internal static int? Show(
        nint owner, string? instruction, string content, Icon icon, params string[] labels)
    {
        if (labels.Length is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(labels), labels.Length, "An alert carries one, two or three buttons.");
        }

        try
        {
            if (WithTaskDialog(owner, instruction, content, icon, labels) is { } pressed)
            {
                return pressed;
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // TaskDialogIndirect is exported by comctl32 version 6 only, which the process
            // reaches through the dependency in app.manifest. If that ever stops resolving
            // the question still has to be asked, so fall back rather than propagate.
            DiagnosticLog.Write($"Task dialog unavailable, asking with a message box: {exception.Message}");
        }

        return WithMessageBox(owner, instruction, content, icon, labels);
    }

    private static int? WithTaskDialog(
        nint owner, string? instruction, string content, Icon icon, string[] labels)
    {
        var owned = new List<nint>(labels.Length + 3);
        var buttons = nint.Zero;

        try
        {
            var stride = Marshal.SizeOf<TaskDialogButton>();
            buttons = Marshal.AllocHGlobal(stride * labels.Length);

            for (var index = 0; index < labels.Length; index++)
            {
                // Written through StructureToPtr rather than pinned as a managed array,
                // because this is the unmanaged layout by construction — nothing has to be
                // assumed about how the runtime packs the managed copy.
                Marshal.StructureToPtr(
                    new TaskDialogButton
                    {
                        ButtonId = FirstButtonId + index,
                        Text = Allocate(owned, Literal(labels[index])),
                    },
                    buttons + (stride * index),
                    false);
            }

            var config = new TaskDialogConfig
            {
                Size = (uint)Marshal.SizeOf<TaskDialogConfig>(),
                Parent = owner,
                Flags = AllowDialogCancellation | PositionRelativeToWindow,
                WindowTitle = Allocate(owned, "macshot"),
                MainIcon = TaskDialogIcon(icon),
                MainInstruction = instruction is null ? nint.Zero : Allocate(owned, instruction),
                Content = Allocate(owned, content),
                ButtonCount = (uint)labels.Length,
                Buttons = buttons,

                // macOS's first button is its default, in every alert macshot raises.
                DefaultButton = FirstButtonId,
            };

            var result = TaskDialogIndirect(in config, out var pressed, nint.Zero, nint.Zero);
            if (result < 0 && owner != nint.Zero)
            {
                DiagnosticLog.Write($"Task dialog refused the owner (0x{result:X8}), asking without one.");
                config.Parent = nint.Zero;
                result = TaskDialogIndirect(in config, out pressed, nint.Zero, nint.Zero);
            }

            if (result < 0)
            {
                DiagnosticLog.Write($"Task dialog refused to show (0x{result:X8}), asking with a message box.");
                return null;
            }

            // IDCANCEL — which is what AllowDialogCancellation reports Esc and the close box
            // as — falls outside the custom ids, and so does anything else unexpected.
            var chosen = pressed - FirstButtonId;
            return chosen >= 0 && chosen < labels.Length ? chosen : labels.Length - 1;
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

    /// <summary>The shell's own box, for a process that cannot raise a task dialog.</summary>
    /// <remarks>
    /// Its buttons are in the system's language, which is the thing this class exists to
    /// avoid — but a question asked in the wrong language is still better than a question
    /// nobody was asked. The common set of the matching size stands in, in macshot's own
    /// order: OK, then OK/Cancel, then Yes/No/Cancel.
    /// </remarks>
    private static int? WithMessageBox(
        nint owner, string? instruction, string content, Icon icon, string[] labels)
    {
        var text = instruction is null
            ? content
            : instruction + Environment.NewLine + Environment.NewLine + content;

        var style = MessageBoxIcon(icon) | labels.Length switch
        {
            1 => MessageBoxOk,
            2 => MessageBoxOkCancel,
            _ => MessageBoxYesNoCancel,
        };

        var answer = MessageBox(owner, text, "macshot", style);
        if (answer == 0 && owner != nint.Zero)
        {
            answer = MessageBox(nint.Zero, text, "macshot", style);
        }

        return answer switch
        {
            0 => null,
            MessageBoxOkPressed or MessageBoxYesPressed => 0,
            MessageBoxNoPressed => 1,
            _ => labels.Length - 1,
        };
    }

    /// <summary>
    /// TD_INFORMATION_ICON, TD_WARNING_ICON and TD_ERROR_ICON. Each is a
    /// <c>MAKEINTRESOURCEW</c> of a small negative number, and the negative narrows to a
    /// WORD before it becomes a pointer — so the value the struct carries is 0xFFFD, not a
    /// sign-extended ~2.
    /// </summary>
    private static nint TaskDialogIcon(Icon icon) => icon switch
    {
        Icon.Warning => 0xFFFF,
        Icon.Error => 0xFFFE,
        _ => 0xFFFD,
    };

    /// <summary>MB_ICONWARNING, MB_ICONERROR and MB_ICONINFORMATION.</summary>
    private static uint MessageBoxIcon(Icon icon) => icon switch
    {
        Icon.Warning => 0x00000030,
        Icon.Error => 0x00000010,
        _ => 0x00000040,
    };

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
    /// TASKDIALOG_BUTTON. Packed to 1 because commctrl.h wraps both of these in
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
    /// this file's to end rather than the marshaller's to guess. The fields nothing sets
    /// are the zeroes a task dialog reads as "no radio buttons, no footer, no callback".
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
