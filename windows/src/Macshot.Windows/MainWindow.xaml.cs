using Macshot.Windows.Core.Capture;
using Macshot.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;

namespace Macshot.Windows;

public sealed partial class MainWindow : Window
{
    private readonly NativeScreenCaptureService _screenCaptureService = new();
    private CapturedFrame? _capturedFrame;
    private CaptureRegion? _selection;
    private Windows.Foundation.Point? _selectionStart;
    private GlobalHotkeyService? _globalHotkeys;
    private CaptureOverlayWindow? _captureOverlay;

    public MainWindow()
    {
        InitializeComponent();
        InitializeGlobalHotkeys();
        Closed += (_, _) => _globalHotkeys?.Dispose();
    }

    public bool HasCapture => _capturedFrame is not null;

    private async void CaptureAllScreens_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await CaptureAllScreensAsync();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Screen capture failed", exception.Message);
        }
    }

    private async void CaptureArea_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await BeginAreaCaptureAsync();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Screen capture failed", exception.Message);
        }
    }

    private async Task CaptureAllScreensAsync()
    {
        var frame = _screenCaptureService.CaptureVirtualDesktop();
        await PresentCapturedFrameAsync(frame, selection: null);
    }

    private async Task BeginAreaCaptureAsync()
    {
        if (_captureOverlay is not null)
        {
            return;
        }

        var overlay = new CaptureOverlayWindow(_screenCaptureService.CaptureVirtualDesktop());
        _captureOverlay = overlay;
        overlay.SelectionCompleted += CaptureOverlay_SelectionCompleted;
        overlay.Closed += CaptureOverlay_Closed;
        await overlay.ShowAsync();
    }

    private async void CaptureOverlay_SelectionCompleted(object sender, CaptureRegion selection)
    {
        if (sender is not CaptureOverlayWindow overlay)
        {
            return;
        }

        await PresentCapturedFrameAsync(overlay.Frame, selection);
        Activate();
    }

    private void CaptureOverlay_Closed(object sender, WindowEventArgs args)
    {
        if (ReferenceEquals(sender, _captureOverlay))
        {
            _captureOverlay = null;
        }
    }

    private async Task PresentCapturedFrameAsync(CapturedFrame frame, CaptureRegion? selection)
    {
        _capturedFrame = frame;
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(_capturedFrame.ToSoftwareBitmap());
        PreviewImage.Source = source;
        Canvas.SetLeft(PreviewImage, 0);
        Canvas.SetTop(PreviewImage, 0);
        PreviewCanvas.Width = _capturedFrame.Width;
        PreviewCanvas.Height = _capturedFrame.Height;
        _selection = selection;
        if (selection is { } region)
        {
            Canvas.SetLeft(SelectionRectangle, region.X);
            Canvas.SetTop(SelectionRectangle, region.Y);
            SelectionRectangle.Width = region.Width;
            SelectionRectangle.Height = region.Height;
            SelectionRectangle.Visibility = Visibility.Visible;
        }
        else
        {
            SelectionRectangle.Visibility = Visibility.Collapsed;
        }
        Bindings.Update();
    }

    private async void SavePng_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SavePngAsync();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Save failed", exception.Message);
        }
    }

    private async Task SavePngAsync()
    {
        if (_capturedFrame is null)
        {
            return;
        }

        var path = await _screenCaptureService.SavePngAsync(_capturedFrame, _selection);
        await ShowMessageAsync("Saved", path);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _capturedFrame = null;
        _selection = null;
        PreviewImage.Source = null;
        SelectionRectangle.Visibility = Visibility.Collapsed;
        Bindings.Update();
    }

    private void PreviewCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!HasCapture)
        {
            return;
        }

        _selectionStart = e.GetCurrentPoint(PreviewCanvas).Position;
        PreviewCanvas.CapturePointer(e.Pointer);
        UpdateSelection(_selectionStart.Value, _selectionStart.Value);
    }

    private void PreviewCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_selectionStart is { } start && e.Pointer.IsInContact)
        {
            UpdateSelection(start, e.GetCurrentPoint(PreviewCanvas).Position);
        }
    }

    private void PreviewCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_selectionStart is { } start)
        {
            UpdateSelection(start, e.GetCurrentPoint(PreviewCanvas).Position);
        }

        _selectionStart = null;
        PreviewCanvas.ReleasePointerCaptures();
    }

    private void UpdateSelection(Windows.Foundation.Point start, Windows.Foundation.Point end)
    {
        _selection = CaptureRegion.FromPoints(start.X, start.Y, end.X, end.Y);
        Canvas.SetLeft(SelectionRectangle, _selection.Value.X);
        Canvas.SetTop(SelectionRectangle, _selection.Value.Y);
        SelectionRectangle.Width = _selection.Value.Width;
        SelectionRectangle.Height = _selection.Value.Height;
        SelectionRectangle.Visibility = Visibility.Visible;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var root = (Content as FrameworkElement)?.XamlRoot
            ?? throw new InvalidOperationException("The main window has no XAML root.");
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = root,
        };
        await dialog.ShowAsync();
    }

    private Task ShowErrorAsync(string title, string message) => ShowMessageAsync(title, message);

    private void InitializeGlobalHotkeys()
    {
        _globalHotkeys = new GlobalHotkeyService(WindowNative.GetWindowHandle(this));
        _globalHotkeys.RegisterControlShift(1, 'X', () =>
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await BeginAreaCaptureAsync();
                }
                catch (Exception exception)
                {
                    await ShowErrorAsync("Screen capture failed", exception.Message);
                }
            });
        });
        _globalHotkeys.RegisterControlShift(2, 'F', () =>
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await CaptureAllScreensAsync();
                    await SavePngAsync();
                }
                catch (Exception exception)
                {
                    await ShowErrorAsync("Full screen capture failed", exception.Message);
                }
            });
        });
    }
}
