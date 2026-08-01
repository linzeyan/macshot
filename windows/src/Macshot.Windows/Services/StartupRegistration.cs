using Microsoft.Win32;

namespace Macshot.Windows.Services;

/// <summary>
/// Whether macshot starts with Windows.
/// </summary>
/// <remarks>
/// <para>
/// The per-user <c>Run</c> key, which is the answer for an unpackaged app. macOS uses
/// <c>SMAppService.mainApp</c> and Windows' nearest equivalent —
/// <c>Windows.ApplicationModel.StartupTask</c> — needs an MSIX identity this build does
/// not have. <c>HKEY_CURRENT_USER</c> rather than <c>HKEY_LOCAL_MACHINE</c>: starting
/// macshot for every account on the machine is not what the checkbox says.
/// </para>
/// <para>
/// Every operation is best-effort. Group policy can lock the key, and a screenshot tool
/// that refused to open its own settings window because it could not write an
/// autostart entry would be broken over something nobody asked for.
/// </para>
/// </remarks>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The value name, which is also what Task Manager's Startup tab shows.</summary>
    private const string EntryName = "macshot";

    /// <summary>Whether the entry is there and points at this build.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(EntryName) is string;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Adds or removes the entry, and says whether Windows allowed it.
    /// </summary>
    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                // Quoted, because the path leads through Program Files often enough that
                // an unquoted one would be read as a command and its first space.
                key.SetValue(EntryName, $"\"{Environment.ProcessPath}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(EntryName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception error) when (error is System.Security.SecurityException
            or UnauthorizedAccessException
            or IOException)
        {
            DiagnosticLog.Verbose($"launch at login: registry refused ({error.Message})");
            return false;
        }
    }
}
