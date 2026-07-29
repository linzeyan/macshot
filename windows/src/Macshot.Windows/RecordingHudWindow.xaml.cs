using System.Runtime.InteropServices;
using Macshot.Windows.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace Macshot.Windows;

/// <summary>
/// The panel shown while a recording runs: that one is running, how long it has been
/// going, and the way to end it.
/// </summary>
/// <remarks>
/// <para>
/// A recording leaves nothing on screen to say it is happening, and macshot has no
/// dock icon to notice. Without this panel the only way to know the desktop is being
/// recorded is to remember pressing the hotkey.
/// </para>
/// <para>
/// It is kept out of the recording itself with <c>WDA_EXCLUDEFROMCAPTURE</c>, so the
/// thing that says "you are recording" is not in the file afterwards.
/// </para>
/// </remarks>
public sealed partial class RecordingHudWindow : Window
{
    private const double WidthDips = 340;
    private const double HeightDips = 76;
    private const double MarginDips = 24;

    /// <summary>
    /// WDA_EXCLUDEFROMCAPTURE: the window still draws on screen, but the compositor
    /// leaves it out of anything capturing. WDA_MONITOR, the older value, would black
    /// the panel out on screen too.
    /// </summary>
    private const uint ExcludeFromCapture = 0x11;

    /// <summary>
    /// How long the panel stays up after the recording ends. Long enough to read
    /// where the file went, short enough to be gone before it is in the way.
    /// </summary>
    private static readonly TimeSpan SavedLinger = TimeSpan.FromSeconds(3);

    private readonly DispatcherQueueTimer _ticker;
    private DateTimeOffset _started;

    public RecordingHudWindow()
    {
        InitializeComponent();

        _ticker = DispatcherQueue.CreateTimer();
        _ticker.Interval = TimeSpan.FromSeconds(1);
        _ticker.Tick += (_, _) => StatusText.Text = $"Recording {Format(DateTimeOffset.UtcNow - _started)}";
    }

    /// <summary>Raised when the user asks for the recording to stop.</summary>
    public event EventHandler? StopRequested;

    /// <summary>Puts the panel on screen and starts counting.</summary>
    public void ShowHud()
    {
        var appWindow = this.GetAppWindow();
        appWindow.MakeChromeless().IsAlwaysOnTop = true;

        // Before the window is shown, so it is never in a frame at all — asking
        // afterwards would leave the first moment of the recording with a panel in it.
        SetWindowDisplayAffinity(WindowNative.GetWindowHandle(this), ExcludeFromCapture);

        appWindow.MoveAndResize(PlaceBottomCentre());

        _started = DateTimeOffset.UtcNow;
        _ticker.Start();
        Activate();
    }

    /// <summary>
    /// Says where the recording went, then closes itself.
    /// </summary>
    /// <remarks>
    /// A recording that simply stops and shows nothing is indistinguishable from one
    /// that failed. There is no thumbnail to fall back on the way a screenshot has,
    /// so the panel that said it was recording is what says it was saved.
    /// </remarks>
    public void ShowSaved(string fileName)
    {
        _ticker.Stop();
        StatusText.Text = $"Saved {fileName}";
        HintText.Text = Format(DateTimeOffset.UtcNow - _started);
        StopButton.IsEnabled = false;

        var linger = DispatcherQueue.CreateTimer();
        linger.Interval = SavedLinger;
        linger.IsRepeating = false;
        linger.Tick += (_, _) => Close();
        linger.Start();
    }

    /// <summary>
    /// Bottom centre of the primary display's work area, the same place the scroll
    /// capture panel takes: out of the corner a finished capture's thumbnail uses,
    /// and out of the middle of the screen.
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

    /// <summary>
    /// Minutes and seconds, growing an hours field only once there is one. A
    /// recording is usually under a minute and should not read like a stopwatch.
    /// </summary>
    private static string Format(TimeSpan elapsed)
    {
        return elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes}:{elapsed.Seconds:00}";
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke(this, EventArgs.Empty);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
}
