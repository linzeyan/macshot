using System.Globalization;
using System.Runtime.InteropServices;
using Macshot.Windows.Core.Capture;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;
using Macshot.Windows.Services;

namespace Macshot.Windows;

/// <summary>
/// The panel shown while a recording runs: that one is running, how long it has been
/// going, and the ways to hold or end it. The counterpart of macshot's
/// <c>RecordingHUDPanel</c>, in its 164 × 32.
/// </summary>
/// <remarks>
/// <para>
/// A recording leaves nothing on screen to say it is happening, and macshot has no dock
/// icon to notice. Without this panel the only way to know the desktop is being recorded
/// is to remember pressing the hotkey.
/// </para>
/// <para>
/// It is kept out of the recording itself with <c>WDA_EXCLUDEFROMCAPTURE</c>, so the
/// thing that says "you are recording" is not in the file afterwards, and it never takes
/// the foreground: a panel that stole focus from the window being demonstrated would
/// change the recording by appearing.
/// </para>
/// </remarks>
public sealed partial class RecordingHudWindow : Window
{
    /// <summary>macshot's pill, which never changes size while the recording runs.</summary>
    private const double WidthDips = 164;

    private const double HeightDips = 32;

    /// <summary>What it grows to for the three seconds that name the file.</summary>
    private const double SavedWidthDips = 340;

    /// <summary>
    /// WDA_EXCLUDEFROMCAPTURE: the window still draws on screen, but the compositor
    /// leaves it out of anything capturing. WDA_MONITOR, the older value, would black the
    /// panel out on screen too.
    /// </summary>
    private const uint ExcludeFromCapture = 0x11;

    private const int ExtendedStyle = -20;

    /// <summary>WS_EX_NOACTIVATE: never becomes the foreground window, not even on a click.</summary>
    private const long NoActivate = 0x08000000;

    /// <summary>WS_EX_TOOLWINDOW: keeps a panel this transient out of Alt+Tab.</summary>
    private const long ToolWindow = 0x00000080;

    /// <summary>The hairline as a COLORREF: macshot's icon colour at 10% over the pill.</summary>
    private const int HairlineColour = 0x00383838;

    private static readonly Color Recording = Color.FromArgb(255, 255, 59, 48);

    /// <summary>macshot's dark-appearance orange, the one the snap line uses too.</summary>
    private static readonly Color Held = Color.FromArgb(255, 255, 159, 10);

    /// <summary>
    /// How long the panel stays up after the recording ends. Long enough to read where
    /// the file went, short enough to be gone before it is in the way.
    /// </summary>
    private static readonly TimeSpan SavedLinger = TimeSpan.FromSeconds(3);

    private readonly DispatcherQueueTimer _ticker;

    private double _scale = 1;
    private DateTimeOffset _started;

    /// <summary>How long the recording has been held, which the reading leaves out.</summary>
    private TimeSpan _heldFor;

    private DateTimeOffset? _heldSince;

    private CursorPoint? _dragCursorOrigin;
    private PointInt32 _dragWindowOrigin;

    public RecordingHudWindow()
    {
        InitializeComponent();
        // Every string in the XAML is already the English text macshot keys by,
        // so the page is translated in place rather than written twice.
        this.Localize();

        _ticker = DispatcherQueue.CreateTimer();
        _ticker.Interval = TimeSpan.FromSeconds(1);
        _ticker.Tick += (_, _) => ElapsedText.Text = Format(Elapsed);
    }

    /// <summary>Raised when the user asks for the recording to stop.</summary>
    public event EventHandler? StopRequested;

    /// <summary>Raised with whether the recording should now be held.</summary>
    public event EventHandler<bool>? PauseToggled;

    /// <summary>What the reading shows: the wall clock, less any time held.</summary>
    private TimeSpan Elapsed => DateTimeOffset.UtcNow - _started - _heldFor
        - (_heldSince is { } since ? DateTimeOffset.UtcNow - since : TimeSpan.Zero);

    /// <summary>
    /// Puts the panel against <paramref name="region"/> and starts counting.
    /// </summary>
    /// <param name="region">
    /// What is being recorded, in virtual-screen pixels — the display's own bounds when
    /// the whole display is.
    /// </param>
    /// <param name="display">The display it is on, for the work area and the scale.</param>
    public void ShowHud(CaptureRegion region, CaptureMonitor display)
    {
        ArgumentNullException.ThrowIfNull(display);

        _scale = display.Scale;

        var appWindow = this.GetAppWindow();
        appWindow.MakeChromeless().IsAlwaysOnTop = true;
        this.RoundCorners(HairlineColour);

        var handle = WindowNative.GetWindowHandle(this);

        // Before the window is shown, so it is never in a frame at all — asking
        // afterwards would leave the first moment of the recording with a panel in it.
        SetWindowDisplayAffinity(handle, ExcludeFromCapture);

        // Also before, for the reason the countdown does it: applying WS_EX_NOACTIVATE
        // afterwards still leaves the one frame that steals the foreground, and here that
        // frame is the beginning of the recording.
        var style = GetWindowLongPtr(handle, ExtendedStyle).ToInt64();
        SetWindowLongPtr(handle, ExtendedStyle, new IntPtr(style | NoActivate | ToolWindow));

        appWindow.MoveAndResize(Place(region, display, WidthDips));

        _started = DateTimeOffset.UtcNow;
        _ticker.Start();

        // Activate rather than AppWindow.Show, because a WinUI window does not render its
        // content until it has been activated once. WS_EX_NOACTIVATE is what makes that
        // safe: the panel appears, and the foreground stays where it was.
        Activate();
    }

