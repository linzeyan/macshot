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
using static Macshot.Windows.Services.Localization;

namespace Macshot.Windows;

/// <summary>
/// The panel that appears after a capture, offering the actions that would otherwise
/// need the preview window. It is the counterpart of the macOS
/// <c>FloatingThumbnailController</c>, in its 240 × 160.
/// </summary>
/// <remarks>
/// It dismisses itself, because the whole point is that a capture does not interrupt what
/// the user was doing. Hovering it stops the countdown — a panel that vanished while
/// being aimed at would be worse than no panel at all — and hovering is also what brings
/// the buttons up. They are over the capture rather than under it: the panel is small,
/// and a row of buttons beneath the picture would take a third of it.
/// </remarks>
public sealed partial class ThumbnailWindow : Window
{
    /// <summary>
    /// The hairline as a COLORREF: macshot draws white at 40% round the thumbnail, and
    /// the attribute that carries it here takes no alpha.
    /// </summary>
    private const int HairlineColour = 0x00999999;

    private readonly CapturedFrame _frame;
    private readonly SettingsStore _settings;
    private readonly DispatcherTimer _dismissTimer = new();

    public ThumbnailWindow(CapturedFrame frame, SettingsStore settings, string? historyPath)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        HistoryPath = historyPath;
        InitializeComponent();
        // Every string in the XAML is already the English text macshot keys by,
        // so the page is translated in place rather than written twice.
        this.Localize();

        CloseDisc.Child = Glyph(ToolbarCommand.Cancel);
        PinDisc.Child = Glyph(ToolbarCommand.Pin);
        EditDisc.Child = Glyph(ToolbarCommand.OpenEditor);

