namespace Macshot.Windows.Core.Output;

/// <summary>
/// Which model Remove Background asks to find the subject.
/// </summary>
/// <remarks>
/// <para>
/// A choice macOS does not offer, because macOS has nothing to choose between: subject
/// lifting is in every Mac from Sonoma on. Here there are two models with genuinely
/// different bargains — Windows AI Foundry's is already on the machine and needs no
/// download, but runs only on a Copilot+ PC and only from a packaged build; macshot's own
/// runs anywhere and costs a 4 MB download once.
/// </para>
/// <para>
/// So the setting exists to let someone who has both say which they want, not to make
/// anyone decide before they can press the button. <see cref="Automatic"/> is the default
/// and is what almost everyone should leave it on.
/// </para>
/// </remarks>
public enum BackgroundRemovalBackend
{
    /// <summary>Foundry where it works, macshot's own model everywhere else.</summary>
    Automatic,

    /// <summary>
    /// Foundry only. Refuses rather than falling back, so a machine that should be able to
    /// use it says why instead of quietly doing something else.
    /// </summary>
    WindowsAi,

    /// <summary>
    /// macshot's own model only, even where Foundry works — which is the useful direction
    /// of the two, since it is the one a user can reach on a machine where the other is
    /// silently unavailable.
    /// </summary>
    LocalModel,
}