    /// <summary>
    /// Says where the recording went, then closes itself.
    /// </summary>
    /// <remarks>
    /// A recording that simply stops and shows nothing is indistinguishable from one that
    /// failed. There is no thumbnail to fall back on the way a screenshot has, so the
    /// panel that said it was recording is what says it was saved — and it may grow to
    /// fit the name, now that there is no recording left for a moving panel to disturb.
    /// </remarks>
    public void ShowSaved(string fileName)
    {
        _ticker.Stop();

        SavedText.Text = Localization.L("Saved {0}", fileName);
        SavedDuration.Text = Format(Elapsed);
        RunningLayer.Visibility = Visibility.Collapsed;
        SavedLayer.Visibility = Visibility.Visible;

        var appWindow = this.GetAppWindow();
        var at = appWindow.Position;
        appWindow.MoveAndResize(new RectInt32(
            // Leftwards: the panel is right-aligned to what was recorded, and growing
            // rightwards would walk it off the screen edge it was clamped to.
            at.X - (int)((SavedWidthDips - WidthDips) * _scale),
            at.Y,
            (int)(SavedWidthDips * _scale),
            (int)(HeightDips * _scale)));

        var linger = DispatcherQueue.CreateTimer();
        linger.Interval = SavedLinger;
        linger.IsRepeating = false;
        linger.Tick += (_, _) => Close();
        linger.Start();
    }

    private static RectInt32 Place(CaptureRegion region, CaptureMonitor display, double widthDips)
    {
        var size = new CaptureRegion(0, 0, widthDips * display.Scale, HeightDips * display.Scale);
        var placed = HudPlacement.For(region, display.WorkArea, size);

        return new RectInt32((int)placed.X, (int)placed.Y, (int)placed.Width, (int)placed.Height);
    }

    /// <summary>
    /// Minutes and seconds, growing an hours field only once there is one. The minutes
    /// are padded, as macshot's are, so the reading never changes width as it passes ten.
    /// </summary>
    private static string Format(TimeSpan elapsed)
    {
        return elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private void Stop_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Handled, or the press underneath it starts dragging the panel.
        e.Handled = true;
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Pause_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;

        // Matched rather than tested against a bool taken first: the compiler cannot
        // carry "holding is false, so the field is not null" from a local into a field
        // access, and .Value on a field it has not proved is exactly the shape of a
        // crash that only happens once.
        bool holding;
        if (_heldSince is { } heldSince)
        {
            _heldFor += DateTimeOffset.UtcNow - heldSince;
            _heldSince = null;
            holding = false;
        }
        else
        {
            _heldSince = DateTimeOffset.UtcNow;
            holding = true;
        }

        PauseBars.Visibility = holding ? Visibility.Collapsed : Visibility.Visible;
        ResumeTriangle.Visibility = holding ? Visibility.Visible : Visibility.Collapsed;
        RecordDot.Fill = new SolidColorBrush(holding ? Held : Recording);
        ElapsedText.Text = Format(Elapsed);

        PauseToggled?.Invoke(this, holding);
    }

    /// <summary>
    /// Drags the panel, which macshot's can be because it is placed against the region
    /// being recorded — and sometimes that is exactly where the interesting part is.
    /// </summary>
    /// <remarks>
    /// Tracked in screen pixels rather than in the window's layout units, as the pin's
    /// drag is: dragging across a display boundary changes the scale that would convert
    /// them, mid-gesture.
    /// </remarks>
    private void HudRoot_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(HudRoot).Properties.IsLeftButtonPressed || !GetCursorPos(out var cursor))
        {
            return;
        }

        _dragCursorOrigin = cursor;
        _dragWindowOrigin = this.GetAppWindow().Position;
        HudRoot.CapturePointer(e.Pointer);
    }

    private void HudRoot_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragCursorOrigin is not { } origin || !GetCursorPos(out var cursor))
        {
            return;
        }

        this.GetAppWindow().Move(new PointInt32(
            _dragWindowOrigin.X + cursor.X - origin.X,
            _dragWindowOrigin.Y + cursor.Y - origin.Y));
    }

    private void HudRoot_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragCursorOrigin = null;
        HudRoot.ReleasePointerCaptures();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }
}
