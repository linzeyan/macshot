using Macshot.Windows.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace Macshot.Windows;

/// <summary>
/// The panel that appears after a capture, offering the actions that would
/// otherwise need the preview window. It is the counterpart of the macOS
/// <c>FloatingThumbnailController</c>.
/// </summary>
/// <remarks>
/// It dismisses itself, because the whole point is that a capture does not
/// interrupt what the user was doing. Hovering it stops the countdown: a panel
/// that vanished while being aimed at would be worse than no panel at all.
/// </remarks>
public sealed partial class ThumbnailWindow : Window
{
    private const int WidthDips = 260;
    private const int HeightDips = 210;
    private const int MarginDips = 16;

    private readonly CapturedFrame _frame;
    private readonly SettingsStore _settings;
    private readonly DispatcherTimer _dismissTimer = new();

    public ThumbnailWindow(CapturedFrame frame, SettingsStore settings)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        InitializeComponent();

        _dismissTimer.Interval = TimeSpan.FromSeconds(_settings.Current.ThumbnailSeconds);
        _dismissTimer.Tick += (_, _) => Close();
    }

    /// <summary>Raised with the capture the user wants kept on top.</summary>
    public event EventHandler<CapturedFrame>? PinRequested;

    /// <summary>Raised with the capture the user wants opened in the preview window.</summary>
    public event EventHandler<CapturedFrame>? EditRequested;

    public async Task ShowAsync()
    {
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(_frame.ToSoftwareBitmap());
        ThumbnailImage.Source = source;

        var appWindow = this.GetAppWindow();
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
        }

        appWindow.MoveAndResize(PlaceBottomRight());
        Activate();
        _dismissTimer.Start();
    }

    /// <summary>
    /// Bottom-right of the primary display's work area, which is where Windows puts
    /// transient notifications, and inside the work area so the taskbar does not
    /// cover the buttons.
    /// </summary>
    private static RectInt32 PlaceBottomRight()
    {
        var monitor = MonitorEnumerator.Enumerate().Layout.Primary;
        var width = (int)(WidthDips * monitor.Scale);
        var height = (int)(HeightDips * monitor.Scale);
        var margin = (int)(MarginDips * monitor.Scale);

        return new RectInt32(
            (int)monitor.WorkArea.Right - width - margin,
            (int)monitor.WorkArea.Bottom - height - margin,
            width,
            height);
    }

    private void ThumbnailRoot_PointerEntered(object sender, PointerRoutedEventArgs e) => _dismissTimer.Stop();

    private void ThumbnailRoot_PointerExited(object sender, PointerRoutedEventArgs e) => _dismissTimer.Start();

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

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        PinRequested?.Invoke(this, _frame);
        Close();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        EditRequested?.Invoke(this, _frame);
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async Task RunAsync(string failureTitle, Func<Task> action)
    {
        // Whatever the action was, the user is now interacting deliberately, so the
        // panel must not disappear underneath them.
        _dismissTimer.Stop();
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
}
