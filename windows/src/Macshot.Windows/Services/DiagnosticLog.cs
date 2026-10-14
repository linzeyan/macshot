using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Macshot.Windows.Core.Output;

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
/// Every line names the file and line that wrote it, and every exception is written with
/// its stack. A report is read against source that has moved on since, so a message alone
/// — "Could not save" — leaves the reader searching for which of a dozen statements in
/// the try block threw; the location and the stack are what end the search.
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
    private static long MaximumBytes => Level > LogLevel.Normal ? 8 * 1024 * 1024 : 256 * 1024;

    /// <summary>
    /// Recording threads, the frame pool and the UI thread all write here. Unserialized,
    /// two appends at once collide on the file and one of them is silently lost — and the
    /// lines lost are the ones written at the moment two things were happening at once.
    /// </summary>
    private static readonly object Gate = new();

    /// <summary>
    /// Set while this thread is inside <see cref="Append"/>, so an exception thrown by the
    /// logging itself — a locked file — cannot reach <see cref="OnFirstChanceException"/>
    /// and log itself, forever.
    /// </summary>
    [ThreadStatic]
    private static bool _appending;

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
    /// How much <see cref="Debug"/> and <see cref="Trace"/> write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A static rather than a parameter threaded through every call site. Tracing is a
    /// cross-cutting concern that has to be reachable from the P/Invoke layer as easily
    /// as from a click handler, and passing the settings into <c>WindowEnumerator</c>
    /// to decide whether to write a line would be worse than the problem.
    /// </para>
    /// <para>
    /// <see cref="LogLevel.Normal"/> by default. Everything worth tracing is on a path
    /// that runs at pointer-move rates, and a log the user did not ask for is a file that
    /// grows on their disk.
    /// </para>
    /// </remarks>
    public static LogLevel Level { get; set; }

    /// <summary>
    /// Records a step, at <see cref="LogLevel.Debug"/> and above. Costs a branch below it.
    /// </summary>
    /// <remarks>
    /// This is what makes a fault on unverified hardware locatable: a failure reported
    /// as "nothing happened" is a session lost to guessing, where the same failure with
    /// the last twenty steps written down is one line to read. Use it for the facts a
    /// failure would be diagnosed from — which display, which backend, what size, which
    /// branch, how long — and never for a secret.
    /// </remarks>
    public static void Debug(
        string message,
        Exception? exception = null,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (Level >= LogLevel.Debug)
        {
            Append("debug", message, exception, file, line);
        }
    }

    /// <summary>
    /// Records something that happens per frame or per pointer move, at
    /// <see cref="LogLevel.Trace"/> only.
    /// </summary>
    public static void Trace(
        string message,
        Exception? exception = null,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (Level >= LogLevel.Trace)
        {
            Append("trace", message, exception, file, line);
        }
    }

    /// <summary>Records a failure, or a one-off fact a failure is read against.</summary>
    public static void Write(
        string message,
        Exception? exception = null,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0) =>
        Append("info", message, exception, file, line);

    /// <summary>
    /// Records how long something the user was waiting on took: at
    /// <see cref="LogLevel.Normal"/> once the wait was long enough to notice, and as a
    /// step otherwise.
    /// </summary>
    /// <remarks>
    /// Written without being asked for once it is slow, because a slow capture is not
    /// something anyone can turn tracing on for in advance: it is reported after the fact,
    /// once, and the run it happened in is the only one that knows where the time went.
    /// </remarks>
    public static void Took(
        TimeSpan elapsed,
        string message,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (elapsed >= TimeSpan.FromSeconds(1))
        {
            Append("slow", message, null, file, line);
        }
        else
        {
            Debug(message, null, file, line);
        }
    }

    /// <summary>
    /// The first line of every run: what is running, so the line numbers after it can be
    /// read against the source they came from.
    /// </summary>
    /// <remarks>
    /// The informational version untrimmed, because a source-linked build appends the
    /// commit after the '+', and a line number is only an address in one commit.
    /// </remarks>
    public static void Started(string variant)
    {
        var version = typeof(DiagnosticLog).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        Append(
            "info",
            $"{variant} {version} starting on Windows {Environment.OSVersion.Version} "
                + $"{RuntimeInformation.ProcessArchitecture}, logging at {Level}",
            null,
            string.Empty,
            0);
    }

    /// <summary>
    /// Writes every exception at the moment it is thrown, at <see cref="LogLevel.Trace"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ones nobody logs are the reason. A <c>catch</c> that falls back — the older
    /// capture backend, a probe that answers "not supported" — turns a fault into a
    /// behaviour, and twice in this app a swallowed exception read as a platform refusing
    /// something when it was a bug in the code that asked. This is the one place that sees
    /// them all.
    /// </para>
    /// <para>
    /// With the whole stack rather than the exception's own, which at this moment holds
    /// only the frame that threw — and when that frame is inside the runtime, it names no
    /// line of macshot's at all.
    /// </para>
    /// </remarks>
    public static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (Level < LogLevel.Trace || _appending)
        {
            return;
        }

        var exception = args.Exception;
        Append(
            "trace",
            $"thrown: 0x{exception.HResult:X8} {exception.GetType().FullName}: {exception.Message}"
                + Environment.NewLine
                + new StackTrace(1, fNeedFileInfo: true),
            null,
            string.Empty,
            0);
    }

    private static void Append(string level, string message, Exception? exception, string file, int line)
    {
        if (_appending)
        {
            return;
        }

        _appending = true;
        try
        {
            // The file name alone: the compiler records the path the build machine had,
            // which says nothing to anyone reading this and is long enough to push the
            // message off the screen.
            var location = file.Length > 0 ? $"{System.IO.Path.GetFileName(file)}:{line}" : "-";

            // The HRESULT beside the exception's own text, because the COM and WinRT
            // failures this app meets are told apart by it — RPC_E_WRONG_THREAD and
            // E_ACCESSDENIED both arrive as an unhelpful sentence.
            var text = exception is null
                ? message
                : $"{message}: 0x{exception.HResult:X8} {exception}";

            var entry = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}  {level,-5}  "
                + $"#{Environment.CurrentManagedThreadId,-3} {location}  {text}{Environment.NewLine}";

            lock (Gate)
            {
                // Qualified, and so is every other BCL file API in this class: the members
                // below are named Path and Directory, which shadow the types of the same
                // name inside it. The names are worth keeping — they are what a caller
                // wants to read — so the cost is paid here.
                if (!string.IsNullOrEmpty(Directory))
                {
                    System.IO.Directory.CreateDirectory(Directory);
                }

                if (File.Exists(Path) && new FileInfo(Path).Length > MaximumBytes)
                {
                    // Moved rather than deleted, overwriting only the copy from the roll
                    // before. Two files' worth is enough to hold a run that overflowed
                    // while it was being traced.
                    File.Move(Path, PreviousPath, overwrite: true);
                }

                File.AppendAllText(Path, entry);
            }
        }
        catch (Exception)
        {
            // Deliberately silent: see the remarks. There is nowhere left to report to.
        }
        finally
        {
            _appending = false;
        }
    }
}
