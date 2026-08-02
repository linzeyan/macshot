using System.Runtime.InteropServices;

namespace Macshot.Windows.Services;

/// <summary>
/// The two boxes that are not failures: something said, and something asked.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FailureReport"/> is for what went wrong — it logs, and it draws the error
/// icon. Neither is right for "macshot is up to date", which is the answer the user
/// asked for and not a fault, so it has its own two calls rather than a flag threaded
/// through that one.
/// </para>
/// <para>
/// A shell message box for the same reason the failures use one: macshot is a background
/// app that usually has no window, and the window it does have most often is the
/// message-only one that receives the hotkeys, which cannot host a XAML dialog.
/// </para>
/// </remarks>
internal static class Message
{
    private const uint Ok = 0x00000000;

    private const uint OkCancel = 0x00000001;

    private const uint IconInformation = 0x00000040;

    /// <summary>What MessageBox returns for OK. Anything else is a refusal.</summary>
    private const int Accepted = 1;

    /// <summary>Says something, and waits for it to be read.</summary>
    public static void Say(nint owner, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        Show(owner, text, Ok | IconInformation);
    }

    /// <summary>Asks something, and answers whether it was accepted.</summary>
    /// <remarks>
    /// A box that could not be shown is a no. Whatever was going to happen next was
    /// waiting on an answer, and taking silence for consent is how a program does
    /// something nobody agreed to.
    /// </remarks>
    public static bool Ask(nint owner, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        return Show(owner, text, OkCancel | IconInformation) == Accepted;
    }

    /// <remarks>
    /// Asked again with no owner when the first attempt is refused, which is what
    /// <see cref="FailureReport.Notice"/> does and for the same reason: a message-only
    /// window cannot own a dialog, and MessageBox reports that refusal as a zero rather
    /// than as an error — so without this the box is shown to nobody.
    /// </remarks>
    private static int Show(nint owner, string text, uint style)
    {
        var answer = MessageBox(owner, text, "macshot", style);
        return answer == 0 && owner != IntPtr.Zero
            ? MessageBox(IntPtr.Zero, text, "macshot", style)
            : answer;
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
