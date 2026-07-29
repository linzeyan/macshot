using Macshot.Windows.Services;
using Microsoft.UI.Xaml;

namespace Macshot.Windows;

public partial class App : Application
{
    private CaptureController? _controller;

    public App()
    {
        InitializeComponent();

        // Without this, WinUI ends the process when the last window closes, and macshot
        // spends almost all of its life with no window at all. Every overlay dismissed
        // was the process exiting: pressing Enter on a selection closed the overlays and
        // took the app down with them, mid-delivery, so the capture was never written.
        // Set here because it has to be set before the first window exists.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

        // A background app that vanishes tells the user nothing, and there is no log to
        // find afterwards. Handling the exception keeps the notification area icon alive
        // — the app may be in a state it should not continue from, but quitting is then
        // the user's decision, taken knowing what happened, rather than macshot simply
        // disappearing.
        UnhandledException += (_, args) =>
        {
            args.Handled = true;
            FailureReport.Show(IntPtr.Zero, args.Exception);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // No window at startup. macshot lives in the notification area and shows UI
        // only once the user asks for a capture.
        _controller = new CaptureController();
    }
}
