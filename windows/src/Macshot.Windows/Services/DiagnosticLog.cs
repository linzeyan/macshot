namespace Macshot.Windows.Services;

/// <summary>
/// Appends failures to a file next to the settings, so a fault that nobody saw
/// happen can still be read afterwards.
/// </summary>
/// <remarks>
/// <para>
/// macshot has no window most of the time and no console ever, so a message box is
/// the only thing standing between a failure and silence — and a message box that
/// nobody is sitting in front of, or that cannot be shown at all, leaves nothing
/// behind. The file is what makes a report survive the moment it happened.
/// </para>
/// <para>
/// Every operation is best-effort. Logging is a diagnostic aid, and an aid that can
/// take the app down with it — a locked file, a full disk — is worse than no aid.
/// </para>
/// </remarks>
public static class DiagnosticLog
{
    /// <summary>
    /// Roll the file over past this size, so it cannot grow without bound. Larger once
    /// tracing is on, because a single scroll capture writes a line per frame and a
    /// 256 KB ceiling would throw away the beginning of the run being diagnosed.
    /// </summary>
    private static long MaximumBytes => IsVerbose ? 8 * 1024 * 1024 : 256 * 1024;

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "macshot",
        "macshot.log");

    /// <summary>
    /// The previous file, kept across one roll-over.
    /// </summary>
    /// <remarks>
    /// Rolling over used to delete. That is the wrong behaviour precisely when the log
    /// matters: the run being diagnosed is the one that filled the file, and deleting
    /// it discards the beginning of the very thing being looked for.
    /// </remarks>
    public static string PreviousPath => Path + ".1";

    /// <summary>
    /// The folder holding both the log and <c>settings.json</c>, for the button that
    /// opens it.
    /// </summary>
    public static string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    /// <summary>
    /// Whether <see cref="Verbose"/> writes anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A static rather than a parameter threaded through every call site. Tracing is a
    /// cross-cutting concern that has to be reachable from the P/Invoke layer as easily
    /// as from a click handler, and passing the settings into <c>WindowEnumerator</c>
    /// to decide whether to write a line would be worse than the problem.
    /// </para>
    /// <para>
    /// Off by default. Everything worth tracing is on a path that runs at pointer-move
    /// rates, and a log the user did not ask for is a file that grows on their disk.
    /// </para>
    /// </remarks>
    public static bool IsVerbose { get; set; }

    /// <summary>
    /// Records a step, when tracing is on. Costs a branch when it is off.
    /// </summary>
    /// <remarks>
    /// This is what makes a fault on unverified hardware locatable: a failure reported
    /// as "nothing happened" is a session lost to guessing, where the same failure with
    /// the last twenty steps written down is one line to read. Use it for the facts a
    /// failure would be diagnosed from — which display, which backend, what size, which
    /// branch — and never for a secret.
    /// </remarks>
    public static void Verbose(string message)
    {
        if (IsVerbose)
        {
            Write("trace  " + message);
        }
    }

    public static void Write(string message)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(Path) && new FileInfo(Path).Length > MaximumBytes)
            {
                // Moved rather than deleted, overwriting only the copy from the roll
                // before. Two files' worth is enough to hold a run that overflowed
                // while it was being traced.
                File.Move(Path, PreviousPath, overwrite: true);
            }

            File.AppendAllText(Path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Deliberately silent: see the remarks. There is nowhere left to report to.
        }
    }
}
