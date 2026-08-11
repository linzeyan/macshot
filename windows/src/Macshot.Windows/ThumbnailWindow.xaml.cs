using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
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

    /// <summary>
    /// The capture this panel stands for. Not readonly, because the four turns on the
    /// menu replace it: every action here reads this field, so turning the pixels once
    /// is what makes Copy, Save and OCR all work on what the panel is showing rather than
    /// on what it was showing before.
    /// </summary>
    private CapturedFrame _frame;
    private readonly SettingsStore _settings;

    /// <summary>Where the pointer was when a flick began, or null when none is in flight.</summary>
    private double? _flickFrom;

    /// <summary>Where the panel was then, so a flick that falls short can put it back.</summary>
    private int _flickStart;
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

#if OFFLINE
        // Hidden rather than left empty: an unlit disc in a corner that does nothing
        // reads as a button that failed, and this build has nothing to upload with. The
        // menu row goes with it, for the same reason.
        UploadDisc.Visibility = Visibility.Collapsed;
        UploadItem.Visibility = Visibility.Collapsed;
#else
        UploadDisc.Child = Glyph(ToolbarCommand.Upload);
#endif

        _dismissTimer.Interval = TimeSpan.FromSeconds(_settings.Current.ThumbnailSeconds);
        _dismissTimer.Tick += (_, _) => Close();
    }

    /// <summary>Raised with the capture the user wants kept on top.</summary>
    public event EventHandler<CapturedFrame>? PinRequested;

    /// <summary>Raised with the capture the user wants opened in the preview window.</summary>
    public event EventHandler<CapturedFrame>? EditRequested;

    /// <summary>Raised when the user wants the whole column gone, not just this one.</summary>
    public event EventHandler? CloseAllRequested;

    /// <summary>Raised when the user wants every panel in the column written to a folder.</summary>
    /// <remarks>
    /// Answered by the owner rather than here, because it is about the column and only
    /// the owner has one: this panel knows nothing of the others.
    /// </remarks>
    public event EventHandler? SaveAllRequested;

    /// <summary>
    /// The capture this panel stands for, as the four turns have left it — which is what
    /// makes the owner's Save All write what is on show rather than what was taken.
    /// </summary>
    public CapturedFrame Capture => _frame;

#if !OFFLINE
    /// <summary>Raised with the capture the user wants sent, from the fourth disc.</summary>
    public event EventHandler<CapturedFrame>? UploadRequested;
#endif

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
        await source.SetBitmapAsync(_frame.ToDisplayBitmap());
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

    /// <summary>
    /// Starts a flick. Only reached when nothing on the panel wanted the press, because
    /// every button here marks it handled and a handled press does not bubble.
    /// </summary>
    private void ThumbnailRoot_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _flickFrom = e.GetCurrentPoint(null).Position.X;
        _flickStart = this.GetAppWindow().Position.X;
        _ = ThumbnailRoot.CapturePointer(e.Pointer);
    }

    /// <summary>
    /// Pushes the panel along with the pointer, but only outward.
    /// </summary>
    /// <remarks>
    /// Inward is refused rather than tracked: dragging a thumbnail towards the middle of
    /// the screen is a drag to move it, which this port does not offer, and letting the
    /// panel follow the pointer there would promise one.
    /// </remarks>
    private void ThumbnailRoot_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_flickFrom is not { } from)
        {
            return;
        }

        var window = this.GetAppWindow();
        var direction = ThumbnailPlacement.DismissDirection(_settings.Current.ThumbnailCorner);
        var pushed = Math.Max(0, (e.GetCurrentPoint(null).Position.X - from) * direction);

        // Moved in the window's own pixels rather than the pointer's layout units, which
        // is what AppWindow.Move is measured in.
        var scale = MonitorEnumerator.Enumerate().Layout.Primary.Scale;
        window.Move(new PointInt32(
            _flickStart + (int)Math.Round(pushed * direction * scale),
            window.Position.Y));

        ThumbnailRoot.Opacity = ThumbnailPlacement.DismissOpacity(pushed * scale, window.Size.Width);
    }

    /// <summary>
    /// Lets go: past the threshold the panel is thrown away, short of it it goes back.
    /// </summary>
    private void ThumbnailRoot_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_flickFrom is not { } from)
        {
            return;
        }

        ThumbnailRoot.ReleasePointerCapture(e.Pointer);
        _flickFrom = null;

        var window = this.GetAppWindow();
        var direction = ThumbnailPlacement.DismissDirection(_settings.Current.ThumbnailCorner);
        var scale = MonitorEnumerator.Enumerate().Layout.Primary.Scale;
        var pushed = Math.Max(0, (e.GetCurrentPoint(null).Position.X - from) * direction) * scale;

        if (pushed >= ThumbnailPlacement.DismissThreshold(window.Size.Width))
        {
            Close();
            return;
        }

        window.Move(new PointInt32(_flickStart, window.Position.Y));
        ThumbnailRoot.Opacity = 1;
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

    private void Upload_PointerPressed(object sender, PointerRoutedEventArgs e) =>
