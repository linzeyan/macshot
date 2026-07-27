using Macshot.Windows.Core.Capture;
using Macshot.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Point
// resolves to Macshot.Point and does not compile.
using Windows.Foundation;

namespace Macshot.Windows;

/// <summary>
/// The preview window shown after a capture.
/// </summary>
/// <remarks>
/// It is opened on demand by <see cref="CaptureController"/> and closing it does
/// not end the app: macshot keeps running in the notification area. Capture
/// orchestration and the global hotkeys deliberately do not live here, because
/// they must outlive every window.
/// </remarks>
public sealed partial class MainWindow : Window
{
    private readonly CaptureController _controller;
    private CapturedFrame? _capturedFrame;
    private CaptureRegion? _selection;
    private Point? _selectionStart;

    public MainWindow(CaptureController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        InitializeComponent();
    }

    public bool HasCapture => _capturedFrame is not null;

    /// <summary>Shows a capture, optionally with the region the user selected.</summary>
    public async Task PresentAsync(CapturedFrame frame, CaptureRegion? selection)
    {
        ArgumentNullException.ThrowIfNull(frame);

        _capturedFrame = frame;
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(frame.ToSoftwareBitmap());
        PreviewImage.Source = source;
        Canvas.SetLeft(PreviewImage, 0);
        Canvas.SetTop(PreviewImage, 0);
        PreviewCanvas.Width = frame.Width;
        PreviewCanvas.Height = frame.Height;

        _selection = selection;
        if (selection is { } region)
        {
            // The selection arrives in frame space, and the canvas is laid out at
            // frame size, so it maps across unchanged.
            ApplySelectionRectangle(region);
        }
        else
        {
            SelectionRectangle.Visibility = Visibility.Collapsed;
        }

        Bindings.Update();
    }

    private async void CaptureArea_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync("Screen capture failed", _controller.BeginAreaCaptureAsync);
    }

    private async void CaptureAllScreens_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync("Screen capture failed", _controller.CaptureAllScreensAsync);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync("Save failed", SaveAsync);
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync("Copy failed", async () =>
        {
            if (SelectedFrame() is { } output)
            {
                await ImageDelivery.CopyToClipboardAsync(output);
            }
        });
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _capturedFrame = null;
        _selection = null;
        PreviewImage.Source = null;
        SelectionRectangle.Visibility = Visibility.Collapsed;
        Bindings.Update();
    }

    private async Task SaveAsync()
    {
        if (SelectedFrame() is not { } output)
        {
            return;
        }

        var path = await ImageDelivery.SaveAsync(output, _controller.Settings);
        await ShowMessageAsync("Saved", path);
    }

    /// <summary>
    /// The pixels an output action should act on: the drag selection when there is
    /// one, otherwise the whole capture.
    /// </summary>
    private CapturedFrame? SelectedFrame()
    {
        if (_capturedFrame is null)
        {
            return null;
        }

        return _selection is { IsEmpty: false } region
            ? NativeScreenCaptureService.Crop(_capturedFrame, region)
            : _capturedFrame;
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

    private void UpdateSelection(Point start, Point end)
    {
        var region = CaptureRegion.FromPoints(start.X, start.Y, end.X, end.Y);
        _selection = region;
        ApplySelectionRectangle(region);
    }

    private void ApplySelectionRectangle(CaptureRegion region)
    {
        Canvas.SetLeft(SelectionRectangle, region.X);
        Canvas.SetTop(SelectionRectangle, region.Y);
        SelectionRectangle.Width = region.Width;
        SelectionRectangle.Height = region.Height;
        SelectionRectangle.Visibility = Visibility.Visible;
    }

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
        var root = (Content as FrameworkElement)?.XamlRoot
            ?? throw new InvalidOperationException("The preview window has no XAML root.");
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = root,
        };
        await dialog.ShowAsync();
    }
}
