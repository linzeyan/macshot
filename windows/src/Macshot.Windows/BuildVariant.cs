namespace Macshot.Windows;

/// <summary>
/// Which build this is, for the few places that have to say so out loud.
/// </summary>
/// <remarks>
/// <para>
/// The offline variant is the same tree with the network features compiled out, not a
/// fork. Everything that reaches the network sits behind <c>#if !OFFLINE</c>, so the
/// compiler — rather than a reviewer's memory — is what guarantees an offline build
/// contains no uploader at all.
/// </para>
/// <para>
/// This type is the one place allowed to branch on the variant at runtime, and only
/// for what the user is shown. Anything that would do the uploading must be compiled
/// out instead: a runtime check leaves the code in the binary, which is exactly what
/// the variant exists to avoid.
/// </para>
/// </remarks>
public static class BuildVariant
{
#if OFFLINE
    public const bool IsOffline = true;
#else
    public const bool IsOffline = false;
#endif

    /// <summary>What to call this build in a window title or an about box.</summary>
    public static string DisplayName => IsOffline ? "macshot Offline" : "macshot";
}
