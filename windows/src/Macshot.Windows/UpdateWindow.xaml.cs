using System.Runtime.InteropServices;
using Macshot.Windows.Services;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace Macshot.Windows;

/// <summary>
/// The panel shown while an update is being fetched and put in place.
/// </summary>
/// <remarks>
/// <para>
/// A self-contained macshot is a hundred and fifty megabytes, so "install and restart"
/// is minutes of nothing on a slow connection and then the app disappearing and coming
/// back. Without something on screen saying so, the restart is indistinguishable from a
/// crash — which is the one impression an updater must not leave.
/// </para>
/// <para>
/// In both build variants, unlike the upload toast it is modelled on. The update check
/// runs in the offline build for the reason <see cref="UpdateService"/> gives, and a
/// check that can find an update but not show it being installed would be half a
/// feature.
/// </para>
/// </remarks>
public sealed partial class UpdateWindow : Window
{
    /// <summary>The upload toast's width, so the two panels are the same object.</summary>
    private const double WidthDips = 380;

    private const double TopPaddingDips = 12;

    private const double HeightDips = 64;

    private const int ExtendedStyle = -20;

    /// <summary>WS_EX_NOACTIVATE: an update is not a reason to take the foreground.</summary>
    private const long NoActivate = 0x08000000;

    /// <summary>WS_EX_TOOLWINDOW: keeps a panel this transient out of Alt+Tab.</summary>
    private const long ToolWindow = 0x00000080;

    private const int HairlineColour = 0x00383838;

    private double _scale = 1;
    private bool _closed;

    public UpdateWindow()
    {
        InitializeComponent();
        this.Localize();
    }

    /// <summary>Puts the panel up, saying what is about to happen.</summary>
    public void ShowStarting(string status)
    {
        var display = MonitorEnumerator.Enumerate().Layout.Primary;
        _scale = display.Scale;

        StatusText.Text = status;
        Bar.Value = 0;

        var appWindow = this.GetAppWindow();
        appWindow.MakeChromeless().IsAlwaysOnTop = true;
        this.RoundCorners(HairlineColour);

        var handle = WindowNative.GetWindowHandle(this);

        // Before the window is shown. Applying WS_EX_NOACTIVATE afterwards still leaves
        // the one frame that steals the foreground, which here lands in the middle of
        // whatever the user went back to doing while the download runs.
        var style = GetWindowLongPtr(handle, ExtendedStyle).ToInt64();
        SetWindowLongPtr(handle, ExtendedStyle, new IntPtr(style | NoActivate | ToolWindow));

        Place();

        // Activate rather than AppWindow.Show: a WinUI window does not render its content
        // until it has been activated once, and WS_EX_NOACTIVATE is what makes that safe.
        Activate();
    }

    /// <summary>How far through the download is.</summary>
    public void ShowProgress(string status, double fraction)
    {
        StatusText.Text = status;
        Bar.Value = Math.Clamp(fraction, 0, 1);
    }

    /// <summary>
    /// The last thing this panel says. The process is about to end and be started again,
    /// so nothing dismisses this — it goes when the window it lives in does.
    /// </summary>
    public void ShowRestarting(string status)
    {
        StatusText.Text = status;
        Bar.Value = 1;
    }

    public void Dismiss()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        Close();
    }

    private void Place()
    {
        var work = MonitorEnumerator.Enumerate().Layout.Primary.WorkArea;

        var width = (int)Math.Round(WidthDips * _scale);
        var height = (int)Math.Round(HeightDips * _scale);

        this.GetAppWindow().MoveAndResize(new RectInt32(
            (int)Math.Round(work.X + ((work.Width - width) / 2)),
            (int)Math.Round(work.Y + (TopPaddingDips * _scale)),
            width,
            height));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
