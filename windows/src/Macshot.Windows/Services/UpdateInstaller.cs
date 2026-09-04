using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Services;

/// <summary>Why an installation of macshot might not be able to replace itself.</summary>
public enum UpdateBlocker
{
    /// <summary>Nothing. Download it and put it in place.</summary>
    None,

    /// <summary>
    /// Installed from an MSIX. The package is Windows's to replace and a process inside
    /// the container cannot write to its own install directory, so the only honest answer
    /// is the release page.
    /// </summary>
    Packaged,

    /// <summary>
    /// Somewhere this user cannot write, which in practice means Program Files. Replacing
    /// it needs administrator rights, and a notification-area app asking for them to
    /// update itself is a prompt nobody expects — the page, and a decision the user makes
    /// knowingly, is the better answer.
    /// </summary>
    ReadOnly,
}

/// <summary>
/// Puts a downloaded release in place of the running one.
/// </summary>
/// <remarks>
/// <para>
/// The swap itself cannot be done by the program being swapped, so this half only gets as
/// far as a folder on disk and then starts what it downloaded with
/// <see cref="UpdateHandover"/>. <see cref="UpdateApplier"/> is the other half, and it
/// runs inside the new build.
/// </para>
/// <para>
/// A zip rather than the MSIX beside it: the zip is a folder of files, which is a thing
/// macshot can copy over itself, and no release carries an MSIX that Windows would install
/// anyway — there is no certificate to sign one with. See the release notes in CLAUDE.md.
/// </para>
/// </remarks>
internal static class UpdateInstaller
{
    /// <summary>What <c>GetCurrentPackageFullName</c> answers when there is no package.</summary>
    private const int NoPackage = 15700;

    /// <summary>The folder macshot is installed in, which is the one to replace.</summary>
    /// <remarks>
    /// Trimmed, because <see cref="AppContext.BaseDirectory"/> ends in a separator and
    /// a path that does cannot be renamed or have its own name read off it.
    /// </remarks>
    public static string InstallDirectory =>
        Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    /// <summary>Whether this installation can replace itself, and if not why not.</summary>
    public static UpdateBlocker Blocker()
    {
        if (IsPackaged())
        {
            return UpdateBlocker.Packaged;
        }

        return CanWriteTo(InstallDirectory) ? UpdateBlocker.None : UpdateBlocker.ReadOnly;
    }

    /// <summary>
    /// Downloads <paramref name="asset"/> and unpacks it, answering the folder holding the
    /// new build.
    /// </summary>
    /// <remarks>
    /// Anything already staged for this release is thrown away first. A half-finished
    /// attempt — the app was quit mid-download, the machine lost power — would otherwise
    /// be unpacked over and produce a folder that is partly one version and partly
    /// another, which is the one outcome worse than not updating.
    /// </remarks>
    public static async Task<string> StageAsync(
        string tag,
        ReleaseAsset asset,
        IProgress<double>? progress,
        CancellationToken token)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = UpdateStaging.ForRelease(local, tag);

        Remove(folder);
        Directory.CreateDirectory(folder);

        var archive = UpdateStaging.Archive(local, tag);
        await UpdateService.DownloadAsync(asset, archive, progress, token).ConfigureAwait(false);

        var payload = UpdateStaging.Payload(local, tag);
        ZipFile.ExtractToDirectory(archive, payload);

        // The zip is a hundred and fifty megabytes and has done its job. Leaving it would
        // double what an update costs on disk until the next start swept it up.
        File.Delete(archive);

        var executable = Path.Combine(payload, Path.GetFileName(Environment.ProcessPath) ?? string.Empty);
        if (!File.Exists(executable))
        {
            throw new InvalidOperationException(
                $"The downloaded build does not contain {Path.GetFileName(executable)}.");
        }

        return payload;
    }

    /// <summary>
    /// Starts the staged build and asks it to replace the installed one. Answers whether
    /// it started; the caller quits when it did.
    /// </summary>
    public static bool HandOver(string payload)
    {
        var handover = new UpdateHandover(InstallDirectory, Environment.ProcessId);
        var executable = Path.Combine(payload, Path.GetFileName(Environment.ProcessPath) ?? string.Empty);

        var start = new ProcessStartInfo(executable)
        {
            // From the folder it was unpacked into. A working directory inside the folder
            // about to be replaced would hold a handle on it and stop the swap.
            WorkingDirectory = payload,
            UseShellExecute = false,
        };

        foreach (var argument in handover.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var started = Process.Start(start);
            return started is not null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            DiagnosticLog.Write($"The downloaded macshot would not start: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Throws away everything staged for any release.
    /// </summary>
    /// <remarks>
    /// Called by the build that has just been started by an update: the folder it was
    /// copied from cannot be deleted by the process running out of it, so the app it
    /// starts is the first thing that can. Also called when a download fails, and it is
    /// best effort in both cases — a hundred and fifty megabytes left behind is worth
    /// nobody's error message.
    /// </remarks>
    public static void ClearStaging()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Remove(UpdateStaging.Root(local));
    }

    private static void Remove(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Write($"Could not clear '{folder}': {exception.Message}");
        }
    }

    /// <summary>
    /// Whether this process has a package identity, which is what being installed from an
    /// MSIX means.
    /// </summary>
    /// <remarks>
    /// Asked of the process rather than looked for on disk: the same files, run from the
    /// same path, are packaged or not depending on how they were started.
    /// </remarks>
    private static bool IsPackaged()
    {
        var length = 0u;
        return GetCurrentPackageFullName(ref length, null) != NoPackage;
    }

    /// <summary>
    /// Whether a file can be created in <paramref name="folder"/>, which is the only
    /// honest way to ask.
    /// </summary>
    /// <remarks>
    /// Reading the ACL and working it out would answer a different question — what the
    /// permissions say — and get it wrong for virtualization, for a folder on a network
    /// share, and for a read-only volume. Writing a byte and deleting it answers the
    /// question that is actually being asked.
    /// </remarks>
    private static bool CanWriteTo(string folder)
    {
        var probe = Path.Combine(folder, $".macshot-update-{Guid.NewGuid():N}");

        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint length, char[]? name);
}
