#if !OFFLINE
using System.Diagnostics;
using System.Runtime.InteropServices;
using Macshot.Windows.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using WinRT.Interop;

namespace Macshot.Windows;

/// <summary>
/// The panel that says an upload is happening, then gives the link.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>UploadToastController</c>: 380 wide, at the top centre of the main
/// display, a spinner while it runs and then the link with an Open button beside it.
/// Dismissed by a click, or by itself after eight seconds — six when it is reporting a
/// failure, which is macshot's shorter wait for the message nobody needs to copy.
/// </para>
/// <para>
/// It is the only sign an upload happened at all. A capture is copied and saved by the
/// time this appears, so the toast is not confirming the capture — it is the sole
/// evidence of the one thing that left the machine.
/// </para>
/// <para>
/// Never takes the foreground. An upload is started from the overlay and finishes some
/// seconds later, by which time the user is typing somewhere else; a panel that stole
/// focus then would put their keystrokes into it.
/// </para>
/// </remarks>
public sealed partial class UploadToastWindow : Window
{
    /// <summary>macshot's width, and its distance below the top of the work area.</summary>
    private const double WidthDips = 380;

    private const double TopPaddingDips = 12;

    private const double MinimumHeightDips = 56;

    /// <summary>WS_EX_NOACTIVATE and WS_EX_TOOLWINDOW, as the recording panel uses them.</summary>
    private const int ExtendedStyle = -20;

    private const long NoActivate = 0x08000000;

    private const long ToolWindow = 0x00000080;

    /// <summary>The hairline as a COLORREF, matching the other panels this port draws.</summary>
    private const int HairlineColour = 0x00383838;

    private static readonly TimeSpan SuccessLinger = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan FailureLinger = TimeSpan.FromSeconds(6);

    private readonly DispatcherQueueTimer _dismiss;

    private double _scale = 1;
    private string? _link;
    private bool _closed;

    public UploadToastWindow()
    {
        InitializeComponent();
        this.Localize();

        _dismiss = DispatcherQueue.CreateTimer();
        _dismiss.IsRepeating = false;
        _dismiss.Tick += (_, _) => Dismiss();
    }

    /// <summary>Puts the panel up with a first line, and starts the spinner.</summary>
    public void ShowUploading(string status)
    {
        var display = MonitorEnumerator.Enumerate().Layout.Primary;
        _scale = display.Scale;

        StatusText.Text = status;

        var appWindow = this.GetAppWindow();
        appWindow.MakeChromeless().IsAlwaysOnTop = true;
        this.RoundCorners(HairlineColour);

        var handle = WindowNative.GetWindowHandle(this);

        // Before the window is shown. Applying WS_EX_NOACTIVATE afterwards still leaves
        // the one frame that steals the foreground, which here lands in the middle of
        // whatever the user went back to doing.
        var style = GetWindowLongPtr(handle, ExtendedStyle).ToInt64();
        SetWindowLongPtr(handle, ExtendedStyle, new IntPtr(style | NoActivate | ToolWindow));

        ShowAppIcon();
        Resize(MinimumHeightDips);

        // Activate rather than AppWindow.Show: a WinUI window does not render its content
        // until it has been activated once, and WS_EX_NOACTIVATE is what makes that safe.
        Activate();
    }

    /// <summary>Shows how far through the transfer is, as macshot's toast counts it.</summary>
    public void ShowProgress(double fraction)
    {
        var percent = (int)Math.Round(Math.Clamp(fraction, 0, 1) * 100);
        StatusText.Text = Localization.L("Uploading... %d%%").Replace("%d%%", percent + "%", StringComparison.Ordinal);
    }

    /// <summary>Shows the link, which is already on the clipboard by the time this runs.</summary>
    public void ShowSuccess(string link)
    {
        _link = link;

        Spinner.IsActive = false;
        Spinner.Visibility = Visibility.Collapsed;

        StatusText.Text = Localization.L("URL copied to the clipboard");
        LinkText.Text = link;
        LinkText.Visibility = Visibility.Visible;
        OpenButton.Visibility = Visibility.Visible;

        Resize(MeasuredHeight());
        _dismiss.Interval = SuccessLinger;
        _dismiss.Start();
    }

    /// <summary>Shows why there is no link.</summary>
    public void ShowFailure(string message)
    {
        Spinner.IsActive = false;
        Spinner.Visibility = Visibility.Collapsed;

        StatusText.Text = Localization.L("Upload failed: %@").Replace("%@", message, StringComparison.Ordinal);

        // The one red thing in any of this port's chrome. A failure that looked like the
        // success it replaced would be read as one at a glance, which is exactly how a
        // capture nobody uploaded gets pasted as a link that is not there.
        StatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 255, 69, 58));

        Resize(MeasuredHeight());
        _dismiss.Interval = FailureLinger;
        _dismiss.Start();
    }

    /// <summary>Takes the panel away, whoever asked.</summary>
    public void Dismiss()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _dismiss.Stop();
        Close();
    }

    private void ToastRoot_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // The Open button handles its own press and marks it handled, so a click that
        // reaches here is a click on the panel — which macshot treats as "go away".
        Dismiss();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_link is { } link && Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // No default browser, or one that refused to start. The link is on the
                // clipboard either way, which is the part that matters.
            }
        }

        Dismiss();
    }

    /// <summary>
    /// The app's own icon, which is what says whose notification this is. Best effort:
    /// the panel is worth showing without it.
    /// </summary>
    private void ShowAppIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "macshot.ico");
            if (File.Exists(path))
            {
                AppIcon.Source = new BitmapImage(new Uri(path));
                AppIcon.Visibility = Visibility.Visible;
            }
        }
        catch (Exception error) when (error is IOException or UriFormatException)
        {
        }
    }

    /// <summary>How tall the content wants to be, once it has been given the width.</summary>
    private double MeasuredHeight()
    {
        ToastRoot.Measure(new Windows.Foundation.Size(WidthDips, double.PositiveInfinity));
        return Math.Max(MinimumHeightDips, Math.Ceiling(ToastRoot.DesiredSize.Height) + 20);
    }

    /// <summary>
    /// Puts the panel at the top centre of the main display, at <paramref name="heightDips"/>.
    /// </summary>
    private void Resize(double heightDips)
    {
        var display = MonitorEnumerator.Enumerate().Layout.Primary;
        var work = display.WorkArea;

        var width = (int)Math.Round(WidthDips * _scale);
        var height = (int)Math.Round(heightDips * _scale);

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
#endif
