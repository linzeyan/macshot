namespace Macshot.Windows.Services;

/// <summary>
/// The two boxes that are not failures: something said, and something asked.
/// </summary>
/// <remarks>
/// <see cref="FailureReport"/> is for what went wrong — it logs, and it draws the error
/// icon. Neither is right for "macshot is up to date", which is the answer the user asked
/// for and not a fault, so it has its own two calls rather than a flag threaded through
/// that one. Both are <see cref="Alert"/> with the information icon and macshot's own OK
/// and Cancel.
/// </remarks>
internal static class Message
{
    /// <summary>Says something, and waits for it to be read.</summary>
    public static void Say(nint owner, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        Alert.Show(owner, null, text, Alert.Icon.Information, Localization.L("OK"));
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

        return Alert.Show(
            owner,
            null,
            text,
            Alert.Icon.Information,
            Localization.L("OK"),
            Localization.L("Cancel")) == 0;
    }
}
