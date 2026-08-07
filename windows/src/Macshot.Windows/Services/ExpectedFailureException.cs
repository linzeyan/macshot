namespace Macshot.Windows.Services;

/// <summary>
/// A failure macshot saw coming, whose message is the whole diagnosis.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FailureReport"/> shows the type and eight stack frames with every failure,
/// deliberately: macshot is a background app with no window, so the box is all the user
/// ever sees and throwing the stack away would throw the diagnosis away with it.
/// </para>
/// <para>
/// That reasoning holds only for failures nobody predicted. A scroll capture aimed at an
/// elevated window cannot bring it forward — an unelevated process is not allowed to —
/// and answering an ordinary outcome with <c>InvalidOperationException</c> over a stack
/// trace through three files tells the user their tool broke, when what happened is that
/// they pointed it somewhere it cannot reach.
/// </para>
/// <para>
/// The message is therefore written for the user rather than for whoever reads the log,
/// and it should say what to do differently.
/// </para>
/// </remarks>
public sealed class ExpectedFailureException : Exception
{
    public ExpectedFailureException(string message)
        : base(message)
    {
    }

    public ExpectedFailureException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public ExpectedFailureException()
    {
    }
}
