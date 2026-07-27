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
}
