using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Macshot.Windows;

internal static class WindowExtensions
{
    public static AppWindow GetAppWindow(this Window window)
    {
        var windowId = Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(window));
        return AppWindow.GetFromWindowId(windowId);
    }

    /// <summary>
    /// Strips the border and title bar, and answers the presenter that did it so the
    /// caller can go on to say whether the window floats or resizes.
    /// </summary>
    /// <remarks>
    /// The presenter has to be created and set rather than cast to: a window WinUI
    /// made reports the default presenter, which is not an
    /// <see cref="OverlappedPresenter"/>, so <c>Presenter is OverlappedPresenter</c>
    /// silently matches nothing. Every window macshot puts on the screen was written
    /// that way, which is why the capture overlay opened full-screen, correct, and
    /// underneath the window it was meant to be covering.
    /// </remarks>
    public static OverlappedPresenter MakeChromeless(this AppWindow appWindow)
    {
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        appWindow.SetPresenter(presenter);
        return presenter;
    }

    /// <summary>
    /// The macshot icon for a titled window's title bar, Alt+Tab entry, and taskbar
    /// button.
    /// </summary>
    /// <remarks>
    /// An unpackaged WinUI window does not pick up the executable's icon on its own, so
    /// without this every macshot window is the generic placeholder in Alt+Tab. Silent
    /// on failure: a window with the wrong icon still works, and there is nothing the
    /// user could do about it.
    /// </remarks>
    public static void UseAppIcon(this AppWindow appWindow)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "macshot.ico");
            if (File.Exists(path))
            {
                appWindow.SetIcon(path);
            }
        }
        catch (Exception)
        {
            // Cosmetic, and already reported once by the tray icon if the file is gone.
        }
    }

    /// <summary>
    /// Puts the window in front and gives it the keyboard.
    /// </summary>
    /// <remarks>
    /// <see cref="Window.Activate"/> on its own asks the shell politely, and the shell
    /// refuses a process that is not already the foreground one — which macshot never
    /// is, since it is summoned by a hotkey from whatever the user was doing. Without
    /// this the overlay is on screen but every key still goes to the app behind it.
    /// </remarks>
    public static void TakeForeground(this Window window)
    {
        window.Activate();
        SetForegroundWindow(WindowNative.GetWindowHandle(window));
    }

    /// <summary>
    /// Rounds a chromeless window's corners, and optionally puts a hairline round it —
    /// the two things that tell a floating panel apart from the screen behind it.
    /// </summary>
    /// <param name="hairline">
    /// A COLORREF (0x00BBGGRR) for the border, or null to leave the window unbordered.
    /// </param>
    /// <remarks>
    /// DWM's, not drawn in the content: a WinUI window has no per-pixel transparency, so
    /// a rounded border inside it would leave the window's own square corners showing
    /// through behind the curve. That costs the exact radius — Windows' rather than
    /// macshot's — and any alpha in the hairline, since the attribute takes none.
    /// Windows 10 has neither attribute and fails harmlessly, leaving a square window.
    /// </remarks>
    public static void RoundCorners(this Window window, int? hairline = null)
    {
        var handle = WindowNative.GetWindowHandle(window);

        var rounded = CornerPreferenceRound;
        DwmSetWindowAttribute(handle, CornerPreference, ref rounded, sizeof(int));

        if (hairline is { } colour)
        {
            DwmSetWindowAttribute(handle, BorderColour, ref colour, sizeof(int));
        }
    }

    /// <summary>DWMWA_WINDOW_CORNER_PREFERENCE, Windows 11 and later.</summary>
    private const int CornerPreference = 33;

    /// <summary>DWMWCP_ROUND: the radius Windows rounds an ordinary window with.</summary>
    private const int CornerPreferenceRound = 2;

    /// <summary>DWMWA_BORDER_COLOR, Windows 11 and later.</summary>
    private const int BorderColour = 34;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
