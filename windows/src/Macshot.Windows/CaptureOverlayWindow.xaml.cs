using Macshot.Windows.Core.Capture;
using Macshot.Windows.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Point
// resolves to Macshot.Point and does not compile.
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace Macshot.Windows;

/// <summary>
/// The capture overlay for one display. One window is created per monitor because
/// a WinUI window has a single rasterization scale, so a window spanning displays
/// with different DPI cannot map pointer input to pixels correctly. See
/// <c>docs/windows-port/architecture.md</c>, decision D6.
/// </summary>
public sealed partial class CaptureOverlayWindow : Window
{
    private readonly MonitorLayout _layout;
    private readonly CaptureMonitor _monitor;
    private readonly CapturedFrame _monitorFrame;
    private Point? _selectionStart;

    public CaptureOverlayWindow(CapturedFrame desktopFrame, MonitorLayout layout, CaptureMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(desktopFrame);
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _monitorFrame = NativeScreenCaptureService.Crop(desktopFrame, layout.FrameRegionOf(monitor));
        InitializeComponent();
    }

    /// <summary>
    /// Reports the selection in frame space, meaning pixels of the whole virtual
    /// desktop capture rather than this display's local pixels, so the owner can
    /// crop from the frame it captured.
    /// </summary>
    public event EventHandler<CaptureRegion>? SelectionCompleted;

    public event EventHandler? Cancelled;

    public CaptureMonitor Monitor => _monitor;

    public async Task ShowAsync()
    {
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(_monitorFrame.ToSoftwareBitmap());
        PreviewImage.Source = source;

        var appWindow = this.GetAppWindow();
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
        }

        // AppWindow positions in physical pixels, so the display's virtual-space
        // bounds go in unchanged. Converting to layout units here would misplace
        // the overlay on every display that is not at 100%.
        appWindow.MoveAndResize(new RectInt32(
            (int)_monitor.Bounds.X,
            (int)_monitor.Bounds.Y,
            (int)_monitor.Bounds.Width,
            (int)_monitor.Bounds.Height));
        Activate();
        OverlayRoot.Focus(FocusState.Programmatic);
    }

    private void SelectionCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _selectionStart = e.GetCurrentPoint(SelectionCanvas).Position;
        SelectionCanvas.CapturePointer(e.Pointer);
        UpdateSelection(_selectionStart.Value, _selectionStart.Value);
    }

    private void SelectionCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_selectionStart is { } start && e.Pointer.IsInContact)
        {
            UpdateSelection(start, e.GetCurrentPoint(SelectionCanvas).Position);
        }
    }

    private void SelectionCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_selectionStart is not { } start)
        {
            return;
        }

        var end = e.GetCurrentPoint(SelectionCanvas).Position;
        UpdateSelection(start, end);
        _selectionStart = null;
        SelectionCanvas.ReleasePointerCaptures();

        var region = ToFrameRegion(start, end);
        if (!region.IsEmpty)
        {
            SelectionCompleted?.Invoke(this, region);
        }
    }

    private void OverlayRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;

            // Escape cancels the whole capture, not just this display's overlay, so
            // the owner tears every window down instead of this one closing itself
            // and stranding the overlays on the other monitors.
            Cancelled?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Draws the marquee, which stays in layout units because it is chrome.</summary>
    private void UpdateSelection(Point start, Point end)
    {
        var region = CaptureRegion.FromPoints(start.X, start.Y, end.X, end.Y);
        Canvas.SetLeft(SelectionRectangle, region.X);
        Canvas.SetTop(SelectionRectangle, region.Y);
        SelectionRectangle.Width = region.Width;
        SelectionRectangle.Height = region.Height;
        SelectionRectangle.Visibility = Visibility.Visible;
    }

    private CaptureRegion ToFrameRegion(Point start, Point end)
    {
        // Scaling by the displayed image's ActualWidth would silently go wrong the
        // moment the image is letterboxed or the window is not exactly the display
        // size; the monitor's own scale is the authoritative conversion.
        var dipRegion = CaptureRegion.FromPoints(start.X, start.Y, end.X, end.Y);
        return _layout.PointerToFrame(_monitor, dipRegion);
    }
}
