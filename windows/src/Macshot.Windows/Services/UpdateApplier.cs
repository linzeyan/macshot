using System.Diagnostics;
using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Services;

/// <summary>
/// Replaces the installed macshot with this one, and starts what it wrote.
/// </summary>
/// <remarks>
/// <para>
/// This runs in the <em>downloaded</em> build, started by the installed one with
/// <see cref="UpdateHandover"/>. It has to be that way round: a program cannot overwrite
/// the folder it is running from, so somebody else must do it, and the new build is the
/// better somebody — the copying is then done by the code that shipped with the update,
/// and the downloaded executable has been proved to start before anything is replaced.
/// </para>
/// <para>
/// The installed folder is moved aside rather than written over. A copy that fails
/// halfway through writing over the old files leaves a folder that is neither version and
/// starts as neither; moving it first means the failure has somewhere to be undone from.
/// </para>
/// </remarks>
internal static class UpdateApplier
{
    /// <summary>
    /// How long to wait for the macshot that asked to end.
    /// </summary>
    /// <remarks>
    /// It quits as soon as it has started this, so the wait is normally a fraction of a
    /// second. The limit is for the case where it does not — a modal message box left
    /// open, a hung dispatcher — where giving up is right: the copy would fail on an open
    /// file anyway, and the installed macshot is still there and still works.
    /// </remarks>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long to keep asking for the installed folder after it is refused.
    /// </summary>
    /// <remarks>
    /// A process having exited does not mean Windows has finished with it: the image
    /// sections behind its executable and its two hundred assemblies are released
    /// afterwards, and the virus scanner is at the same moment part-way through the
    /// hundred and fifty megabytes that were just unpacked. Measured on the test machine,
    /// the move was denied one second after the old macshot ended and allowed a moment
    /// later. Without this the first attempt is the only attempt, and it lands inside both
    /// windows.
    /// </remarks>
    private static readonly TimeSpan Insistence = TimeSpan.FromSeconds(20);

    /// <summary>How long to leave between attempts.</summary>
    private static readonly TimeSpan Breath = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Carries <paramref name="handover"/> out. Answers nothing: this process ends either
    /// way, having started one macshot or the other.
    /// </summary>
    public static void Apply(UpdateHandover handover)
    {
        var target = handover.TargetDirectory;
        var payload = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var executable = Path.GetFileName(Environment.ProcessPath) ?? "Macshot.Windows.exe";

        DiagnosticLog.Write($"applying an update: '{payload}' over '{target}'");

        if (!WaitFor(handover.WaitForProcessId))
        {
            Give(target, executable, "macshot did not quit, so the update was not installed.");
            return;
        }

        var moved = $"{target}.replaced-{Environment.ProcessId}";

        if (MoveAside(target, moved) is { } refusal)
        {
            // Nothing has been touched, so there is nothing to undo — the installed
            // macshot is exactly as it was.
            Give(target, executable, $"macshot's folder could not be replaced: {refusal}");
            return;
        }

        try
        {
            Copy(payload, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Restore(target, moved);
            Give(target, executable, $"The update could not be written: {exception.Message}");
            return;
        }

        // Only once the new one is known to be complete. Until here the old folder is the
        // way back.
        Remove(moved);

        DiagnosticLog.Write("update installed");
        Start(Path.Combine(target, executable), []);
    }

    /// <summary>
    /// Renames the installed folder out of the way, insisting for
    /// <see cref="Insistence"/>. Answers null when it moved, and otherwise why it would
    /// not.
    /// </summary>
    private static string? MoveAside(string target, string moved)
    {
        var trying = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                Directory.Move(target, moved);
                return null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (trying.Elapsed >= Insistence)
                {
                    return exception.Message;
                }

                Thread.Sleep(Breath);
            }
        }
    }

    /// <summary>
    /// Waits for the macshot that asked for this to end, and answers whether it did.
    /// </summary>
    /// <remarks>
    /// A process that has already gone is the ordinary case rather than an error: between
    /// this process starting and reaching here, the one that started it has usually
    /// finished quitting.
    /// </remarks>
    private static bool WaitFor(int processId)
    {
        try
        {
            using var waiting = Process.GetProcessById(processId);
            return waiting.WaitForExit((int)Patience.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    /// <summary>
    /// Puts the old folder back after a failed copy.
    /// </summary>
    /// <remarks>
    /// The partly written new folder goes first, because the move cannot land on a name
    /// that exists. If that fails there is nothing further to try, and the message the
    /// caller shows is what tells the user where their macshot went — which is why it
    /// names the folder.
    /// </remarks>
    private static void Restore(string target, string moved)
    {
        Remove(target);

        try
        {
            Directory.Move(moved, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Write(
                $"The update failed and macshot could not be put back: it is at '{moved}'. {exception.Message}");
        }
    }

    /// <summary>
    /// Says what went wrong and starts whichever macshot is at
    /// <paramref name="directory"/>, so a failed update leaves the user with a working
    /// program rather than with nothing.
    /// </summary>
    private static void Give(string directory, string executable, string message)
    {
        DiagnosticLog.Write($"the update was not installed: {message}");
        FailureReport.Notice(IntPtr.Zero, Localization.L("The update could not be installed.") + Environment.NewLine + message);
        Start(Path.Combine(directory, executable), []);
    }

    private static void Start(string executable, IReadOnlyList<string> arguments)
    {
        if (!File.Exists(executable))
        {
            DiagnosticLog.Write($"there is no macshot at '{executable}' to start");
            return;
        }

        var start = new ProcessStartInfo(executable)
        {
            // Not the folder this process is running from: that one is about to be deleted
            // by the macshot being started, and a working directory holds a handle on it.
            WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var started = Process.Start(start);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            DiagnosticLog.Write($"macshot would not start after the update: {exception.Message}");
        }
    }

    /// <summary>
    /// Copies a folder and everything under it. Written out because there is no framework
    /// method for it and the alternative — a shell copy — puts a progress dialog on screen
    /// that this already has a panel for.
    /// </summary>
    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (var file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var folder in Directory.EnumerateDirectories(from))
        {
            Copy(folder, Path.Combine(to, Path.GetFileName(folder)));
        }
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
            DiagnosticLog.Write($"Could not remove '{folder}': {exception.Message}");
        }
    }
}
