using Macshot.Windows.Core.Capture;
using Macshot.Windows.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.System;

namespace Macshot.Windows;

public sealed partial class CaptureOverlayWindow : Window
{
    private readonly CapturedFrame _frame;
    private Windows.Foundation.Point? _selectionStart;

    public CaptureOverlayWindow(CapturedFrame frame)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        InitializeComponent();
    }

    public event EventHandler<CaptureRegion>? SelectionCompleted;

    public CapturedFrame Frame => _frame;

    public async Task ShowAsync()
    {
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(_frame.ToSoftwareBitmap());
        PreviewImage.Source = source;

        var appWindow = this.GetAppWindow();
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
        }

        appWindow.MoveAndResize(new RectInt32(_frame.VirtualX, _frame.VirtualY, _frame.Width, _frame.Height));
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

        UpdateSelection(start, e.GetCurrentPoint(SelectionCanvas).Position);
        _selectionStart = null;
        SelectionCanvas.ReleasePointerCaptures();

        var region = ToFrameRegion(start, e.GetCurrentPoint(SelectionCanvas).Position);
        if (!region.IsEmpty)
        {
            SelectionCompleted?.Invoke(this, region);
            Close();
        }
    }

    private void OverlayRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void UpdateSelection(Windows.Foundation.Point start, Windows.Foundation.Point end)
    {
        var region = CaptureRegion.FromPoints(start.X, start.Y, end.X, end.Y);
        Canvas.SetLeft(SelectionRectangle, region.X);
        Canvas.SetTop(SelectionRectangle, region.Y);
        SelectionRectangle.Width = region.Width;
        SelectionRectangle.Height = region.Height;
        SelectionRectangle.Visibility = Visibility.Visible;
    }

    private CaptureRegion ToFrameRegion(Windows.Foundation.Point start, Windows.Foundation.Point end)
    {
        if (SelectionCanvas.ActualWidth <= 0 || SelectionCanvas.ActualHeight <= 0)
        {
            return default;
        }

        return CaptureRegion.FromPoints(
            start.X * _frame.Width / SelectionCanvas.ActualWidth,
            start.Y * _frame.Height / SelectionCanvas.ActualHeight,
            end.X * _frame.Width / SelectionCanvas.ActualWidth,
            end.Y * _frame.Height / SelectionCanvas.ActualHeight);
    }
}
