using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Core.Recognition;
using Macshot.Windows.Rendering;
using Macshot.Windows.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Point
// resolves to Macshot.Point and does not compile.
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace Macshot.Windows;

/// <summary>
/// The capture overlay for one display. One window is created per monitor because
/// a WinUI window has a single rasterization scale, so a window spanning displays
/// with different DPI cannot map pointer input to pixels correctly. See
/// <c>docs/windows-port/architecture.md</c>, decision D6.
/// </summary>
public sealed partial class CaptureOverlayWindow : Window
{
    private readonly CapturedFrame _desktopFrame;
    private readonly MonitorLayout _layout;
    private readonly CaptureMonitor _monitor;
    private readonly CapturedFrame _monitorFrame;
    private readonly SettingsStore _settings;
    private readonly AnnotationEditor _editor = new(new AnnotationDocument());
    private readonly Dictionary<AnnotationTool, ToggleButton> _toolButtons = [];

    private RasterAnnotationPreview? _annotationPreview;
    private Point? _selectionStart;
    private CaptureRegion? _selection;

    /// <summary>The style the toolbar started from, so only a real change is written back.</summary>
    private AnnotationStyle _loadedStyle = AnnotationStyle.Default;

    private bool _isLoadingStyle;

    public CaptureOverlayWindow(
        CapturedFrame desktopFrame,
        MonitorLayout layout,
        CaptureMonitor monitor,
        SettingsStore settings)
    {
        _desktopFrame = desktopFrame ?? throw new ArgumentNullException(nameof(desktopFrame));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _monitorFrame = NativeScreenCaptureService.Crop(desktopFrame, layout.FrameRegionOf(monitor));
        InitializeComponent();
    }

    /// <summary>
    /// Raised with the finished image: the selection cropped out of the capture with
    /// every annotation already burned in. The owner receives pixels rather than a
    /// region because only this window knows what was drawn on it.
    /// </summary>
    public event EventHandler<CapturedFrame>? CaptureCompleted;

    /// <summary>
    /// Raised once this overlay owns the capture, so the owner can close the
    /// overlays on the other displays instead of leaving always-on-top windows
    /// covering them while the user annotates.
    /// </summary>
    public event EventHandler? SelectionCommitted;

    public event EventHandler? Cancelled;

    public CaptureMonitor Monitor => _monitor;

    /// <summary>True once a region is chosen and the window is accepting annotations.</summary>
    private bool IsAnnotating => _selection is not null;

    public async Task ShowAsync()
    {
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(_monitorFrame.ToSoftwareBitmap());
        PreviewImage.Source = source;
        BuildToolButtons();
        LoadStyle();

        // Covers both finishing and cancelling: the owner closes every overlay either
        // way, and a colour picked but not used is still the colour the user wants.
        Closed += (_, _) => PersistStyle();

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
        SelectionCanvas.CapturePointer(e.Pointer);

        if (IsAnnotating)
        {
            _editor.PointerPressed(ToFrame(e), ToModifiers(e));
            RenderAnnotations();
            return;
        }

        _selectionStart = e.GetCurrentPoint(SelectionCanvas).Position;
        DrawMarquee(_selectionStart.Value, _selectionStart.Value);
    }

    private void SelectionCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (IsAnnotating)
        {
            if (e.Pointer.IsInContact)
            {
                _editor.PointerMoved(ToFrame(e), ToModifiers(e));
                RenderAnnotations();
            }

            return;
        }

