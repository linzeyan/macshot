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
using Microsoft.UI.Xaml.Shapes;

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
    /// <summary>
    /// The standing instruction, restored whenever a transient message — a text entry
    /// prompt, a redaction count, an error — has had its turn.
    /// </summary>
    private const string AnnotationHint = "Draw to annotate • Ctrl+Z undo • Enter to finish • Esc to cancel";

    /// <summary>The standing instruction before anything is chosen. Matches the XAML default.</summary>
    private const string SelectionHint = "Drag to capture • Click a window to take it • Esc to cancel";

    /// <summary>
    /// Shown while a window is highlighted. Scroll capture is otherwise unfindable:
    /// there is no toolbar yet at hover time, and a gesture nobody is told about is
    /// a feature nobody has.
    /// </summary>
    private const string WindowHint = "Click to take this window • Shift+click to scroll-capture it • Esc to cancel";

    /// <summary>
    /// How far, in layout units, the pointer may travel between press and release
    /// and still count as a click rather than a drag.
    /// </summary>
    private const double ClickSlop = 4;

    private readonly CapturedFrame _desktopFrame;
    private readonly MonitorLayout _layout;
    private readonly CaptureMonitor _monitor;
    private readonly CapturedFrame _monitorFrame;
    private readonly SettingsStore _settings;
    private readonly IReadOnlyList<CaptureWindow> _snapCandidates;

    /// <summary>
    /// Takes one window as its own capture, or answers null when Windows cannot.
    /// Injected because the service behind it belongs to the controller and
    /// outlives every overlay.
    /// </summary>
    private readonly Func<long, Task<CapturedFrame?>> _captureWindow;
    private readonly AnnotationEditor _editor = new(new AnnotationDocument());
    private readonly Dictionary<AnnotationTool, ToggleButton> _toolButtons = [];

    private RasterAnnotationPreview? _annotationPreview;
    private Point? _selectionStart;
    private CaptureRegion? _selection;

    /// <summary>The window under the pointer, in frame space, while none is chosen yet.</summary>
    private CaptureWindow? _hoveredWindow;
    private TextBox? _textEntry;
    private CapturePoint _textEntryOrigin;
    private string _stampEmoji = StampGlyph.Default;

    /// <summary>
    /// The sprite placement still rasterizing, if any. Producing a sprite is async, so
    /// finishing the capture has to wait for it: without this, clicking Done right
    /// after typing would deliver an image missing the text that click committed.
    /// </summary>
    private Task _pendingSprite = Task.CompletedTask;

    /// <summary>The style the toolbar started from, so only a real change is written back.</summary>
    private AnnotationStyle _loadedStyle = AnnotationStyle.Default;

    private bool _isLoadingStyle;

    public CaptureOverlayWindow(
        CapturedFrame desktopFrame,
        MonitorLayout layout,
        CaptureMonitor monitor,
        SettingsStore settings,
        IReadOnlyList<CaptureWindow> snapCandidates,
        Func<long, Task<CapturedFrame?>> captureWindow)
    {
        _desktopFrame = desktopFrame ?? throw new ArgumentNullException(nameof(desktopFrame));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _snapCandidates = snapCandidates ?? throw new ArgumentNullException(nameof(snapCandidates));
        _captureWindow = captureWindow ?? throw new ArgumentNullException(nameof(captureWindow));
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

    /// <summary>
    /// Raised when the user asks for a window to be scroll-captured. The overlay
    /// hands over the window rather than running it: scroll capture hides every
    /// overlay and drives the desktop, which is the owner's business, not one
    /// display's.
    /// </summary>
    public event EventHandler<CaptureWindow>? ScrollCaptureRequested;

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
            // Sprite tools are placed with a click rather than dragged out: their size
            // comes from the rasterized pixels, so there is nothing left for a drag to
            // decide. The editor is deliberately not told about the press, which is
            // what keeps its move and release handlers no-ops.
            if (Annotation.RequiresSprite(_editor.Tool))
            {
                PlaceSprite(ToFrame(e));
                return;
            }

            _editor.PointerPressed(ToFrame(e), ToModifiers(e));
            RenderAnnotations();
            return;
        }

        _selectionStart = e.GetCurrentPoint(SelectionCanvas).Position;

        // The highlight has done its job the moment a drag begins: what the pointer
        // is over stops mattering once the user is drawing their own edges.
        SnapHighlight.Visibility = Visibility.Collapsed;
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
            return;
        }

        TrackHoveredWindow(ToFrame(e));
    }

    /// <summary>
    /// Offers the window under the pointer, so a click can take it whole. The
    /// candidates were enumerated when the screenshot was, which is what lets this
    /// run on every pointer move without asking Windows anything.
    /// </summary>
    private void TrackHoveredWindow(CapturePoint point)
    {
        _hoveredWindow = WindowSnapper.Snap(_snapCandidates, point, FrameBounds);
        if (_hoveredWindow is not { } window)
        {
            SnapHighlight.Visibility = Visibility.Collapsed;
            HintText.Text = SelectionHint;
            return;
        }

        PlaceChrome(SnapHighlight, window.Bounds);
        HintText.Text = WindowHint;
    }

    /// <summary>
    /// Puts a piece of overlay chrome over a frame-space region. Chrome is laid out
    /// in this display's layout units while the region is in desktop pixels, so it
    /// has to come back through the same per-display scale input went out through.
    /// </summary>
    private void PlaceChrome(Rectangle target, CaptureRegion region)
    {
        var origin = _layout.FrameToPointer(_monitor, new CapturePoint(region.X, region.Y));
        Canvas.SetLeft(target, origin.X);
        Canvas.SetTop(target, origin.Y);
        target.Width = region.Width / _monitor.Scale;
        target.Height = region.Height / _monitor.Scale;
        target.Visibility = Visibility.Visible;
    }

    /// <summary>The whole capture, in frame space: what a window rect is clipped to.</summary>
    private CaptureRegion FrameBounds =>
        new(0, 0, _layout.VirtualBounds.Width, _layout.VirtualBounds.Height);

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

        var dragged = CaptureRegion.FromPoints(start.X, start.Y, end.X, end.Y);

        // A press that never became a drag is a click on the highlighted window.
        // The slop is what makes that reachable: a click carries a pixel or two of
        // movement, and without it the user would get a 2px selection instead of
        // the window they were being shown.
        if (dragged.Width < ClickSlop && dragged.Height < ClickSlop)
        {
            if (_hoveredWindow is { } window && !window.Bounds.IsEmpty)
            {
                // Shift asks for the whole page rather than the screenful of it that
                // is showing. The window is the same one either way, which is why the
                // gesture hangs off the same click rather than off a mode.
                if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift))
                {
                    ScrollCaptureRequested?.Invoke(this, window);
                    return;
                }

                // Awaited inside rather than by the caller: a pointer event handler
                // cannot be, and the capture it waits on is the only asynchronous
                // step between the click and the annotation phase.
                _ = EnterSnappedWindowPhaseAsync(window);
            }

            return;
        }

        var region = _layout.PointerToFrame(_monitor, dragged);
        if (!region.IsEmpty)
        {
            EnterAnnotationPhase(region);
        }
    }

    /// <summary>
    /// Enters the annotation phase on a clicked window, taking the window's own
    /// pixels when Windows will give them and the frozen screenshot when it will
    /// not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A capture of the window itself is the better image — no dialog and no
    /// neighbouring app sitting over it — but only if it is the same image. It is
    /// accepted only at the size the highlight promised, because the annotation
    /// phase maps what the user draws onto this buffer through the selection
    /// region. A window that resized between the screenshot and the click would put
    /// every mark in the wrong place, and delivering what they were looking at is
    /// the honest answer to that.
    /// </para>
    /// <para>
    /// Falling back is said out loud, because it is not only a worse capture but a
    /// different one: the screenshot crop still has whatever was covering the
    /// window in it, and that is about to be saved.
    /// </para>
    /// </remarks>
    private async Task EnterSnappedWindowPhaseAsync(CaptureWindow window)
    {
        CapturedFrame? captured = null;

        try
        {
            captured = await _captureWindow(window.Id);

            if (captured is not null
                && (captured.Width != (int)Math.Round(window.Bounds.Width)
                    || captured.Height != (int)Math.Round(window.Bounds.Height)))
            {
                captured = null;
            }

            EnterAnnotationPhase(window.Bounds, captured);

            if (captured is null)
            {
                HintText.Text = "Captured from the screen, so anything over the window is included";
            }
        }
        catch (Exception exception)
        {
            // Nothing may escape: this runs on a task nobody holds, where an
            // unobserved exception ends the process rather than the capture. The
            // hint is where this overlay reports every other failure too.
            HintText.Text = exception.Message;
        }
    }

    private void EnterAnnotationPhase(CaptureRegion region, CapturedFrame? capturedWindow = null)
    {
        _selection = region;
        _hoveredWindow = null;
        SnapHighlight.Visibility = Visibility.Collapsed;

        // Brings the marquee onto the region actually taken, which is the whole
        // point when the region came from a snapped window rather than from a drag
        // that already left it in the right place.
        PlaceChrome(SelectionRectangle, region);

        // The preview covers the selection with the pixels that will be delivered,
        // which also hides the selection tint inside it: from here on, what is inside
        // the marquee is the finished image rather than a tinted approximation of it.
        _annotationPreview = new RasterAnnotationPreview(
            AnnotationLayer,
            _layout,
            _monitor,
            capturedWindow ?? NativeScreenCaptureService.Crop(_desktopFrame, region),
            region);

        AnnotationToolbar.Visibility = Visibility.Visible;
        HintText.Text = AnnotationHint;

        // The other displays' overlays are always on top, so they have to go before
        // the user can see anything but this one.
        SelectionCommitted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Starts placing the active sprite tool's mark at <paramref name="point"/>. Text
    /// opens an entry box and commits later; the other two commit as soon as their
    /// glyphs are rasterized. See <c>docs/windows-port/architecture.md</c>, decision
    /// D7.
    /// </summary>
    private void PlaceSprite(CapturePoint point)
    {
        switch (_editor.Tool)
        {
            case AnnotationTool.Text:
                QueueSprite(() => ReplaceTextEntryAsync(point));
                return;
            case AnnotationTool.Number:
                QueueSprite(() => PlaceNumberAsync(point));
                return;
            case AnnotationTool.Stamp:
                QueueSprite(() => PlaceStampAsync(point));
                return;
            default:
                return;
        }
    }

    /// <summary>
    /// Runs sprite work behind whatever is already in flight, and keeps the tail of
    /// the chain so finishing the capture can wait for all of it.
    /// </summary>
    /// <remarks>
    /// Sprites are rasterized asynchronously, so two placements a moment apart would
    /// otherwise interleave: clicking away from a half-typed label would open the next
    /// entry box before the previous one had committed, and the first label would be
    /// lost. Ordering them is cheaper than making each one defend itself.
    /// </remarks>
    private void QueueSprite(Func<Task> work) => _pendingSprite = RunAfterAsync(_pendingSprite, work);

    private async Task RunAfterAsync(Task previous, Func<Task> work)
    {
        try
        {
            await previous;
            await work();
        }
        catch (Exception exception)
        {
            // Failures land in the hint line for the same reason the recognition ones
            // do: the overlay is a borderless always-on-top window covering the
            // screen, so a dialog has nowhere to go. Catching here also keeps one
            // failed placement from poisoning every later one that waits on it.
            HintText.Text = exception.Message;
        }
    }

    /// <summary>
    /// Finishes whatever was being typed before starting the next label, so clicking
    /// elsewhere moves the text on rather than abandoning it.
    /// </summary>
    private async Task ReplaceTextEntryAsync(CapturePoint point)
    {
        await CommitTextEntryAsync();
        BeginTextEntry(point);
    }

    /// <summary>
    /// Places a numbered badge centred on <paramref name="point"/>. Rasterizing the
    /// digits is async, so the badge appears a frame or two after the click — which
    /// is exactly why a sprite is produced once when the annotation is committed and
    /// never from inside the draw path.
    /// </summary>
    private async Task PlaceNumberAsync(CapturePoint point)
    {
        var style = _editor.Style;

        // The next number is read off the document rather than kept in a counter, so
        // undoing a badge frees its number instead of leaving a hole in the sequence.
        var value = _editor.Document.Annotations.Count(existing => existing.Tool == AnnotationTool.Number) + 1;

        var badge = NumberBadge.Build(value, style, RasterizationScale);
        var sprite = await GlyphSpriteFactory.RenderAsync(SpriteHost, badge);

        Commit(Annotation.CreateSprite(AnnotationTool.Number, Centred(point, sprite), sprite, style) with
        {
            // Kept alongside the pixels so the badge stays readable as data, not only
            // as an image.
            NumberValue = value,
        });
    }

    private async Task PlaceStampAsync(CapturePoint point)
    {
        var style = _editor.Style;
        var emoji = _stampEmoji;

        var glyph = StampGlyph.Build(emoji, style, RasterizationScale);
        var sprite = await GlyphSpriteFactory.RenderAsync(SpriteHost, glyph);

        Commit(Annotation.CreateSprite(AnnotationTool.Stamp, Centred(point, sprite), sprite, style) with
        {
            Text = emoji,
        });
    }

    /// <summary>
    /// Opens the on-canvas entry box. The box is what makes the text tool feel like
    /// typing on the screenshot rather than filling in a dialog, and it uses the same
    /// font size the sprite will, so what is typed is what is committed.
    /// </summary>
    private void BeginTextEntry(CapturePoint point)
    {
        var position = _layout.FrameToPointer(_monitor, point);
        var entry = new TextBox
        {
            MinWidth = 120,

            // No padding, so the first glyph sits at the click point rather than
            // inset from it by whatever the theme's padding happens to be.
            Padding = new Thickness(0),
            FontSize = TextGlyphs.FontSizeFor(_editor.Style, RasterizationScale),
            Foreground = new SolidColorBrush(GlyphSpriteFactory.ToBrushColor(_editor.Style)),
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
        };

        entry.KeyDown += TextEntry_KeyDown;
        entry.LostFocus += TextEntry_LostFocus;
        Canvas.SetLeft(entry, position.X);
        Canvas.SetTop(entry, position.Y);
        TextEntryLayer.Children.Add(entry);
        _textEntry = entry;
        _textEntryOrigin = point;
        entry.Focus(FocusState.Programmatic);
        HintText.Text = "Type the label • Enter to place • Esc to discard it";
    }

    private void TextEntry_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                // Handled here, or it would bubble up and finish the whole capture.
                e.Handled = true;
                QueueSprite(CommitTextEntryAsync);
                return;

            case VirtualKey.Escape:
                // The first Escape discards the text being typed, the same way it
                // discards a half-drawn mark before it cancels the capture.
                e.Handled = true;
                RemoveTextEntry();
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// Clicking anywhere else — another tool, the canvas, Done — means the text is
    /// finished, so it is committed rather than lost.
    /// </summary>
    private void TextEntry_LostFocus(object sender, RoutedEventArgs e) => QueueSprite(CommitTextEntryAsync);

    private async Task CommitTextEntryAsync()
    {
        if (_textEntry is not { } entry)
        {
            return;
        }

        var text = entry.Text.Trim();
        var origin = _textEntryOrigin;
        var style = _editor.Style;

        // Torn down before the await, so the LostFocus that removing the box raises
        // cannot commit the same text a second time.
        RemoveTextEntry();
        if (text.Length == 0)
        {
            return;
        }

        var glyphs = TextGlyphs.Build(text, style, RasterizationScale);
        var sprite = await GlyphSpriteFactory.RenderAsync(SpriteHost, glyphs);

        // Anchored at the click rather than centred on it: the user typed from there.
        Commit(Annotation.CreateSprite(AnnotationTool.Text, origin, sprite, style) with { Text = text });
    }

    private void RemoveTextEntry()
    {
        if (_textEntry is not { } entry)
        {
            return;
        }

        _textEntry = null;
        entry.KeyDown -= TextEntry_KeyDown;
        entry.LostFocus -= TextEntry_LostFocus;
        TextEntryLayer.Children.Remove(entry);

        // The keyboard belongs to the overlay again: Enter finishes, Ctrl+Z undoes.
        OverlayRoot.Focus(FocusState.Programmatic);
        HintText.Text = AnnotationHint;
    }

    private void Commit(Annotation annotation)
    {
        _editor.Document.Add(annotation);
        RenderAnnotations();
    }

    /// <summary>
    /// The scale XAML will actually rasterize at, which is what decides how many
    /// pixels a sprite comes out as. The display's own scale is the fallback for the
    /// window not being in a tree yet, which cannot happen from a pointer event but
    /// keeps the sizing honest rather than silently defaulting to 1.
    /// </summary>
    private double RasterizationScale => OverlayRoot.XamlRoot?.RasterizationScale ?? _monitor.Scale;

    /// <summary>
    /// A mark aimed at a point belongs centred on it; anchoring its top-left there
    /// would drop it down and right of what the user aimed at.
    /// </summary>
    private static CapturePoint Centred(CapturePoint point, AnnotationSprite sprite) =>
        new(point.X - (sprite.Width / 2.0), point.Y - (sprite.Height / 2.0));

    private void OverlayRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // While the entry box has focus the keyboard is its: Delete edits the text
        // instead of deleting an annotation, and Ctrl+Z takes back typing. Enter and
        // Escape are handled on the box itself, which is why they never arrive here.
        if (_textEntry is not null)
        {
            return;
        }

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
                _ = CompleteAsync();
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

    private async void Confirm_Click(object sender, RoutedEventArgs e) => await CompleteAsync();

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
    private async Task CompleteAsync()
    {
        if (_annotationPreview is null)
        {
            return;
        }

        // Text still in the entry box is part of the capture the moment the user
        // finishes it, and clicking Done is finishing it. Every queued placement has
        // to land before the pixels are taken, or the delivered image is missing a
        // mark the user made.
        QueueSprite(CommitTextEntryAsync);
        await _pendingSprite;

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

        StampChoices.ItemsSource = StampGlyph.Choices;
        StampButton.Content = _stampEmoji;
    }

    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: AnnotationTool tool })
        {
            SelectTool(tool);
        }
    }

    private void SelectTool(AnnotationTool tool)
    {
        _editor.Tool = tool;

        // Behaves as a radio group: a tool is always active, so re-clicking the
        // current tool must not leave the toolbar with nothing selected.
        foreach (var (candidate, button) in _toolButtons)
        {
            button.IsChecked = candidate == tool;
        }

        RenderAnnotations();
    }

    private void StampChoice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (StampChoices.SelectedItem is not string emoji)
        {
            return;
        }

        _stampEmoji = emoji;
        StampButton.Content = emoji;
        StampButton.Flyout?.Hide();

        // Picking a stamp is asking to stamp: leaving the previous tool active would
        // make the choice look like it did nothing.
        if (_toolButtons.ContainsKey(AnnotationTool.Stamp))
        {
            SelectTool(AnnotationTool.Stamp);
        }
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
