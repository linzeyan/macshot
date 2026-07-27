using System.Runtime.InteropServices;
using Macshot.Windows.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.System;

namespace Macshot.Windows;

/// <summary>
/// A capture kept on top of everything else, the counterpart of the macOS
/// <c>PinWindowController</c>.
/// </summary>
/// <remarks>
/// It opens over the pixels it was taken from, because a pin is used to keep a
/// piece of screen visible while working somewhere else, and appearing where it
/// was cut from makes that relationship obvious. It has no title bar, so dragging
/// is handled here.
/// </remarks>
public sealed partial class PinWindow : Window
{
    private readonly CapturedFrame _frame;
    private readonly SettingsStore _settings;

    private CursorPoint? _dragCursorOrigin;
    private PointInt32 _dragWindowOrigin;

    public PinWindow(CapturedFrame frame, SettingsStore settings)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        InitializeComponent();
    }

    public async Task ShowPinnedAsync()
    {
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(_frame.ToSoftwareBitmap());
        PinImage.Source = source;

        var appWindow = this.GetAppWindow();
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
        }

        // Physical pixels, so the pin is the same size as what it captured whatever
        // the display scale is.
        appWindow.MoveAndResize(new RectInt32(
            _frame.VirtualX,
            _frame.VirtualY,
            _frame.Width,
            _frame.Height));
        Activate();
        PinRoot.Focus(FocusState.Programmatic);
    }

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
