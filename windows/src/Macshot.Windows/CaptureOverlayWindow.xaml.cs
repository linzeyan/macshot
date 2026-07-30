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

    /// <summary>
    /// The same, for a region that can still be adjusted. The grips are visible on their
    /// own, so what the line has to name is the arrow keys, which nothing advertises.
    /// </summary>
    private const string AdjustableAnnotationHint =
        "Draw to annotate • Drag a grip or arrow-key to adjust • Ctrl+Z undo • Enter to finish";

    /// <summary>The standing instruction before anything is chosen. Matches the XAML default.</summary>
    private const string SelectionHint = "Drag to capture • Click a window to take it • Esc to cancel";

    private const string SamplingHint = "Click to take the colour under the pointer • Esc to stop";

    private const string RememberedHint =
        "Enter to take the last selection again • Drag for a new one • Esc to cancel";

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
    private readonly Dictionary<SelectionHandle, Rectangle> _grips = [];

    private RasterAnnotationPreview? _annotationPreview;
    private Point? _selectionStart;
    private CaptureRegion? _selection;

    /// <summary>
    /// The region the last capture was taken from, offered on the display it was
    /// drawn on. Null on every other overlay, and once the offer has been taken or
    /// drawn over.
    /// </summary>
    private CaptureRegion? _remembered;

    /// <summary>
    /// True while the colour sampler is armed, which makes the next click a pick
    /// rather than a mark.
    /// </summary>
    private bool _samplingColor;

    /// <summary>
    /// Whether the chosen region can still be changed, which it can when its pixels
    /// were cropped out of the screenshot and cannot when they are a window's own.
    /// </summary>
    /// <remarks>
    /// A window capture has exactly one region with pixels behind it: the window's.
    /// Re-cropping the screenshot for an adjusted one would quietly swap the delivered
    /// image for a different photograph of the same place, with whatever was covering
    /// the window back in it. A window capture is the window; a different rectangle is
    /// a drag.
    /// </remarks>
    private bool _regionIsAdjustable;

    /// <summary>The grip being dragged, or <see cref="SelectionHandle.None"/>.</summary>
    private SelectionHandle _resizing = SelectionHandle.None;

    /// <summary>
    /// The region the current grip drag started from. Kept still for the whole drag
    /// because a resize is resolved against the corner opposite the grip: feeding the
    /// region back in as it changes would move that corner too, so a drag past the far
    /// side would crawl after the pointer instead of flipping.
    /// </summary>
    private CaptureRegion _resizeFrom;

    /// <summary>The window under the pointer, in frame space, while none is chosen yet.</summary>
    private CaptureWindow? _hoveredWindow;
    private TextBox? _textEntry;
    private CapturePoint _textEntryOrigin;

    /// <summary>
    /// The sprite placement still rasterizing, if any. Producing a sprite is async, so
    /// finishing the capture has to wait for it: without this, clicking Done right
    /// after typing would deliver an image missing the text that click committed.
    /// </summary>
    private Task _pendingSprite = Task.CompletedTask;

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
        BuildGrips();
        WireToolbar();

        // Covers both finishing and cancelling: the owner closes every overlay either
        // way, and a colour picked but not used is still the colour the user wants.
        Closed += (_, _) => AnnotationToolbar.PersistStyle();

        var appWindow = this.GetAppWindow();
        var presenter = appWindow.MakeChromeless();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;

        // AppWindow positions in physical pixels, so the display's virtual-space
        // bounds go in unchanged. Converting to layout units here would misplace
        // the overlay on every display that is not at 100%.
        appWindow.MoveAndResize(new RectInt32(
            (int)_monitor.Bounds.X,
            (int)_monitor.Bounds.Y,
            (int)_monitor.Bounds.Width,
            (int)_monitor.Bounds.Height));
        this.TakeForeground();
        OverlayRoot.Focus(FocusState.Programmatic);
        OfferRememberedSelection();
    }

    /// <summary>
    /// Draws the last capture's region where it was, on the display it was drawn on,
    /// and offers Enter as the way to take it again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Offered rather than applied. Entering the annotation phase outright would take
    /// a capture nobody asked for and close every other display's overlay to do it,
    /// so a remembered selection is a proposal the user accepts by pressing Enter —
    /// and ignores by dragging, which is exactly what they would have done anyway.
    /// </para>
    /// <para>
    /// Nothing is adjustable yet at this point: the grips arrive with the annotation
    /// phase, so a remembered region is taken first and then trimmed, rather than
    /// trimmed and then taken. Which is the same two steps in the same order as a drag.
    /// </para>
    /// </remarks>
    private void OfferRememberedSelection()
    {
        var monitorFrame = _layout.FrameRegionOf(_monitor);
        var remembered = _settings.Current.RememberedSelectionFor(
            _monitor.DeviceName,
            (int)monitorFrame.Width,
            (int)monitorFrame.Height);

        if (remembered is not { } local)
        {
            return;
        }

        // Stored relative to its own display, so that rearranging the monitors moves
        // the selection with the display it belongs to rather than leaving it at a
        // virtual-desktop coordinate that now points somewhere else entirely.
        _remembered = new CaptureRegion(
            local.X + monitorFrame.X,
            local.Y + monitorFrame.Y,
            local.Width,
            local.Height);

        PlaceChrome(SelectionRectangle, _remembered.Value);
        HintText.Text = RememberedHint;

        DiagnosticLog.Verbose(
            $"offering last selection on {_monitor.DeviceName}: stored {local.X},{local.Y} "
                + $"{local.Width}x{local.Height} placed at {_remembered.Value.X},{_remembered.Value.Y}");
    }

    /// <summary>
    /// Stores the region this capture was taken from, so the next one can offer it
    /// back.
    /// </summary>
    /// <remarks>
    /// Written when the capture completes rather than when the region is chosen: a
    /// selection drawn and then abandoned with Escape is not the one to come back to.
    /// </remarks>
    private void RememberSelection(CaptureRegion region)
    {
        if (!_settings.Current.RememberLastSelection)
        {
            return;
        }

        var monitorFrame = _layout.FrameRegionOf(_monitor);
        var local = new CaptureRegion(
            region.X - monitorFrame.X,
            region.Y - monitorFrame.Y,
            region.Width,
            region.Height);

        try
        {
            _settings.Save(_settings.Current.WithLastSelection(local, _monitor.DeviceName));
        }
        catch (Exception exception)
        {
            // The capture is already made and about to be delivered. Failing to write
            // a convenience for the next one is not a reason to interrupt this one.
            DiagnosticLog.Write($"Could not remember the selection: {exception.Message}");
        }
    }

    private void SelectionCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        SelectionCanvas.CapturePointer(e.Pointer);

        // Ahead of everything else: while the sampler is armed the click is the pick,
        // and must not also start a mark or a selection under it.
        if (_samplingColor)
        {
            TakeSampledColor(ToFrame(e));
            return;
        }

        if (IsAnnotating)
        {
            // Ahead of the tools: the grips are drawn over the selection's edge, so a
            // press on one is aimed at the grip, not through it at the canvas below.
            if (GrabGrip(ToFrame(e)))
            {
                return;
            }

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

        // The offer of the last selection ends the moment the user reaches for the
        // pointer, whether that turns out to be a drag or a click on a window. Leaving
        // it live would let Enter take a region that is no longer the one on screen.
        _remembered = null;

        // The highlight has done its job the moment a drag begins: what the pointer
        // is over stops mattering once the user is drawing their own edges.
        SnapHighlight.Visibility = Visibility.Collapsed;
        DrawMarquee(_selectionStart.Value, _selectionStart.Value);
    }

    private void SelectionCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_samplingColor)
        {
            // Reading it out as it moves is what makes the tool usable at all: on a
            // gradient or a photograph, the pixel under the pointer is not the colour
            // the eye reports, and there is no way to tell before committing to it.
            HintText.Text = $"{SamplingHint} • {SampleAt(ToFrame(e)).ToHex()}";
            return;
        }

        if (_resizing != SelectionHandle.None)
        {
            if (e.Pointer.IsInContact)
            {
                ShowPendingRegion(ResizedTo(e));
            }
            else
            {
                AbandonResize();
            }

            return;
        }

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

            // Back to whichever hint this overlay started with: an offered selection
            // is still on offer after the pointer has passed over a window.
            HintText.Text = _remembered is null ? SelectionHint : RememberedHint;
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

        if (_resizing != SelectionHandle.None)
        {
            var resized = ResizedTo(e);
            _resizing = SelectionHandle.None;
            ApplyRegion(resized);
            return;
        }

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
        // Where the annotation phase's pixels came from, which is the difference between
        // "the window itself" and "the screenshot with whatever was over it". The two
        // look identical unless something was covering the window.
        DiagnosticLog.Verbose(
            $"annotating {region.Width}x{region.Height} at {region.X},{region.Y} on {_monitor.DeviceName}, "
                + (capturedWindow is { } window
                    ? $"from the window's own pixels ({window.Width}x{window.Height})"
                    : "cropped from the screenshot"));

        _selection = region;
        _hoveredWindow = null;
        _regionIsAdjustable = capturedWindow is null;
        SnapHighlight.Visibility = Visibility.Collapsed;

        // Brings the marquee onto the region actually taken, which is the whole
        // point when the region came from a snapped window rather than from a drag
        // that already left it in the right place.
        PlaceChrome(SelectionRectangle, region);
        PlaceGrips(region);

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
        HintText.Text = AnnotatingHint;

        // The other displays' overlays are always on top, so they have to go before
        // the user can see anything but this one.
        SelectionCommitted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The standing instruction for the annotation phase this overlay is in.</summary>
    private string AnnotatingHint => _regionIsAdjustable ? AdjustableAnnotationHint : AnnotationHint;

    /// <summary>This display, in frame space: what an adjusted region is kept inside.</summary>
    /// <remarks>
    /// This display rather than the whole desktop. Choosing the region is what closes
    /// the other displays' overlays, so by the time a grip can be dragged there is
    /// nothing on the neighbouring screens showing what a selection reaching onto them
    /// would take.
    /// </remarks>
    private CaptureRegion MonitorBounds => _layout.FrameRegionOf(_monitor);

    /// <summary>
    /// Builds the eight grips once, hidden. Which of them a selection offers depends on
    /// its size, so they are shown and hidden rather than created and destroyed: a
    /// resize must cost a rasterize per pointer move, not an allocation.
    /// </summary>
    private void BuildGrips()
    {
        foreach (var handle in SelectionHandles.All)
        {
            var grip = new Rectangle
            {
                Visibility = Visibility.Collapsed,

                // The marquee's own blue filled in, outlined in white so a grip is
                // still findable against a blue window behind it.
                Fill = new SolidColorBrush(Color.FromArgb(255, 0x4C, 0xC2, 0xFF)),
                Stroke = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                StrokeThickness = 1,
            };

            _grips[handle] = grip;
            SelectionGrips.Children.Add(grip);
        }
    }

    /// <summary>
    /// Puts the grips on a region's edges, showing only the ones it is big enough to
    /// offer, and hides all of them when there is nothing to adjust.
    /// </summary>
    private void PlaceGrips(CaptureRegion? selection)
    {
        if (_regionIsAdjustable && selection is { } region)
        {
            var offered = SelectionHandles.For(region);
            foreach (var (handle, grip) in _grips)
            {
                if (offered.Contains(handle))
                {
                    PlaceChrome(grip, SelectionHandles.RectangleOf(region, handle));
                }
                else
                {
                    grip.Visibility = Visibility.Collapsed;
                }
            }

            return;
        }

        foreach (var grip in _grips.Values)
        {
            grip.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Starts a resize when <paramref name="point"/> is on one of the grips, and answers
    /// whether it did — a press that grabbed a grip is not also the start of a mark.
    /// </summary>
    private bool GrabGrip(CapturePoint point)
    {
        if (!_regionIsAdjustable || _selection is not { } region)
        {
            return false;
        }

        _resizing = SelectionHandles.HitTest(region, point);
        _resizeFrom = region;
        return _resizing != SelectionHandle.None;
    }

    /// <summary>
    /// The region the grip being dragged is asking for, kept on this display.
    /// </summary>
    /// <remarks>
    /// Clipped to the display rather than pushed back inside it: a resize that runs off
    /// the edge should stop the edge being dragged and leave the opposite one where the
    /// user put it, which is what an intersection does and what moving the whole
    /// rectangle would not.
    /// </remarks>
    private CaptureRegion ResizedTo(PointerRoutedEventArgs e) => SelectionHandles
        .Resize(_resizeFrom, _resizing, ToFrame(e), e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift))
        .Intersect(MonitorBounds);

    /// <summary>
    /// Shows where a region is heading mid-drag, without re-cropping.
    /// </summary>
    /// <remarks>
    /// The preview holds a buffer and a bitmap sized to its region, so rebuilding it on
    /// every pointer move would allocate two of each per frame for the sake of pixels
    /// that are about to be replaced. Until the grip is let go the pixels on show are
    /// the ones the preview already has, and the marquee is what says where the new
    /// edges are — which is the same thing the marquee does while the region is first
    /// being dragged out.
    /// </remarks>
    private void ShowPendingRegion(CaptureRegion region)
    {
        PlaceChrome(SelectionRectangle, region);
        PlaceGrips(region);
    }

    /// <summary>
    /// Drops a grip drag whose release never arrived — a lost pointer capture, or a
    /// button let go somewhere this window never hears about. Left held, the grip would
    /// take every later press and no mark could be drawn again.
    /// </summary>
    private void AbandonResize()
    {
        _resizing = SelectionHandle.None;
        if (_selection is { } current)
        {
            ShowPendingRegion(current);
        }
    }

    /// <summary>
    /// Takes an adjusted region: re-crops the pixels under it and rebuilds the preview
    /// around them, keeping every annotation already made.
    /// </summary>
    /// <remarks>
    /// Annotations are held in frame space, so they stay on the pixels they were drawn
    /// on rather than sliding with the crop. One the region no longer covers is clipped,
    /// which is what already happens to a mark drawn past the edge of the selection.
    /// </remarks>
    private void ApplyRegion(CaptureRegion region)
    {
        if (_selection is not { } current)
        {
            return;
        }

        // An empty region has no pixels to crop and nothing to annotate, so a grip
        // dragged exactly onto its opposite edge leaves the region as it was instead of
        // collapsing the capture. The chrome is put back either way: a drag that ends
        // here left the marquee showing the region that is not being taken.
        var taken = region.IsEmpty ? current : region;

        PlaceChrome(SelectionRectangle, taken);
        PlaceGrips(taken);

        if (taken == current)
        {
            return;
        }

        _selection = taken;
        _annotationPreview?.Detach();
        _annotationPreview = new RasterAnnotationPreview(
            AnnotationLayer,
            _layout,
            _monitor,
            NativeScreenCaptureService.Crop(_desktopFrame, taken),
            taken);
        RenderAnnotations();

        DiagnosticLog.Verbose(
            $"region adjusted to {taken.Width}x{taken.Height} at {taken.X},{taken.Y} on {_monitor.DeviceName}");
    }

    /// <summary>
    /// Moves the whole region by a key press, which is the only way to move it: dragging
    /// inside the selection draws a mark, and taking that gesture over would cost the
    /// tools the one thing they are for.
    /// </summary>
    private void NudgeRegion(VirtualKey key, bool far)
    {
        if (_selection is not { } region)
        {
            return;
        }

        // Ten pixels with Shift. One at a time is what a fine adjustment needs and is
        // uselessly slow for anything else.
        var step = far ? 10 : 1;
        var (deltaX, deltaY) = key switch
        {
            VirtualKey.Left => (-step, 0),
            VirtualKey.Right => (step, 0),
            VirtualKey.Up => (0, -step),
            _ => (0, step),
        };

        ApplyRegion(SelectionHandles.ClampTo(
            SelectionHandles.Translate(region, deltaX, deltaY),
            MonitorBounds));
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
        var emoji = AnnotationToolbar.StampEmoji;

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
        HintText.Text = AnnotatingHint;
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

                // An armed sampler is the first thing Escape gives up. It takes over
                // the click, so leaving it armed while Escape did something else would
                // leave the pointer doing nothing the user asked for.
                if (_samplingColor)
                {
                    SetColorSampling(false);
                    return;
                }

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

            case VirtualKey.Enter when _remembered is { } remembered:
                e.Handled = true;
                EnterAnnotationPhase(remembered);
                return;

            case VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down
                when IsAnnotating && _regionIsAdjustable:
                // Handled, or the arrow would move focus between the toolbar's buttons
                // instead, which is the one other thing arrow keys mean here.
                e.Handled = true;
                NudgeRegion(e.Key, shift);
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

    /// <summary>
    /// Binds the shared toolbar to this overlay's editor and answers the things only the
    /// overlay can: which pixels a colour sample comes from, and what Done means.
    /// </summary>
    private void WireToolbar()
    {
        AnnotationToolbar.Bind(_editor, _settings);
        AnnotationToolbar.Changed += (_, _) => RenderAnnotations();
        AnnotationToolbar.ColorSamplingToggled += (_, armed) => SetColorSampling(armed);
        AnnotationToolbar.ReadTextRequested += (_, _) => _ = ReadTextAsync();
        AnnotationToolbar.RedactRequested += (_, _) => _ = RedactPiiAsync();
        AnnotationToolbar.DoneRequested += (_, _) => _ = CompleteAsync();
    }

    private async Task ReadTextAsync()
    {
        await RunRecognitionAsync(lines =>
        {
            var window = new TextRecognitionWindow(TextRecognizer.ToText(lines), _settings);

            // The overlay is always on top, so the results window would open behind
            // it. Reading the text ends the capture, the same way it does on macOS.
            Cancelled?.Invoke(this, EventArgs.Empty);
            window.Activate();
        });
    }

    private async Task RedactPiiAsync()
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

        if (_selection is { } taken)
        {
            RememberSelection(taken);
        }

        CaptureCompleted?.Invoke(this, _annotationPreview.ToFrame());
    }

    /// <summary>
    /// Arms or disarms the colour sampler, from the toolbar or from Escape.
    /// </summary>
    /// <remarks>
    /// The mode lives here rather than in the toolbar because a pick is answered from
    /// the frozen screenshot, which is this window's. The toolbar is told so its button
    /// cannot show a sampler that is no longer armed.
    /// </remarks>
    private void SetColorSampling(bool armed)
    {
        _samplingColor = armed;
        AnnotationToolbar.SetColorSampling(armed);
        HintText.Text = armed ? SamplingHint : AnnotatingHint;
    }

    /// <summary>
    /// Takes the colour under the pointer and puts it on the toolbar.
    /// </summary>
    private void TakeSampledColor(CapturePoint point)
    {
        var sampled = SampleAt(point);
        AnnotationToolbar.ApplySampledColor(sampled);
        SetColorSampling(false);
        HintText.Text = $"Took {sampled.ToHex()} • {AnnotatingHint}";

        // The point as well as the colour: a sampler reading the wrong pixel is a
        // coordinate fault, and the colour alone cannot tell one from a channel swap.
        DiagnosticLog.Verbose($"sampled {sampled.ToHex()} at frame {point.X},{point.Y}");
    }

    /// <summary>
    /// The colour of the frozen screenshot under a frame-space point.
    /// </summary>
    /// <remarks>
    /// The screenshot rather than the preview, so a mark already drawn cannot be
    /// sampled by accident: what the sampler is for is the colour of the thing being
    /// annotated, and every annotation on top of it is macshot's own.
    /// </remarks>
    private AnnotationColor SampleAt(CapturePoint point) => PixelEffects.Sample(
        _desktopFrame.BgraPixels,
        _desktopFrame.Width,
        _desktopFrame.Height,
        (int)point.X,
        (int)point.Y);

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
