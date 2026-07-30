using Macshot.Windows.Services;
using Microsoft.UI.Xaml;

namespace Macshot.Windows;

public partial class App : Application
{
    /// <summary>
    /// Held for the life of the process, and released by the process ending however
    /// it ends. Named per assembly, so the offline build and the normal one are
    /// separate apps and may run side by side.
    /// </summary>
    private static Mutex? _instanceLock;

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
        // A second macshot is never what was wanted, and it is easy to start one by
        // accident precisely because the first has no window to notice. Two of them
        // means two notification-area icons, and the second losing the fight for the
        // global shortcuts — which it would report as Windows refusing them, sending
        // the user to look for a conflict that is macshot itself.
        if (!TryClaimTheOnlyInstance())
        {
            // Written unconditionally rather than traced: a launch that ends in nothing
            // happening is exactly the report that arrives with no other evidence, and
            // by definition the user had no chance to turn tracing on first.
            DiagnosticLog.Write("A second macshot was started and refused.");
            FailureReport.Notice(
                IntPtr.Zero,
                "macshot is already running. Its icon is in the notification area, "
                    + "at the right-hand end of the taskbar.");
            Exit();
            return;
        }

        // No window at startup. macshot lives in the notification area and shows UI
        // only once the user asks for a capture.
        _controller = new CaptureController();
    }

    /// <summary>
    /// Answers whether this process is the first macshot, claiming that position when
    /// it is.
    /// </summary>
    /// <remarks>
    /// A named mutex rather than hunting for a window or a process by name: there is
    /// no window to find, and a name is something anything can be called. In the
    /// session namespace rather than <c>Global\</c>, so two people signed in to the
    /// same machine each get their own macshot.
    /// </remarks>
    private static bool TryClaimTheOnlyInstance()
    {
        var name = $@"Local\{typeof(App).Assembly.GetName().Name}.instance";
        _instanceLock = new Mutex(initiallyOwned: true, name, out var claimed);
        return claimed;
    }
}