#if OFFLINE
        // The disc is collapsed in this build; the handler stays so the markup, which is
        // shared, still binds.
        Handle(e, () => { });
#else
        Handle(e, UploadCapture);
#endif

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

    private void RotateLeft_Click(object sender, RoutedEventArgs e) => _ = TurnAsync(ImageTurn.Left);

    private void RotateRight_Click(object sender, RoutedEventArgs e) => _ = TurnAsync(ImageTurn.Right);

    private void FlipHorizontal_Click(object sender, RoutedEventArgs e) =>
        _ = TurnAsync(ImageTurn.FlipHorizontal);

    private void FlipVertical_Click(object sender, RoutedEventArgs e) =>
        _ = TurnAsync(ImageTurn.FlipVertical);

    /// <summary>
    /// Turns or mirrors the capture the panel is holding, and writes the archived copy
    /// back with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both, because the panel is a view of the archived capture rather than a separate
    /// thing: turning only the one on screen would leave Save and the history panel
    /// disagreeing about which way up it is, and the panel goes away in seconds.
    /// </para>
    /// <para>
    /// The picture is at its capture size and the panel is 240 wide, so the aspect ratio
    /// changes under a quarter turn and the image is cropped to fill differently. That is
    /// the same UniformToFill it was drawn with in the first place; the panel does not
    /// resize, as macshot's does not.
    /// </para>
    /// </remarks>
    private Task TurnAsync(ImageTurn turn) => RunAsync("Could not turn the capture", async () =>
    {
        var (width, height, pixels) = FrameTransforms.Apply(turn, _frame.Width, _frame.Height, _frame.BgraPixels);
        _frame = new CapturedFrame(_frame.VirtualX, _frame.VirtualY, width, height, pixels, _frame.HasAlpha);

        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(_frame.ToDisplayBitmap());
        ThumbnailImage.Source = source;

        // The marks go with it, as they do from the history panel: they were placed in
        // the coordinates the pixels used to have.
        if (HistoryPath is { } path)
        {
            await ScreenshotHistory.RewriteAsync(path, _frame, _settings.Current);
        }
    });

    private void OpenWith_Click(object sender, RoutedEventArgs e) => _ = HandOffAsync(open: true);

    private void Share_Click(object sender, RoutedEventArgs e) => _ = HandOffAsync(open: false);

    /// <summary>
    /// Hands the capture to another program, either to open or to share.
    /// </summary>
    /// <remarks>
    /// Through a copy in the temporary directory rather than through the archived file,
    /// which is macshot's <c>makeCurrentImageFileURL</c>: the panel is up before anything
    /// has been saved, and with history off this capture is nowhere on disk. It also
    /// means a program that writes back over what it opened cannot touch the archive.
    /// </remarks>
    private Task HandOffAsync(bool open) => RunAsync("Could not hand the capture over", async () =>
    {
        if (open)
        {
            await OpenWith.ShowAsync(await TemporaryCapture.WriteAsync(_frame, _settings.Current));
            return;
        }

        // The pane belongs to this window, so the panel must not go away underneath it —
        // which is why the dismissal timer stays stopped rather than being restarted.
        await ShareSheet.ShowAsync(this, _frame, _settings.Current);
    });

    private void CloseAll_Click(object sender, RoutedEventArgs e) =>
        CloseAllRequested?.Invoke(this, EventArgs.Empty);

    private void SaveAll_Click(object sender, RoutedEventArgs e)
    {
        // The dismissal timer stays stopped: a folder picker is about to take the
        // foreground, and the panel raising this must still be there when it comes back.
        _dismissTimer.Stop();
        SaveAllRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Upload_Click(object sender, RoutedEventArgs e)
    {
#if !OFFLINE
        UploadCapture();
#endif

        // In the offline build the row is collapsed and this does nothing. The handler
        // stays so the markup, which is shared between the two, still binds.
    }

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

#if !OFFLINE
    /// <summary>
    /// Sends the capture, and takes the panel away — the toast reports the rest.
    /// </summary>
    /// <remarks>
    /// The panel closes as it does for Pin and Edit, rather than staying up to show
    /// progress. Two panels in two corners reporting one capture is worse than one, and
    /// the toast outlives this window by design.
    /// </remarks>
    private void UploadCapture()
    {
        if (_settings.Current.UploadConfirm
            && !Upload.UploadConfirm.Ask(
                WinRT.Interop.WindowNative.GetWindowHandle(this),
                _settings.Current.UploadProvider))
        {
            return;
        }

        UploadRequested?.Invoke(this, _frame);
        Close();
    }
#endif

    private Task CopyAsync() =>
        RunAsync("Copy failed", () => ImageDelivery.CopyToClipboardAsync(_frame));

    private Task SaveAsync() => RunAsync("Save failed", async () =>
    {
        // Null means the user dismissed the dialog they asked for, which is not a failure
        // and not a save: nothing to report either way.
        if (await SavePrompt.SaveAsync(this, _frame, _settings.Current) is { } path)
        {
            await ShowMessageAsync("Saved", path);
        }
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
