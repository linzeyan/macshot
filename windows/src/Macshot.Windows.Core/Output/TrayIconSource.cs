namespace Macshot.Windows.Core.Output;

/// <summary>
/// Where the notification-area icon comes from.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>statusBarIconMode</c>, which offers its own icon or an SF Symbol named by
/// the user. Windows has no symbol set the shell draws tray icons from — what the
/// notification area takes is an icon file — so the custom half is a file the user picks
/// rather than a name they type.
/// </para>
/// <para>
/// That difference is not only in how it is chosen. macOS renders a symbol as a template,
/// so it takes the menu bar's own colour and stays legible in light and dark alike; an
/// icon file is a fixed picture, and one drawn for a light taskbar disappears into a dark
/// one. Nothing here can fix that, so the settings page says it instead.
/// </para>
/// </remarks>
public enum TrayIconSource
{
    /// <summary>macshot's own icon.</summary>
    Default,

    /// <summary>The icon file the user chose.</summary>
    Custom,
}
