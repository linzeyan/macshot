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
    /// <summary>Start the file over past this size, so it cannot grow without bound.</summary>
    private const long MaximumBytes = 256 * 1024;

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "macshot",
        "macshot.log");

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
                File.Delete(Path);
            }

            File.AppendAllText(Path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Deliberately silent: see the remarks. There is nowhere left to report to.
        }
    }
}
