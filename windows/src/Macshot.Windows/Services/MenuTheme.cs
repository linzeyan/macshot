using System.Runtime.InteropServices;

using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Services;

/// <summary>
/// Puts the notification area's context menu into the light or dark the user chose.
/// </summary>
/// <remarks>
/// <para>
/// The tray menu is a real Win32 popup, the way macshot's is a real <c>NSMenu</c> — the
/// menu the shell puts up next to every other tray icon's, rather than a window drawn to
/// look like one. What it does not do on its own is follow the theme: a menu built with
/// <c>CreatePopupMenu</c> comes up white on a dark desktop and stays white, which is the
/// one place macshot's Windows build looks like it was written for a different decade.
/// </para>
/// <para>
/// The switch for it is <c>SetPreferredAppMode</c>, which is exported by uxtheme by
/// ordinal and by no name at all. Undocumented, so it is called through a guard and the
/// menu is left in the system's light if the call is not there — an old menu is worse
/// looking than a themed one, not broken.
/// </para>
/// <para>
/// Applied process-wide rather than per menu, because that is the only granularity the
/// call has. It is the same choice <see cref="AppThemes"/> applies to macshot's windows,
/// so the menu and the settings window opened from it agree.
/// </para>
/// </remarks>
internal static class MenuTheme
{
    /// <summary>
    /// uxtheme's own enumeration. <see cref="AllowDark"/> follows the system's app
    /// setting and changes with it; the two Force values do not.
    /// </summary>
    private enum PreferredAppMode
    {
        Default,
        AllowDark,
        ForceDark,
        ForceLight,
    }

    /// <summary>
    /// The last mode asked for, so the ordinals are not called again on every right
    /// click. Null once a call has failed, which is also what stops it being retried.
    /// </summary>
    private static AppTheme? _applied;

    private static bool _unavailable;

    public static void Apply(AppTheme theme)
    {
        if (_unavailable || _applied == theme)
        {
            return;
        }

        try
        {
            SetPreferredAppMode(theme switch
            {
                AppTheme.Dark => PreferredAppMode.ForceDark,
                AppTheme.Light => PreferredAppMode.ForceLight,

                // Not Default: that is uxtheme's "light, whatever the system says".
                _ => PreferredAppMode.AllowDark,
            });

            // Without this the change reaches menus created from here on and leaves the
            // ones the process has already themed alone, which on a theme switched in
            // settings is every menu that matters.
            FlushMenuThemes();

            _applied = theme;
        }
        catch (Exception exception) when (
            exception is EntryPointNotFoundException or DllNotFoundException)
        {
            _unavailable = true;
            DiagnosticLog.Write($"Menu theming unavailable: {exception.Message}");
        }
    }

    [DllImport("uxtheme.dll", EntryPoint = "#135", ExactSpelling = true)]
    private static extern PreferredAppMode SetPreferredAppMode(PreferredAppMode mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136", ExactSpelling = true)]
    private static extern void FlushMenuThemes();
}
