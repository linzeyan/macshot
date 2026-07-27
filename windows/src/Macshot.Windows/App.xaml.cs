using Microsoft.UI.Xaml;

namespace Macshot.Windows;

public partial class App : Application
{
    private CaptureController? _controller;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // No window at startup. macshot lives in the notification area and shows UI
        // only once the user asks for a capture, so the controller, not a window, is
        // what keeps the process alive.
        _controller = new CaptureController();
    }
}
