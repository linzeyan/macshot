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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
