using Macshot.Windows.Core.Output;
using Microsoft.UI.Xaml;

namespace Macshot.Windows.Services;

/// <summary>
/// Puts macshot's ordinary windows into the light or dark the user chose.
/// </summary>
/// <remarks>
/// <para>
/// Applied per window rather than once for the application, because a WinUI window's
/// theme is a property of its content root and there is no application-wide switch that
/// reaches windows already on screen. Each window calls this as it opens, so a theme
/// changed in settings arrives with the next window rather than repainting the ones
/// already up — which is what macshot does too.
/// </para>
/// <para>
/// The capture toolbar is deliberately not among them. It is drawn over a screenshot
/// rather than inside a window, so following the system theme would make it disappear
/// into half the captures anyone takes; it keeps its own dark palette whatever this says.
/// </para>
/// </remarks>
internal static class AppThemes
{
    public static void Apply(Window window, AppTheme theme)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
    }
}
