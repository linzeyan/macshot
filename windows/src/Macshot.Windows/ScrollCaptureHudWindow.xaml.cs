using Macshot.Windows.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Macshot.Windows;

/// <summary>
/// The panel shown while a scroll capture runs: how far it has got, and the way to
/// end it early.
/// </summary>
/// <remarks>
/// <para>
/// A scroll capture is the one thing macshot does that takes over the desktop —
/// another window comes forward, the pointer moves, the wheel turns on its own.
/// Without something on screen saying so, that reads as the machine misbehaving.
/// </para>
/// <para>
/// It cannot live in the capture overlay, which is hidden for the duration: the
/// wheel goes to whatever sits under the pointer, and an always-on-top overlay
/// covering the desktop would take every notch itself.
/// </para>
/// </remarks>
public sealed partial class ScrollCaptureHudWindow : Window
{
    private const double WidthDips = 340;

    /// <summary>
    /// macshot's 36-tall bar with its 8 of padding, plus the second line this port
    /// carries and macshot does not — it has an auto-scroll toggle where this says how
    /// to stop, because this port scrolls by itself.
    /// </summary>
    private const double HeightDips = 52;

    private const double MarginDips = 24;

    public ScrollCaptureHudWindow()
    {
        InitializeComponent();
        // Every string in the XAML is already the English text macshot keys by,
        // so the page is translated in place rather than written twice.
        this.Localize();
    }

    /// <summary>Raised when the user asks for the capture to stop where it is.</summary>
    public event EventHandler? StopRequested;

    /// <summary>
    /// Puts the panel on screen.
    /// </summary>
    /// <remarks>
    /// Deliberately called before the capture takes the foreground. Windows only
    /// hands the foreground to a process that already has it, so macshot has to be
    /// the active app at the moment the target window is brought forward — showing
    /// this afterwards would both steal that foreground back and break the takeover
    /// that had just succeeded.
    /// </remarks>
    public void ShowHud()
    {
        var appWindow = this.GetAppWindow();
        appWindow.MakeChromeless().IsAlwaysOnTop = true;

        appWindow.MoveAndResize(PlaceBottomCentre());
        Activate();
    }

    /// <summary>Says how much has been captured so far.</summary>
    public void Report(int frames, int rows)
    {
        ProgressText.Text = Localization.L("Scrolling — {0} frames, {1} px tall", frames, rows);
    }

    /// <summary>
    /// Bottom centre of the primary display's work area. Away from the bottom right,
    /// where a finished capture's thumbnail goes, and away from the middle of the
    /// screen, which is where the pointer is parked driving the wheel.
    /// </summary>
    private static RectInt32 PlaceBottomCentre()
    {
        var monitor = MonitorEnumerator.Enumerate().Layout.Primary;
        var width = (int)(WidthDips * monitor.Scale);
        var height = (int)(HeightDips * monitor.Scale);
        var margin = (int)(MarginDips * monitor.Scale);

        return new RectInt32(
            (int)(monitor.WorkArea.X + ((monitor.WorkArea.Width - width) / 2)),
            (int)monitor.WorkArea.Bottom - height - margin,
            width,
            height);
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke(this, EventArgs.Empty);
}