        if (_selectionStart is { } start && e.Pointer.IsInContact)
        {
            DrawMarquee(start, e.GetCurrentPoint(SelectionCanvas).Position);
        }
    }

    private void SelectionCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        SelectionCanvas.ReleasePointerCaptures();

        if (IsAnnotating)
        {
            _editor.PointerReleased(ToFrame(e), ToModifiers(e));
            RenderAnnotations();
            return;
        }

        if (_selectionStart is not { } start)
        {
            return;
        }

        var end = e.GetCurrentPoint(SelectionCanvas).Position;
        DrawMarquee(start, end);
        _selectionStart = null;

        var region = _layout.PointerToFrame(_monitor, CaptureRegion.FromPoints(start.X, start.Y, end.X, end.Y));
        if (!region.IsEmpty)
        {
            EnterAnnotationPhase(region);
        }
    }

    private void EnterAnnotationPhase(CaptureRegion region)
    {
        _selection = region;

        // The preview covers the selection with the pixels that will be delivered,
        // which also hides the selection tint inside it: from here on, what is inside
        // the marquee is the finished image rather than a tinted approximation of it.
        _annotationPreview = new RasterAnnotationPreview(
            AnnotationLayer,
            _layout,
            _monitor,
            NativeScreenCaptureService.Crop(_desktopFrame, region),
            region);

        AnnotationToolbar.Visibility = Visibility.Visible;
        HintText.Text = "Draw to annotate • Ctrl+Z undo • Enter to finish • Esc to cancel";

        // The other displays' overlays are always on top, so they have to go before
        // the user can see anything but this one.
        SelectionCommitted?.Invoke(this, EventArgs.Empty);
    }

    private void OverlayRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = IsDown(VirtualKey.Control);
        var shift = IsDown(VirtualKey.Shift);

        switch (e.Key)
        {
            case VirtualKey.Escape:
                e.Handled = true;

                // The first Escape abandons a half-drawn mark; only an Escape with
                // nothing in flight throws the whole capture away.
                if (_editor.Cancel())
                {
                    RenderAnnotations();
                    return;
                }

                // Cancelling ends the whole capture, not just this display's overlay,
                // so the owner tears every window down instead of this one closing
                // itself and stranding the overlays on the other monitors.
                Cancelled?.Invoke(this, EventArgs.Empty);
                return;

            case VirtualKey.Enter when IsAnnotating:
                e.Handled = true;
                Complete();
                return;

            case VirtualKey.Delete or VirtualKey.Back when IsAnnotating:
                e.Handled = true;
                if (_editor.DeleteSelected())
                {
                    RenderAnnotations();
                }

                return;

            case VirtualKey.Z when control:
                e.Handled = true;
                _ = shift ? _editor.Redo() : _editor.Undo();
                RenderAnnotations();
                return;

            case VirtualKey.Y when control:
                e.Handled = true;
                _editor.Redo();
                RenderAnnotations();
                return;

            default:
                return;
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        _editor.Undo();
        RenderAnnotations();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        _editor.Redo();
        RenderAnnotations();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => Complete();

    private async void ReadText_Click(object sender, RoutedEventArgs e)
    {
        await RunRecognitionAsync(lines =>
        {
            var window = new TextRecognitionWindow(TextRecognizer.ToText(lines));

            // The overlay is always on top, so the results window would open behind
            // it. Reading the text ends the capture, the same way it does on macOS.
            Cancelled?.Invoke(this, EventArgs.Empty);
            window.Activate();
        });
    }

    private async void RedactPii_Click(object sender, RoutedEventArgs e)
    {
        await RunRecognitionAsync(lines =>
        {
            var annotations = AutoRedactor.Redact(lines);
            if (annotations.Count == 0)
            {
                // Silence here would be indistinguishable from a broken button, and
                // "nothing found" is a useful answer on a screenshot about to be
                // shared.
                HintText.Text = "No personal data found in the selection";
                return;
            }

            // One AddRange rather than a loop, so a single Ctrl+Z takes the whole
            // run back off. This is what the document's snapshot history buys.
            _editor.Document.AddRange(annotations);
            RenderAnnotations();
            HintText.Text = $"Redacted {annotations.Count} • Ctrl+Z to undo • Enter to finish";
        });
    }

    /// <summary>
    /// Runs OCR over the selection and hands the result to the caller. Failures land
    /// in the hint line rather than a dialog: the overlay is a borderless
    /// always-on-top window covering the screen, and it already has somewhere to say
    /// things.
    /// </summary>
    private async Task RunRecognitionAsync(Action<IReadOnlyList<RecognizedLine>> handle)
    {
        if (_selection is not { } region)
        {
            return;
        }

        var previousHint = HintText.Text;
        HintText.Text = "Reading text...";
        try
        {
            var frame = NativeScreenCaptureService.Crop(_desktopFrame, region);
            var lines = await TextRecognizer.RecognizeAsync(frame, region.X, region.Y);
            HintText.Text = previousHint;
            handle(lines);
        }
        catch (Exception exception)
        {
            HintText.Text = exception.Message;
        }
    }

    /// <summary>
    /// Delivers the pixels the preview is already showing. There is no separate
    /// export render: the preview was produced by the Core rasterizer at capture
    /// resolution over this exact crop, so re-rendering could only introduce a
    /// difference between what was approved and what is handed over.
    /// </summary>
    private void Complete()
    {
        if (_annotationPreview is null)
        {
            return;
        }

        // An in-flight mark is not part of the capture, and the preview still shows
        // it, so the draft is dropped and the preview brought back into agreement
        // before its pixels are taken.
        if (_editor.Cancel())
        {
            RenderAnnotations();
        }

        CaptureCompleted?.Invoke(this, _annotationPreview.ToFrame());
    }

    private void BuildToolButtons()
    {
        foreach (var tool in AnnotationRasterizer.SupportedTools)
        {
            var button = new ToggleButton
            {
                Content = Label(tool),
                Tag = tool,
                IsChecked = tool == _editor.Tool,
            };
            button.Click += ToolButton_Click;
            _toolButtons[tool] = button;
            ToolButtons.Children.Add(button);
        }
    }

    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: AnnotationTool tool })
        {
            return;
        }

        _editor.Tool = tool;

        // Behaves as a radio group: a tool is always active, so re-clicking the
        // current tool must not leave the toolbar with nothing selected.
        foreach (var (candidate, button) in _toolButtons)
        {
            button.IsChecked = candidate == tool;
        }

        RenderAnnotations();
    }

    /// <summary>
    /// Fills the style controls from the remembered style. The flag keeps the change
    /// handlers from writing a half-initialized style back while this runs: setting
    /// the colour before the slider has a value would otherwise commit a stroke width
    /// of zero.
    /// </summary>
    private void LoadStyle()
    {
        _isLoadingStyle = true;
        try
        {
            _loadedStyle = _settings.Current.ToAnnotationStyle();
            _editor.Style = _loadedStyle;

            LineStyleBox.ItemsSource = Enum.GetValues<LineStyle>().Select(style => style.ToString()).ToList();
            LineStyleBox.SelectedIndex = (int)_loadedStyle.LineStyle;
            StrokeWidthSlider.Value = _loadedStyle.StrokeWidth;
            StyleColorPicker.Color = ToUiColor(_loadedStyle.Color);
        }
        finally
        {
            _isLoadingStyle = false;
        }

        UpdateColorSwatch();
    }

    private void StyleColor_Changed(ColorPicker sender, ColorChangedEventArgs args) => ApplyStyle();

    private void StrokeWidth_Changed(object sender, RangeBaseValueChangedEventArgs e) => ApplyStyle();

    private void LineStyle_Changed(object sender, SelectionChangedEventArgs e) => ApplyStyle();

    /// <summary>
    /// The style applies to marks drawn from now on. Restyling what is already on the
    /// canvas would need a selection, which is a separate feature.
    /// </summary>
    private void ApplyStyle()
    {
        // SelectionChanged fires while the XAML tree is still being built, before the
        // other controls this reads from exist.
        if (_isLoadingStyle || StyleColorPicker is null || StrokeWidthSlider is null)
        {
            return;
        }

        var color = StyleColorPicker.Color;
        _editor.Style = new AnnotationStyle(
            new AnnotationColor(color.R, color.G, color.B, color.A),
            Math.Max(1, StrokeWidthSlider.Value),
            LineStyleBox.SelectedIndex >= 0 ? (LineStyle)LineStyleBox.SelectedIndex : LineStyle.Solid);
        UpdateColorSwatch();
    }

    private void UpdateColorSwatch() =>
        ColorSwatch.Background = new SolidColorBrush(ToUiColor(_editor.Style.Color));

    /// <summary>
    /// Remembers the style for the next capture. A failure here is swallowed on
    /// purpose: this runs while the overlay is being torn down, there is no window
    /// left to report into, and the cost of losing it is that the next capture starts
    /// from the previous colour.
    /// </summary>
    private void PersistStyle()
    {
        if (_editor.Style == _loadedStyle)
        {
            return;
        }

        try
        {
            _settings.Save(_settings.Current.WithAnnotationStyle(_editor.Style));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static Color ToUiColor(AnnotationColor color) =>
        new() { A = color.Alpha, R = color.Red, G = color.Green, B = color.Blue };

    private static string Label(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Arrow => "Arrow",
        AnnotationTool.Rectangle => "Box",
        AnnotationTool.Ellipse => "Ellipse",
        AnnotationTool.Line => "Line",
        AnnotationTool.Pencil => "Pen",
        AnnotationTool.Marker => "Marker",
        AnnotationTool.FilledRectangle => "Redact",
        AnnotationTool.Pixelate => "Pixelate",
        AnnotationTool.Blur => "Blur",
        _ => tool.ToString(),
    };

    private void RenderAnnotations() => _annotationPreview?.Render(_editor.VisibleAnnotations);

    private CapturePoint ToFrame(PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(SelectionCanvas).Position;
        return _layout.PointerToFrame(_monitor, position.X, position.Y);
    }

    private static EditorModifiers ToModifiers(PointerRoutedEventArgs e) =>
        e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) ? EditorModifiers.Constrain : EditorModifiers.None;

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    /// <summary>Draws the marquee, which stays in layout units because it is chrome.</summary>
    private void DrawMarquee(Point start, Point end)
    {
        var region = CaptureRegion.FromPoints(start.X, start.Y, end.X, end.Y);
        Canvas.SetLeft(SelectionRectangle, region.X);
        Canvas.SetTop(SelectionRectangle, region.Y);
        SelectionRectangle.Width = region.Width;
        SelectionRectangle.Height = region.Height;
        SelectionRectangle.Visibility = Visibility.Visible;
    }
}
