using System.Runtime.InteropServices;
using System.Text;

namespace Macshot.Windows.Services;

/// <summary>
/// Puts a failure in front of the user, in enough detail to act on, and leaves a copy
/// behind for when nobody was looking.
/// </summary>
/// <remarks>
/// <para>
/// macshot is a background app with, most of the time, no window. The box this shows
/// is all the user ever sees of a failure, so throwing away everything but the message
/// throws away the whole diagnosis; <see cref="DiagnosticLog"/> is what keeps the same
/// text once the box has been dismissed.
/// </para>
/// <para>
/// A shell message box rather than a XAML dialog, because a failure while starting a
/// capture may have no window to host one — and the failures worth reporting most are
/// the ones where building a window is what went wrong.
/// </para>
/// </remarks>
public static class FailureReport
{
    /// <summary>Stack frames reported. A message box does not scroll.</summary>
    private const int StackFrames = 8;

    private const uint IconError = 0x00000010;

    public static void Show(nint owner, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Notice(owner, Describe(exception));
    }

    /// <summary>
    /// Reports something that is not an exception but still has to reach the user: a
    /// capture that degraded to the older backend, a page too long to finish.
    /// </summary>
    /// <remarks>
    /// Told the same way a failure is, because it is worth no less. These notices used
    /// to go straight to <c>MessageBox</c> with the message window as owner, which is
    /// the one combination that shows nothing at all — and the fallback reason is the
    /// only evidence there is that the preferred backend broke.
    /// </remarks>
    public static void Notice(nint owner, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        // Written first, and unconditionally. Whether the box appears depends on the
        // owner still being a window that can own one; whether the failure is knowable
        // afterwards must not.
        DiagnosticLog.Write(text);

        // A message-only window cannot own a dialog, and macshot reports most failures
        // from exactly such a window — the one that receives the hotkeys. MessageBox
        // answers a refusal with zero rather than an error, which is how a whole class
        // of failures came to be reported to nobody. Asking again with no owner is what
        // keeps the report on screen.
        if (MessageBox(owner, text, "macshot", IconError) == 0 && owner != IntPtr.Zero)
        {
            MessageBox(IntPtr.Zero, text, "macshot", IconError);
        }
    }

    /// <summary>
    /// The exception as something that can be acted on: what it was, what every inner
    /// exception was, and where it came from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The type matters as much as the message. WinUI reports every markup fault as
    /// "XAML parsing failed." and names neither the window nor the key, so without the
    /// stack there is nothing to look at.
    /// </para>
    /// <para>
    /// Except for the failures macshot saw coming, which are their message and nothing
    /// else. Decided here rather than at each call site, so that every route to the user
    /// — this one, and the unhandled-exception net in <c>App</c> — draws the same line.
    /// </para>
    /// </remarks>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is ExpectedFailureException)
        {
            return exception.Message;
        }

        var text = new StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            text.AppendLine($"{current.GetType().Name}: {current.Message}");
        }

        if (exception.StackTrace is { } stack)
        {
            text.AppendLine();
            text.AppendLine(string.Join(
                Environment.NewLine,
                stack.Split(Environment.NewLine).Take(StackFrames)));
        }

        return text.ToString();
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
