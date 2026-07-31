using System.Runtime.InteropServices;
using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Services;
using Macshot.Windows.Toolbar;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace Macshot.Windows;

/// <summary>
/// A capture kept on top of everything else, the counterpart of the macOS
/// <c>PinWindowController</c>.
/// </summary>
/// <remarks>
/// It opens in the middle of the display it came from rather than over the pixels it was
/// cut from, where it would sit invisibly on top of the thing it is a copy of, and it is
/// scaled by the wheel — see <see cref="PinPlacement"/>. It has no title bar, so dragging
/// is handled here.
/// </remarks>
public sealed partial class PinWindow : Window
{
    /// <summary>
    /// How much one wheel notch changes the scale. macshot's own step for a mouse, which
    /// is small on purpose: the pin is being sized to fit beside something, not paged
    /// through.
    /// </summary>
    private const double WheelStep = 0.03;

    /// <summary>What Windows reports for one notch. A precision touchpad sends less.</summary>
    private const double WheelNotch = 120;

    private readonly CapturedFrame _frame;
    private readonly SettingsStore _settings;

    /// <summary>The window at scale 1, which every later size is a multiple of.</summary>
    private CaptureRegion _opening;

    private CursorPoint? _dragCursorOrigin;
    private PointInt32 _dragWindowOrigin;

    public PinWindow(CapturedFrame frame, SettingsStore settings)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        InitializeComponent();

        EditButton.Child = Glyph(ToolbarCommand.OpenEditor);
        CloseButton.Child = Glyph(ToolbarCommand.Cancel);
    }

    /// <summary>Raised with the capture the user wants opened in the editor.</summary>
    public event EventHandler<CapturedFrame>? EditRequested;

    public async Task ShowPinnedAsync()
    {
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(_frame.ToSoftwareBitmap());
        PinImage.Source = source;

        var appWindow = this.GetAppWindow();
        var presenter = appWindow.MakeChromeless();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        this.RoundCorners(HairlineColour);

        var layout = MonitorEnumerator.Enumerate().Layout;
        var centre = new CapturePoint(
            _frame.VirtualX + (_frame.Width / 2d),
            _frame.VirtualY + (_frame.Height / 2d));
        var display = layout.MonitorAt(centre) ?? layout.Primary;

        _opening = PinPlacement.Opening(_frame.Width, _frame.Height, display.WorkArea);
        appWindow.MoveAndResize(Bounds(_opening));

        Activate();
        PinRoot.Focus(FocusState.Programmatic);
    }

    private static FrameworkElement? Glyph(ToolbarCommand command)
    {
        // The strip's own icons rather than a second set drawn for these two buttons:
        // the pin's Edit opens the same editor the overlay's does, and its close is the
        // overlay's cancel.
        var glyph = ToolbarIcons.For(new ToolbarItem(command, string.Empty));
        if (glyph is not null)
        {
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
        }

        return glyph;
    }

    private static RectInt32 Bounds(CaptureRegion region) => new(
        (int)region.X,
        (int)region.Y,
        (int)region.Width,
        (int)region.Height);

    private void PinRoot_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(PinRoot).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        // The drag is tracked in screen pixels rather than in the window's layout
        // units, because dragging the pin across a display boundary changes the
        // scale that would convert them, mid-gesture.
        _dragCursorOrigin = cursor;
        _dragWindowOrigin = this.GetAppWindow().Position;
        PinRoot.CapturePointer(e.Pointer);
    }

    private void PinRoot_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragCursorOrigin is not { } origin || !GetCursorPos(out var cursor))
        {
            return;
        }

        this.GetAppWindow().Move(new PointInt32(
            _dragWindowOrigin.X + cursor.X - origin.X,
            _dragWindowOrigin.Y + cursor.Y - origin.Y));
    }

    private void PinRoot_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragCursorOrigin = null;
        PinRoot.ReleasePointerCaptures();
    }

    private void PinRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void PinRoot_PointerEntered(object sender, PointerRoutedEventArgs e) =>
        PinChrome.Visibility = Visibility.Visible;

    private void PinRoot_PointerExited(object sender, PointerRoutedEventArgs e) =>
        PinChrome.Visibility = Visibility.Collapsed;

    /// <summary>
    /// Scales the pin about the pixel under the pointer, the way macshot's scroll wheel
    /// and pinch both do.
    /// </summary>
    /// <remarks>
    /// The pointer is read from the system rather than from the event, for the same
    /// reason the drag is: this is arithmetic in screen pixels, and the event's point is
    /// in the layout units of whichever display the window is mostly on.
    /// </remarks>
    private void PinRoot_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(PinRoot).Properties.MouseWheelDelta;
        if (delta == 0 || !GetCursorPos(out var cursor))
        {
            return;
        }

        e.Handled = true;
        Resize(PinPlacement.Zoomed(
            CurrentBounds(),
            _opening,
            1 + (WheelStep * delta / WheelNotch),
            new CapturePoint(cursor.X, cursor.Y)));
    }

    /// <summary>
    /// Back to 100%. The reading doubles as the button for it, as macshot's does — there
    /// is no other way back to 1:1 once the wheel has been over it, and a pin that no
    /// longer matches the pixels it was cut from is hard to compare against them.
    /// </summary>
    private void Zoom_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Handled, or the press underneath it starts dragging the window.
        e.Handled = true;
        Resize(PinPlacement.Restored(CurrentBounds(), _opening));
    }

    private void Edit_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        EditRequested?.Invoke(this, _frame);
        Close();
    }

    private void Close_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    private CaptureRegion CurrentBounds()
    {
        var appWindow = this.GetAppWindow();
        return new CaptureRegion(
            appWindow.Position.X,
            appWindow.Position.Y,
            appWindow.Size.Width,
            appWindow.Size.Height);
    }

    private void Resize(CaptureRegion bounds)
    {
        this.GetAppWindow().MoveAndResize(Bounds(bounds));
        ZoomText.Text = $"{PinPlacement.Percent(bounds, _opening)}%";
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync("Copy failed", () => ImageDelivery.CopyToClipboardAsync(_frame));
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync("Save failed", async () =>
        {
            var path = await ImageDelivery.SaveAsync(_frame, _settings.Current);
            await ShowMessageAsync("Saved", path);
        });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async Task RunAsync(string failureTitle, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            await ShowMessageAsync(failureTitle, exception.Message);
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        if ((Content as FrameworkElement)?.XamlRoot is not { } root)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = root,
        };
        await dialog.ShowAsync();
    }

    /// <summary>
    /// The hairline, as a COLORREF (0x00BBGGRR). Grey rather than macshot's white at 30%,
    /// which cannot be asked for: the attribute it goes to takes no alpha.
    /// </summary>
    private const int HairlineColour = 0x005A5A5A;

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
