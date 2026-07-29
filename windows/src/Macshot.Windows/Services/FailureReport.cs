using System.Runtime.InteropServices;
using System.Text;

namespace Macshot.Windows.Services;

/// <summary>
/// Puts a failure in front of the user, in enough detail to act on.
/// </summary>
/// <remarks>
/// <para>
/// macshot is a background app with no log and, most of the time, no window. Whoever
/// is looking at the box this shows is the only place the information ever exists, so
/// throwing away everything but the message throws away the whole diagnosis.
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
        MessageBox(owner, Describe(exception), "macshot", IconError);
    }

    /// <summary>
    /// The exception as something that can be acted on: what it was, what every inner
    /// exception was, and where it came from.
    /// </summary>
    /// <remarks>
    /// The type matters as much as the message. WinUI reports every markup fault as
    /// "XAML parsing failed." and names neither the window nor the key, so without the
    /// stack there is nothing to look at.
    /// </remarks>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

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