        _dismissTimer.Interval = TimeSpan.FromSeconds(_settings.Current.ThumbnailSeconds);
        _dismissTimer.Tick += (_, _) => Close();
    }

    /// <summary>Raised with the capture the user wants kept on top.</summary>
    public event EventHandler<CapturedFrame>? PinRequested;

    /// <summary>Raised with the capture the user wants opened in the preview window.</summary>
    public event EventHandler<CapturedFrame>? EditRequested;

    /// <summary>Raised when the user wants the whole column gone, not just this one.</summary>
    public event EventHandler? CloseAllRequested;

    /// <summary>
    /// Where history put its copy of this capture, so Delete can take it back out. Null
    /// when history is off, which is what makes Delete a plain close.
    /// </summary>
    /// <remarks>
    /// Taken in the constructor and read-only afterwards, rather than set through an
    /// object initializer. XamlTypeInfo.g.cs writes a setter for every public property
    /// of a type XAML instantiates, and an assignment it generates is not inside an
    /// initializer, so <c>init</c> here does not compile at all — CS8852. A property
    /// with no setter gives it nothing to generate, and the path is known at
    /// construction anyway.
    /// </remarks>
    public string? HistoryPath { get; }

    /// <summary>
    /// Shows the panel <paramref name="stackIndex"/> places up the corner, so a second
    /// capture stands above the first rather than on top of it.
    /// </summary>
    public async Task ShowAsync(int stackIndex = 0)
    {
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(_frame.ToSoftwareBitmap());
        ThumbnailImage.Source = source;

        var appWindow = this.GetAppWindow();
        var presenter = appWindow.MakeChromeless();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        this.RoundCorners(HairlineColour);

        appWindow.MoveAndResize(Place(stackIndex));
        Activate();
        _dismissTimer.Start();
    }

    /// <summary>
    /// Moves the panel to a new place in the stack, for when one below it is dismissed.
    /// </summary>
    /// <remarks>
    /// Failures are swallowed on purpose: the window may have been closed between the
    /// owner deciding to restack and this running, and a panel that could not be nudged
    /// is not worth a message about.
    /// </remarks>
    public void Restack(int stackIndex)
    {
        try
        {
            this.GetAppWindow().MoveAndResize(Place(stackIndex));
        }
        catch (Exception exception)
        {
            DiagnosticLog.Verbose($"Could not restack a thumbnail: {exception.Message}");
        }
    }

    private static FrameworkElement? Glyph(ToolbarCommand command)
    {
        // The toolbar's own icons rather than a second set drawn for these three: the
        // panel's edit opens the editor the strip's does, and its close is the same X.
        var glyph = ToolbarIcons.For(new ToolbarItem(command, string.Empty));
        if (glyph is not null)
        {
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
        }

        return glyph;
    }

    /// <summary>
    /// The corner of the primary display's work area the user chose — inside the work
    /// area, so the taskbar never covers the buttons — with each place in the stack one
    /// panel further from the edge, macshot's 8 apart.
    /// </summary>
    private RectInt32 Place(int stackIndex)
    {
        var monitor = MonitorEnumerator.Enumerate().Layout.Primary;
        var (x, y, width, height) = ThumbnailPlacement.For(
            _settings.Current.ThumbnailCorner,
            monitor.WorkArea,
            _settings.Current.ThumbnailScale,
            monitor.Scale,
            stackIndex);

        return new RectInt32(x, y, width, height);
    }

    private static void Handle(PointerRoutedEventArgs e, Action action)
    {
        // Handled, or the panel underneath sees the same press as the start of
        // something else.
        e.Handled = true;
        action();
    }

    private void ThumbnailRoot_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _dismissTimer.Stop();
        HoverLayer.Visibility = Visibility.Visible;
    }

    private void ThumbnailRoot_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        HoverLayer.Visibility = Visibility.Collapsed;
        _dismissTimer.Start();
    }

    private void Close_PointerPressed(object sender, PointerRoutedEventArgs e) => Handle(e, Close);

    private void Pin_PointerPressed(object sender, PointerRoutedEventArgs e) => Handle(e, Pin);

    private void Edit_PointerPressed(object sender, PointerRoutedEventArgs e) => Handle(e, Edit);

    private void Copy_PointerPressed(object sender, PointerRoutedEventArgs e) =>
        Handle(e, () => _ = CopyAsync());

    private void Save_PointerPressed(object sender, PointerRoutedEventArgs e) =>
        Handle(e, () => _ = SaveAsync());

    private void Copy_Click(object sender, RoutedEventArgs e) => _ = CopyAsync();

    private void Save_Click(object sender, RoutedEventArgs e) => _ = SaveAsync();

    private void SaveAs_Click(object sender, RoutedEventArgs e) => _ = SaveAsAsync();

    private void ReadText_Click(object sender, RoutedEventArgs e) => _ = ReadTextAsync();

    private void Pin_Click(object sender, RoutedEventArgs e) => Pin();

    private void Edit_Click(object sender, RoutedEventArgs e) => Edit();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void CloseAll_Click(object sender, RoutedEventArgs e) =>
        CloseAllRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Takes the capture back out of the history and closes the panel — which for a
    /// capture that was never written there is simply a close.
    /// </summary>
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryPath is { } path)
        {
            ScreenshotHistory.Forget(path);
        }

        Close();
    }

    private void Pin()
    {
        PinRequested?.Invoke(this, _frame);
        Close();
    }

    private void Edit()
    {
        EditRequested?.Invoke(this, _frame);
        Close();
    }

    private Task CopyAsync() =>
        RunAsync("Copy failed", () => ImageDelivery.CopyToClipboardAsync(_frame));

    private Task SaveAsync() => RunAsync("Save failed", async () =>
    {
        var path = await ImageDelivery.SaveAsync(_frame, _settings.Current);
        await ShowMessageAsync("Saved", path);
    });

    private Task SaveAsAsync() => RunAsync("Save failed", async () =>
    {
        // Nothing is reported afterwards: the user chose the name and the place, so they
        // already know where it went.
        await SavePrompt.WriteAsync(this, _frame, _settings.Current);
    });

    private Task ReadTextAsync() => RunAsync("Could not read the text", async () =>
    {
        var lines = await TextRecognizer.RecognizeAsync(_frame, 0, 0);
        var codes = await TextRecognizer.ScanQrCodesAsync(_frame);
        new TextRecognitionWindow(TextRecognizer.ToText(lines), _settings, _frame, codes).Activate();
    });

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
            CloseButtonText = L("OK"),
            XamlRoot = root,
        };
        await dialog.ShowAsync();
    }
}
