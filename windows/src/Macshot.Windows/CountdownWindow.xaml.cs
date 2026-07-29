using System.Globalization;
using System.Runtime.InteropServices;
using Macshot.Windows.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace Macshot.Windows;

/// <summary>
/// The panel shown while a delayed capture counts down, and the wait itself.
/// </summary>
/// <remarks>
/// <para>
/// A delay exists for the things that cannot survive being clicked away from: an
/// open menu, a hover state, a drag in progress. All of them end the moment another
/// window takes the foreground — so this panel must appear over them without taking
/// it, which is what <c>WS_EX_NOACTIVATE</c> is for. Without that the countdown
/// destroys the very thing it is counting down to.
/// </para>
/// <para>
/// It is the counterpart of the macOS <c>CountdownView</c>.
/// </para>
/// </remarks>
public sealed partial class CountdownWindow : Window
{
    private const double WidthDips = 320;
    private const double HeightDips = 84;
    private const double MarginDips = 24;

    /// <summary>
    /// WDA_EXCLUDEFROMCAPTURE, as the recording panel uses. The screenshot is taken
    /// the instant the count reaches zero, with this window closing at the same
    /// moment — near enough for the compositor to still have it.
    /// </summary>
    private const uint ExcludeFromCapture = 0x11;

    private const int ExtendedStyle = -20;

    /// <summary>WS_EX_NOACTIVATE: never becomes the foreground window, not even on a click.</summary>
    private const long NoActivate = 0x08000000;

    /// <summary>WS_EX_TOOLWINDOW: keeps a panel this transient out of Alt+Tab.</summary>
    private const long ToolWindow = 0x00000080;

    /// <summary>
    /// Completed with whether the countdown ran out. Cancelling — the button, Escape,
    /// or the window being closed from under it — completes it false, and the caller
    /// takes no capture.
    /// </summary>
    private readonly TaskCompletionSource<bool> _finished =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly DispatcherQueueTimer _ticker;
    private int _remaining;

    public CountdownWindow()
    {
        InitializeComponent();

        _ticker = DispatcherQueue.CreateTimer();
        _ticker.Interval = TimeSpan.FromSeconds(1);
        _ticker.Tick += (_, _) => Tick();

        // Closed from the shell rather than by the countdown ending is a cancellation:
        // waiting on a window that no longer exists would never return.
        Closed += (_, _) => _finished.TrySetResult(false);
    }

    /// <summary>
    /// Counts down from <paramref name="seconds"/> and answers whether it ran out
    /// rather than being cancelled. The window is gone by the time this returns.
    /// </summary>
    public async Task<bool> RunAsync(int seconds, CancellationToken cancellation)
    {
        _remaining = Math.Max(1, seconds);
        SecondsText.Text = _remaining.ToString(CultureInfo.CurrentCulture);

        var handle = WindowNative.GetWindowHandle(this);
        SetWindowDisplayAffinity(handle, ExcludeFromCapture);

        // Set before the window is shown. Applying WS_EX_NOACTIVATE afterwards would
        // leave the one frame that steals the foreground, which is the whole failure.
        var style = GetWindowLongPtr(handle, ExtendedStyle).ToInt64();
        SetWindowLongPtr(handle, ExtendedStyle, new IntPtr(style | NoActivate | ToolWindow));

        var appWindow = this.GetAppWindow();
        appWindow.MakeChromeless().IsAlwaysOnTop = true;
        appWindow.MoveAndResize(PlaceBottomCentre());

        // Activate rather than AppWindow.Show, because a WinUI window does not render
        // its content until it has been activated once. WS_EX_NOACTIVATE is what makes
        // that safe: the window appears, and the foreground stays where it was.
        Activate();

        using var registration = cancellation.Register(() => _finished.TrySetResult(false));
        _ticker.Start();

        try
        {
            return await _finished.Task;
        }
        finally
        {
            _ticker.Stop();
            Close();
        }
    }

    private void Tick()
    {
        _remaining--;
        if (_remaining <= 0)
        {
            _finished.TrySetResult(true);
            return;
        }

        SecondsText.Text = _remaining.ToString(CultureInfo.CurrentCulture);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _finished.TrySetResult(false);

    /// <summary>
    /// Bottom centre of the primary display's work area, where the recording and
    /// scroll capture panels also sit: away from the corner a finished capture's
    /// thumbnail uses, and out of the middle of whatever is being arranged.
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
