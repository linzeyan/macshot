using System.Globalization;

namespace Macshot.Windows.Core.Output;

/// <summary>
/// Where a downloaded update is kept while it is being made ready.
/// </summary>
/// <remarks>
/// Under the same directory as the settings and the history rather than beside the
/// program: the program's own folder is the thing about to be replaced, and a machine
/// where macshot was unzipped into a folder it cannot write to is exactly the machine
/// where the download has to land somewhere else.
/// </remarks>
public static class UpdateStaging
{
    /// <summary>Everything downloaded for any version, so one sweep clears them all.</summary>
    public static string Root(string localAppData) =>
        Path.Combine(localAppData, "macshot", "updates");

    /// <summary>The folder one release is prepared in.</summary>
    public static string ForRelease(string localAppData, string tag) =>
        Path.Combine(Root(localAppData), Safe(tag));

    /// <summary>The zip as downloaded.</summary>
    public static string Archive(string localAppData, string tag) =>
        Path.Combine(ForRelease(localAppData, tag), "download.zip");

    /// <summary>
    /// The unpacked build, which is both what gets copied into place and what the copying
    /// is run from.
    /// </summary>
    /// <remarks>
    /// One copy, not two. Windows lets a running executable be read, so the staged build
    /// can copy its own folder over the installed one — which is what saves a
    /// self-contained macshot from being written to disk three times to update itself.
    /// </remarks>
    public static string Payload(string localAppData, string tag) =>
        Path.Combine(ForRelease(localAppData, tag), "payload");

    /// <summary>
    /// <paramref name="tag"/> with anything that is not a path component taken out.
    /// </summary>
    /// <remarks>
    /// The tag comes off the network. Every tag either product has ever cut is already a
    /// legal file name, so this changes nothing today — but it is the one value in the
    /// path that macshot does not choose, and a tag of <c>../../..</c> would otherwise
    /// name a folder to delete rather than one to download into.
    /// </remarks>
    private static string Safe(string tag)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. tag.Select(character => invalid.Contains(character) ? '_' : character)]);

        // A name made only of dots is legal in every character and still names the folder
        // above rather than one inside it.
        return cleaned.Length == 0 || cleaned.All(character => character == '.') ? "_" : cleaned;
    }
}

/// <summary>
/// What one macshot tells the next when it hands an update over to it.
/// </summary>
/// <remarks>
/// <para>
/// Putting an update in place cannot be done by the program being replaced, so the
/// downloaded build is started with this and does it: it waits for the old process to
/// end, copies itself over the installed folder, and starts what it wrote. The new build
/// doing the work rather than the old one is deliberate — it means the copying is done by
/// the code that shipped with the update, and it proves the downloaded executable runs
/// before anything is overwritten.
/// </para>
/// <para>
/// Carried as separate arguments rather than one string, because the target is a
/// directory path and a great many of those have spaces in them.
/// </para>
/// </remarks>
/// <param name="TargetDirectory">The installed folder to replace.</param>
/// <param name="WaitForProcessId">
/// The macshot that asked, which still has the folder open. Its ending is what makes the
/// copy possible.
/// </param>
public readonly record struct UpdateHandover(string TargetDirectory, int WaitForProcessId)
{
    /// <summary>The switch that marks a launch as this rather than an ordinary start.</summary>
    public const string Switch = "--apply-update";

    private const string TargetSwitch = "--target";

    private const string WaitSwitch = "--wait";

    /// <summary>The arguments to start the staged build with.</summary>
    public IReadOnlyList<string> Arguments =>
    [
        Switch,
        TargetSwitch,
        TargetDirectory,
        WaitSwitch,
        WaitForProcessId.ToString(CultureInfo.InvariantCulture),
    ];

    /// <summary>
    /// The handover <paramref name="arguments"/> carry, or null when this launch is not
    /// one.
    /// </summary>
    /// <remarks>
    /// A launch that says <see cref="Switch"/> but cannot be read is null as well, which
    /// starts macshot normally. The alternative — refusing to run — would leave a user
    /// whose update went wrong with a program that will not start at all.
    /// </remarks>
    public static UpdateHandover? Parse(IReadOnlyList<string>? arguments)
    {
        if (arguments is null || !arguments.Contains(Switch, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var target = After(arguments, TargetSwitch);
        var wait = After(arguments, WaitSwitch);

        return target is { Length: > 0 }
            && int.TryParse(wait, NumberStyles.None, CultureInfo.InvariantCulture, out var processId)
                ? new UpdateHandover(target, processId)
                : null;

        static string? After(IReadOnlyList<string> arguments, string name)
        {
            for (var index = 0; index < arguments.Count - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[index + 1];
                }
            }

            return null;
        }
    }
}
