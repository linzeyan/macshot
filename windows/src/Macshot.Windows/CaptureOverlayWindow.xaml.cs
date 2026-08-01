using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Core.Recognition;
using Macshot.Windows.Rendering;
using Macshot.Windows.Services;
using Macshot.Windows.Toolbar;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using static Macshot.Windows.Services.Localization;

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
    /// The standing instruction before anything is chosen, with window snap on. Matches
    /// the XAML default.
    /// </summary>
    /// <remarks>
    /// What a click does is the whole difference between the two, which is why there are
    /// two of them rather than one sentence covering both: with snap on a click takes the
    /// window under the pointer and F is the way to the whole screen, with snap off the
    /// click is the way to the whole screen and there is no window to take.
    /// </remarks>
    private const string SelectionHint =
        "Drag to capture • Click a window to take it • F for the whole screen • Esc to cancel";

    /// <summary>The same instruction with window snap off.</summary>
    private const string SelectionHintNoSnap =
        "Drag to capture • Click for the whole screen • Esc to cancel";

    /// <summary>
    /// Line two of the idle pill: whether a click will take a window, and the key that
    /// changes the answer. Split where the state goes, which is the one part of it that
    /// is coloured.
    /// </summary>
    private const string SnapLinePrefix = "Window snap: ";

    private const string SnapLineSuffix = "  (Tab to toggle)";

    /// <summary>
    /// Shown above the region while it is being dragged out, which is the one moment the
    /// Space key is worth naming: it is what saves a drag whose first corner landed in the
    /// wrong place, and letting go to start again is how someone who has not been told
    /// deals with that.
    /// </summary>
    private const string SelectingHint = "Hold Space to move. Release to annotate and edit";

    private const string SamplingHint = "Click to take the colour under the pointer • Esc to stop";

    private const string MovingHint = "Move the region with the pointer • Click to place it • Esc to leave it";

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

    /// <summary>
    /// How much one notch of the wheel magnifies. Small enough that the zoom is steered
    /// rather than jumped through: the point of it is landing on one pixel.
    /// </summary>
    private const double ZoomStep = 1.1;

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

    /// <summary>
    /// The region's size, shown against it and typed into. Held here rather than declared
    /// in the markup so it can stay internal to the assembly like the toolbar's own parts.
    /// </summary>
    private readonly ResolutionBox _sizeBox = new();

    /// <summary>
    /// The ring of colours a right-click opens. Added to the overlay in code, above
    /// everything else, because it is drawn over whatever the pointer happens to be on.
    /// </summary>
    private readonly ColorWheelView _colorWheel = new();

    /// <summary>
    /// The word ON or OFF in the idle pill's second line, kept as a field so that the
    /// line can be refreshed without rebuilding its runs: the pill is re-hinted on every
    /// pointer move that changes which window is under the pointer.
    /// </summary>
    private readonly Run _snapState = new() { FontWeight = FontWeights.SemiBold };

    /// <summary>
    /// macOS's system green and orange, in their dark-appearance values. The pill is
    /// black whatever the system theme is, so the dark values are the right ones.
    /// </summary>
    private static readonly SolidColorBrush SnapOnBrush = new(Color.FromArgb(255, 48, 209, 88));

    private static readonly SolidColorBrush SnapOffBrush = new(Color.FromArgb(255, 255, 159, 10));

    /// <summary>
    /// This display's mapping between frame pixels and the layout units the overlay's
    /// chrome is arranged in. Built once: it depends only on the monitor.
    /// </summary>
    private readonly IFramePlacement _placement;

    /// <summary>The sampler's magnified circle, once one has been asked for.</summary>
    private SamplerLoupe? _loupe;

    private Point? _selectionStart;

    /// <summary>
    /// Where the pointer was on the last move of a marquee drag. Held so a rectangle
    /// being moved with Space travels by exactly what the pointer travelled.
    /// </summary>
    private Point? _marqueeAt;

    private CaptureRegion? _selection;

    /// <summary>
    /// Whether the delivered capture is mounted on a gradient background. Held here
    /// rather than applied when the button is pressed, because the frame is bigger than
    /// the region and there is nowhere in the overlay to show it.
    /// </summary>
    private bool _beautify;

    /// <summary>
    /// Whether the preview and the delivered capture have their colours turned. Unlike
    /// the gradient frame this one is the same size as the region, so it is shown rather
    /// than only promised.
    /// </summary>
    private bool _inverted;

    /// <summary>
    /// What the Adjust popover is asking for. Live state rather than something done to
    /// the pixels once, because the sliders are dragged: an adjustment burnt in on every
    /// tick would be an undo stack thirty entries deep for one decision.
    /// </summary>
    private ImageEffectsOptions _effects = ImageEffectsOptions.Default;

    /// <summary>
    /// The window's own pixels, when the region came from clicking a window rather than
    /// from a drag. Held because the preview is rebuilt whenever the region or an image
    /// switch changes, and re-cropping the screenshot would quietly swap the window's
    /// own capture for the screenshot of whatever was in front of it.
    /// </summary>
    private CapturedFrame? _capturedWindow;

    /// <summary>
    /// What the window the region came from calls itself, for the <c>{window}</c>
    /// filename token. Null for a region that was dragged out.
    /// </summary>
    /// <remarks>
    /// Kept even when the window's own capture failed and the pixels came from the
    /// screenshot instead. The region is still that window's, which is what the name is
    /// describing.
    /// </remarks>
    private string? _capturedWindowTitle;

    /// <summary>
    /// The region the last capture was taken from, offered on the display it was
    /// drawn on. Null on every other overlay, and once the offer has been taken or
    /// drawn over.
    /// </summary>
    private CaptureRegion? _remembered;

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

    /// <summary>
    /// Where the region was when the move button was pressed. Non-null for as long as the
    /// region is following the pointer.
    /// </summary>
    private CaptureRegion? _movingFrom;

    /// <summary>
    /// The pointer's hold on the region being moved, taken on the first movement rather
    /// than when the button was pressed.
    /// </summary>
    /// <remarks>
    /// The press that starts a move lands on a button outside the region, so an offset of
    /// zero would teleport the region under the toolbar the instant the pointer twitched.
    /// Measuring from wherever the pointer is when the move actually starts keeps the
    /// region where the user left it and moves it by exactly as much as the pointer moves.
    /// </remarks>
    private CapturePoint? _moveGrip;

    /// <summary>Where a move has reached, which is not taken until it is let go.</summary>
    private CaptureRegion _movePending;

    /// <summary>
    /// The shape the selection is being held to, or null when it is freeform.
    /// </summary>
    private double? _lockedAspect;

    /// <summary>How far into the capture the overlay is, and where.</summary>
    private Viewport _viewport = Viewport.Identity;

    /// <summary>The transform that carries out <see cref="_viewport"/>.</summary>
    private readonly ScaleTransform _zoom = new();
    private readonly TranslateTransform _pan = new();

    /// <summary>
    /// Where the pointer was when a pan drag last moved, in the overlay's own units.
    /// Non-null for as long as the middle button is held.
    /// </summary>
    private Point? _panningFrom;

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
        _placement = new MonitorFramePlacement(layout, monitor);
        InitializeComponent();
        // Every string in the XAML is already the English text macshot keys by,
        // so the page is translated in place rather than written twice.
        this.Localize();
    }

    /// <summary>
    /// Raised with the finished image and what was asked of it: the selection cropped out
    /// of the capture with every annotation already burned in. The owner receives pixels
    /// rather than a region because only this window knows what was drawn on it.
    /// </summary>
    public event EventHandler<CaptureCompletion>? CaptureCompleted;

    /// <summary>
    /// Raised once this overlay owns the capture, so the owner can close the
    /// overlays on the other displays instead of leaving always-on-top windows
    /// covering them while the user annotates.
    /// </summary>
    public event EventHandler? SelectionCommitted;

    public event EventHandler? Cancelled;

    /// <summary>
    /// Raised with the finished image when the user asks for it in the editor window
    /// rather than delivered. The capture ends either way; what differs is where the
    /// pixels go, which is why this carries them the way completing does.
    /// </summary>
    public event EventHandler<CapturedFrame>? EditorRequested;

    /// <summary>
    /// Raised when the user asks for a window to be scroll-captured. The overlay
    /// hands over the window rather than running it: scroll capture hides every
    /// overlay and drives the desktop, which is the owner's business, not one
    /// display's.
    /// </summary>
    public event EventHandler<ScrollCaptureRequest>? ScrollCaptureRequested;

    /// <summary>
    /// Raised when the user asks for the region to be recorded rather than captured.
    /// Handed over for the same reason a scroll capture is: a recording outlives every
    /// overlay, and the display it names may not be this one's.
    /// </summary>
    public event EventHandler<RecordingRequest>? RecordingRequested;

    /// <summary>
    /// Raised when Tab turns window snap on or off, so that the overlays on the other
    /// displays stop showing the state this one has just changed.
    /// </summary>
    public event EventHandler? WindowSnapToggled;

    public CaptureMonitor Monitor => _monitor;

    /// <summary>True once a region is chosen and the window is accepting annotations.</summary>
    private bool IsAnnotating => _selection is not null;

    public async Task ShowAsync()
    {
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(_monitorFrame.ToSoftwareBitmap());
        PreviewImage.Source = source;
        BuildGrips();
        WireZoom();
        WireToolbar();
        WireSizeBox();
        WireColorWheel();
        WireCanvas();

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

        // Everything is dimmed until something is chosen, which is what says the whole
        // screen is the thing being captured from.
        UpdateDim(null);

        // Placed rather than merely shown: the pill is laid out in a canvas, so until it is
        // told where to go it sits in the display's top-left corner.
        BuildSnapLine();
        ShowIdleInstruction();
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
        UpdateDim(ToLayout(_remembered.Value));
        ShowIdleInstruction();

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

        // The middle button drags the magnified capture around under the overlay. It is
        // the one button no tool wants, which is what makes it the one that can mean this
        // everywhere without taking anything away.
        if (e.GetCurrentPoint(SelectionCanvas).Properties.IsMiddleButtonPressed)
        {
            _panningFrom = e.GetCurrentPoint(OverlayRoot).Position;
            return;
        }

        // The right button opens the ring of colours where the pointer already is. The
        // colour button is wherever the toolbar happens to be, which is a trip across the
        // screen for every mark that wants a different colour from the last one.
        if (e.GetCurrentPoint(SelectionCanvas).Properties.IsRightButtonPressed && IsAnnotating)
        {
            _colorWheel.Show(ToLayoutPoint(e));
            return;
        }

        // A press while the ring is open answers it, whatever else that press would have
        // meant: the ring is in front of everything and the user is aiming at it.
        if (_colorWheel.IsShown)
        {
            TakeWheelColor();
            return;
        }

        // Ahead of everything else: while the sampler is armed the click is the pick,
        // and must not also start a mark or a selection under it.
        if (AnnotationToolbar.IsSamplingColor)
        {
            TakeSampledColor(ToFrame(e));
            return;
        }

        // A move ends on the click that places the region, and that click does nothing
        // else: it is the user putting the region down, not starting a mark inside it.
        if (_movingFrom is not null)
        {
            EndRegionMove(keep: true);
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

            // Sprite tools are placed with a click rather than dragged out, and the
            // editor is deliberately not told about the press, which is what keeps its
            // move and release handlers no-ops.
            if (AnnotationCanvasView.IsPlacedByClick(_editor.Tool))
            {
                AnnotationCanvas.PlaceSprite(ToFrame(e));
                return;
            }

            _editor.PointerPressed(ToFrame(e), ToModifiers(e));
            RenderAnnotations();
            return;
        }

        _selectionStart = e.GetCurrentPoint(SelectionCanvas).Position;
        _marqueeAt = _selectionStart;

        // The offer of the last selection ends the moment the user reaches for the
        // pointer, whether that turns out to be a drag or a click on a window. Leaving
        // it live would let Enter take a region that is no longer the one on screen.
        _remembered = null;

        // The highlight has done its job the moment a drag begins: what the pointer
        // is over stops mattering once the user is drawing their own edges.
        SnapHighlight.Visibility = Visibility.Collapsed;
        Instruct(SelectingHint);
        DrawMarquee(_selectionStart.Value, _selectionStart.Value);
    }

    private void SelectionCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_panningFrom is { } panFrom && e.Pointer.IsInContact)
        {
            var now = e.GetCurrentPoint(OverlayRoot).Position;
            _panningFrom = now;
            ApplyViewport(_viewport.PannedBy(now.X - panFrom.X, now.Y - panFrom.Y, LayoutBounds));
            return;
        }

        if (_colorWheel.IsShown)
        {
            _colorWheel.Hover(ToLayoutPoint(e));
            return;
        }

        UpdateCursor(ToFrame(e));

        if (AnnotationToolbar.IsSamplingColor)
        {
            // Reading it out as it moves is what makes the tool usable at all: on a
            // gradient or a photograph, the pixel under the pointer is not the colour
            // the eye reports, and there is no way to tell before committing to it.
            var sampling = ToFrame(e);
            Report(SamplingHint, SampleAt(sampling).ToHex());
            Loupe().Track(sampling);
            return;
        }

        // Ahead of everything else that reads the pointer: while the region is following
        // it, nothing else is.
        if (_movingFrom is not null)
        {
            UpdateRegionMove(ToFrame(e));
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
            var now = e.GetCurrentPoint(SelectionCanvas).Position;

            // Space moves the rectangle instead of resizing it, the way it does on macOS
            // and in every drawing program: the first corner of a drag lands where the
            // pointer was, which is rarely where it should have been, and letting go to
            // start again loses the size that was right.
            if (IsDown(VirtualKey.Space) && _marqueeAt is { } previous)
            {
                start = new Point(start.X + (now.X - previous.X), start.Y + (now.Y - previous.Y));
                _selectionStart = start;
            }

            _marqueeAt = now;
            DrawMarquee(start, now);
            return;
        }

        TrackHoveredWindow(ToFrame(e));
    }

    /// <summary>
    /// Says what a press here would do, by way of the cursor.
    /// </summary>
    /// <remarks>
    /// The overlay is one canvas covering the display and a press on it means five
    /// different things depending on where it lands. Nothing is a control, so there is no
    /// hover state to read and the pointer is the only thing that can say which.
    /// </remarks>
    private void UpdateCursor(CapturePoint point)
    {
        // Mid-drag the cursor is left alone: what was grabbed is what is still happening,
        // and a shape changing under the user's hand reads as the grab having slipped.
        if (_resizing != SelectionHandle.None || _selectionStart is not null)
        {
            return;
        }

        if (AnnotationToolbar.IsSamplingColor)
        {
            SelectionCanvas.UseCursor(InputSystemCursorShape.Cross);
            return;
        }

        if (_movingFrom is not null)
        {
            SelectionCanvas.UseCursor(InputSystemCursorShape.SizeAll);
            return;
        }

        if (!IsAnnotating)
        {
            SelectionCanvas.UseCursor(InputSystemCursorShape.Cross);
            return;
        }

        // Same order the press handler tries things in, or the cursor would promise
        // something other than what clicking does.
        if (_regionIsAdjustable && _selection is { } region)
        {
            var grip = SelectionHandles.HitTest(region, point, _monitor.Scale);
            if (grip != SelectionHandle.None)
            {
                SelectionCanvas.UseCursor(CursorHints.For(grip));
                return;
            }
        }

        SelectionCanvas.UseCursor(AnnotationCursor(point));
    }

    /// <summary>
    /// The cursor for the active tool: what it will do to the mark under the pointer with
    /// the select tool, and the crosshair a mark is drawn with otherwise.
    /// </summary>
    private InputSystemCursorShape AnnotationCursor(CapturePoint point)
    {
        if (_editor.Tool != AnnotationTool.Select)
        {
            return InputSystemCursorShape.Cross;
        }

        if (_editor.SelectionShown is { } shown
            && AnnotationHandles.At(shown, point, _editor.Scale) is { } handle)
        {
            return CursorHints.For(handle.Kind);
        }

        return _editor.Document.HitTest(point) is null
            ? InputSystemCursorShape.Arrow
            : InputSystemCursorShape.SizeAll;
    }

    /// <summary>
    /// Offers the window under the pointer, so a click can take it whole. The
    /// candidates were enumerated when the screenshot was, which is what lets this
    /// run on every pointer move without asking Windows anything.
    /// </summary>
    private void TrackHoveredWindow(CapturePoint point)
    {
        _hoveredWindow = SnapEnabled ? WindowSnapper.Snap(_snapCandidates, point, FrameBounds) : null;
        if (_hoveredWindow is not { } window)
        {
            SnapHighlight.Visibility = Visibility.Collapsed;

            // Back to whichever hint this overlay started with: an offered selection
            // is still on offer after the pointer has passed over a window.
            ShowIdleInstruction();
            return;
        }

        // Half a unit in on every side, the way macshot insets this one border —
        // OverlayView+WindowSnapping.swift:146. Without it the stroke straddles the
        // window's edge and the highlight reads as a window a pixel larger than it is.
        // macshot fills the uninset rect and strokes the inset one; here it is a single
        // Rectangle, so the fill comes in with the stroke — by half a unit, which is
        // under the fill's own alpha and cannot be seen.
        PlaceChrome(SnapHighlight, window.Bounds, inset: 0.5);
        Instruct(WindowHint);
    }

    /// <summary>
    /// Puts a piece of overlay chrome over a frame-space region. Chrome is laid out
    /// in this display's layout units while the region is in desktop pixels, so it
    /// has to come back through the same per-display scale input went out through.
    /// </summary>
    /// <param name="inset">
    /// How far inside the region the chrome sits, in layout units. Only the window
    /// highlight uses it: the selection's own outline is drawn on the edge, because the
    /// edge is what the user placed.
    /// </param>
    private void PlaceChrome(Rectangle target, CaptureRegion region, double inset = 0)
    {
        var origin = _layout.FrameToPointer(_monitor, new CapturePoint(region.X, region.Y));
        Canvas.SetLeft(target, origin.X + inset);
        Canvas.SetTop(target, origin.Y + inset);

        // Never below zero: a window narrower than the inset would otherwise ask WinUI
        // for a negative width, which throws rather than drawing nothing.
        target.Width = Math.Max(0, (region.Width / _monitor.Scale) - (inset * 2));
        target.Height = Math.Max(0, (region.Height / _monitor.Scale) - (inset * 2));
        target.Visibility = Visibility.Visible;
    }

    /// <summary>The whole capture, in frame space: what a window rect is clipped to.</summary>
    private CaptureRegion FrameBounds =>
        new(0, 0, _layout.VirtualBounds.Width, _layout.VirtualBounds.Height);

    private void SelectionCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        SelectionCanvas.ReleasePointerCaptures();

        if (_panningFrom is not null)
        {
            _panningFrom = null;
            return;
        }

        // Letting go over a colour takes it. Letting go having pointed at nothing leaves
        // the ring open to be clicked at instead — a right-click that opens it and does
        // not move is someone wanting to look, not someone who has missed.
        if (_colorWheel.IsShown)
        {
            if (_colorWheel.HoveredColor is not null)
            {
                TakeWheelColor();
            }
            else
            {
                _colorWheel.IsSticky = true;
            }

            return;
        }

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

            // After the release, because a ruler's reading is only knowable once the
            // drag that measures it has stopped.
            AnnotationCanvas.LabelRulers();
            return;
        }

        if (_selectionStart is not { } start)
        {
            return;
        }

        var end = e.GetCurrentPoint(SelectionCanvas).Position;
        DrawMarquee(start, end);
        _selectionStart = null;
        _marqueeAt = null;
        ShowIdleInstruction();

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
                    ScrollCaptureRequested?.Invoke(this, new ScrollCaptureRequest(window));
                    return;
                }

                // Awaited inside rather than by the caller: a pointer event handler
                // cannot be, and the capture it waits on is the only asynchronous
                // step between the click and the annotation phase.
                _ = EnterSnappedWindowPhaseAsync(window);
                return;
            }

            // No window to take — because snap is off, or because the pointer is over
            // the desktop. Either way the click meant the whole display: a click that
            // did nothing would read as the overlay having missed it.
            EnterAnnotationPhase(_layout.FrameRegionOf(_monitor));
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

            EnterAnnotationPhase(window.Bounds, captured, window.Title);

            if (captured is null)
            {
                Hint(L("Captured from the screen, so anything over the window is included"));
            }
        }
        catch (Exception exception)
        {
            // Nothing may escape: this runs on a task nobody holds, where an
            // unobserved exception ends the process rather than the capture. The
            // hint is where this overlay reports every other failure too.
            Hint(exception.Message);
        }
    }

    private void EnterAnnotationPhase(
        CaptureRegion region,
        CapturedFrame? capturedWindow = null,
        string? windowTitle = null)
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
        _capturedWindow = capturedWindow;
        _capturedWindowTitle = windowTitle;
        _regionIsAdjustable = capturedWindow is null;
        SnapHighlight.Visibility = Visibility.Collapsed;

        // Brings the marquee onto the region actually taken, which is the whole
        // point when the region came from a snapped window rather than from a drag
        // that already left it in the right place.
        PlaceChrome(SelectionRectangle, region);
        PlaceGrips(region);
        UpdateDim(ToLayout(region));

        // The preview covers the selection with the pixels that will be delivered,
        // which also hides the selection tint inside it: from here on, what is inside
        // the marquee is the finished image rather than a tinted approximation of it.
        AnnotationCanvas.Present(PixelsFor(region), region, _placement);

        // macshot's auto modes. A region chosen for recording, for its text, for a scroll
        // or for a quick capture is not a region to draw on, so the toolbar never appears
        // — putting one up for the one frame before the intent takes over would be a bar
        // that flickers past. A scroll with nothing behind it is the one intent that can
        // fail before it starts, and that one falls back to the toolbar rather than to
        // nothing.
        var automatic = Intent is not CaptureIntent.Capture
            && (Intent is not CaptureIntent.Scroll || WindowBehind(region) is not null);

        if (!automatic)
        {
            AnnotationToolbar.ShowToolbar(true);
            _sizeBox.Visibility = Visibility.Visible;
            RepositionChrome(region);
            Hint(string.Empty);
        }

        // The other displays' overlays are always on top, so they have to go before
        // the user can see anything but this one.
        SelectionCommitted?.Invoke(this, EventArgs.Empty);

        StartIntent();
    }

    /// <summary>
    /// Does what the menu item that opened this overlay said it would, now that there is
    /// a region to do it to. Nothing at all for an ordinary capture, which is the toolbar's
    /// to finish.
    /// </summary>
    private void StartIntent()
    {
        // Cleared first: every one of these can leave the overlay standing — a scroll with
        // no window behind it, a recognition that finds nothing — and a mode still armed
        // then would fire again on the next confirm.
        var intent = Intent;
        Intent = CaptureIntent.Capture;

        switch (intent)
        {
        case CaptureIntent.Record:
            RequestRecording();
            break;
        case CaptureIntent.Recognize:
            _ = ReadTextAsync();
            break;
        case CaptureIntent.Scroll:
            RequestScrollCapture();
            break;
        case CaptureIntent.Quick:
            _ = CompleteAsync();
            break;
        default:
            break;
        }
    }

    /// <summary>
    /// Takes the remembered region without waiting for Enter, which is what "Capture Last
    /// Area" means. False when this display has nothing remembered, so the caller can ask
    /// the next one.
    /// </summary>
    /// <remarks>
    /// Called by the owner once every overlay is up rather than from
    /// <see cref="OfferRememberedSelection"/>, because taking a region closes the other
    /// displays' overlays and one closed before it was shown is stranded on screen.
    /// </remarks>
    public bool AcceptRememberedSelection()
    {
        if (_remembered is not { } remembered)
        {
            return false;
        }

        EnterAnnotationPhase(remembered);
        return true;
    }

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
        // The same brush the toolbar paints its selected button with, rather than a copy
        // of its colour: ToolbarPalette repaints in place, so a chosen accent reaches the
        // region's edge and its grips without the overlay being rebuilt.
        SelectionRectangle.Stroke = ToolbarPalette.AccentBrush;

        foreach (var handle in SelectionHandles.All)
        {
            var grip = new Rectangle
            {
                Visibility = Visibility.Collapsed,

                // Accent-filled circles with no outline, which is what macOS draws.
                // PlaceChrome divides the grip's frame size by the display's scale, which
                // brings it back to Size in layout units — so half of Size is a circle on
                // every display.
                Fill = ToolbarPalette.AccentBrush,
                RadiusX = SelectionHandles.Size / 2,
                RadiusY = SelectionHandles.Size / 2,
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
            var offered = SelectionHandles.For(region, _monitor.Scale);
            foreach (var (handle, grip) in _grips)
            {
                if (offered.Contains(handle))
                {
                    PlaceChrome(grip, SelectionHandles.RectangleOf(region, handle, _monitor.Scale));
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

        _resizing = SelectionHandles.HitTest(region, point, _monitor.Scale);
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
    private CaptureRegion ResizedTo(PointerRoutedEventArgs e)
    {
        var dragged = SelectionHandles
            .Resize(_resizeFrom, _resizing, ToFrame(e), e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift))
            .Intersect(MonitorBounds);

        // A held shape applies to the grips too. One that only applied to typed numbers
        // would come apart the first time anyone dragged an edge, which is how the region
        // is adjusted the rest of the time.
        return _lockedAspect is { } aspect
            ? SelectionSizing.ConstrainToAspect(dragged, aspect, _resizing, MonitorBounds)
            : dragged;
    }

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
        UpdateDim(ToLayout(region));
        RepositionChrome(region);
    }

    /// <summary>
    /// Puts the toolbar and the size box around a frame-space region, on this display.
    /// </summary>
    /// <remarks>
    /// Both hang off the region rather than off the screen, so they move with every
    /// adjustment the way they do on macOS. Layout units go in, because they are arranged
    /// by WinUI while the region is in desktop pixels.
    /// </remarks>
    private void RepositionChrome(CaptureRegion region)
    {
        // Through the viewport: the chrome is outside the transform, so where it has to go
        // is where the region appears on screen, not where it is on the capture.
        var selection = _viewport.ToView(ToLayout(region));
        var screen = LayoutBounds;

        _sizeBox.Show(region.Width, region.Height);

        // The box first, so the toolbar knows what to keep clear of, and the box again
        // once the strips have settled. Each is placed around the other, and a single pass
        // would leave whichever went first sitting under the other.
        PlaceSizeBox(selection, screen);
        AnnotationToolbar.Reposition(selection, screen, _sizeBoxBounds);
        PlaceSizeBox(selection, screen);
    }

    /// <summary>Where the size box last landed, in layout units.</summary>
    private CaptureRegion _sizeBoxBounds;

    private void PlaceSizeBox(CaptureRegion selection, CaptureRegion screen)
    {
        // Left exactly where it is mid-edit: moving the box under the caret is how a
        // typed number ends up going somewhere else.
        if (_sizeBox.Visibility != Visibility.Visible || _sizeBox.IsEditing)
        {
            return;
        }

        _sizeBoxBounds = ResolutionBoxPlacement.For(
            selection,
            screen,
            _sizeBox.PreferredSize,
            AnnotationToolbar.Occupies,
            _sizeBox.DimensionsCenter);

        Canvas.SetLeft(_sizeBox, _sizeBoxBounds.X);
        Canvas.SetTop(_sizeBox, _sizeBoxBounds.Y);
    }

    /// <summary>This display, in the layout units the overlay's chrome is arranged in.</summary>
    private CaptureRegion LayoutBounds =>
        new(0, 0, _monitor.Bounds.Width / _monitor.Scale, _monitor.Bounds.Height / _monitor.Scale);

    /// <summary>A frame-space region as chrome over this display sees it.</summary>
    private CaptureRegion ToLayout(CaptureRegion region)
    {
        var origin = _layout.FrameToPointer(_monitor, new CapturePoint(region.X, region.Y));
        return new CaptureRegion(
            origin.X,
            origin.Y,
            region.Width / _monitor.Scale,
            region.Height / _monitor.Scale);
    }

    /// <summary>
    /// Hands the region to the pointer, which is the only way to move it with the mouse:
    /// dragging inside the selection draws a mark, and taking that gesture over would cost
    /// the tools the one thing they are for.
    /// </summary>
    /// <remarks>
    /// It runs until the next click rather than until the button is let go, the way it
    /// does on macOS. The press that starts it is a button click, and by the time a click
    /// has happened the mouse is already up — so "let go to place it" would place the
    /// region before it had moved at all.
    /// </remarks>
    private void BeginRegionMove()
    {
        if (_selection is not { } region || _movingFrom is not null)
        {
            return;
        }

        // Moving a window capture makes it an ordinary region: its pixels stop being the
        // window's own the moment it is over something else, so from here it is cropped
        // out of the screenshot like any other and its grips come back with it.
        _regionIsAdjustable = true;

        _movingFrom = region;
        _movePending = region;
        _moveGrip = null;
        Instruct(MovingHint);
        SelectionCanvas.UseCursor(InputSystemCursorShape.SizeAll);
        PlaceGrips(region);
    }

    /// <summary>
    /// Ends a move, either taking where the region has reached or putting it back.
    /// </summary>
    private void EndRegionMove(bool keep)
    {
        if (_movingFrom is not { } original)
        {
            return;
        }

        var moved = _movePending;
        _movingFrom = null;
        _moveGrip = null;
        Hint(string.Empty);

        if (keep)
        {
            ApplyRegion(moved);
            return;
        }

        ShowPendingRegion(original);
    }

    /// <summary>
    /// Follows the pointer with the region being moved, keeping it on this display.
    /// </summary>
    private void UpdateRegionMove(CapturePoint pointer)
    {
        if (_movingFrom is not { } original)
        {
            return;
        }

        _moveGrip ??= new CapturePoint(pointer.X - original.X, pointer.Y - original.Y);

        _movePending = SelectionHandles.ClampTo(
            new CaptureRegion(
                pointer.X - _moveGrip.Value.X,
                pointer.Y - _moveGrip.Value.Y,
                original.Width,
                original.Height),
            MonitorBounds);

        ShowPendingRegion(_movePending);
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
        UpdateDim(ToLayout(taken));
        RepositionChrome(taken);

        if (taken == current)
        {
            return;
        }

        _selection = taken;
        AnnotationCanvas.Present(PixelsFor(taken), taken, _placement);

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

    private void OverlayRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // While the entry box has focus the keyboard is its: Delete edits the text
        // instead of deleting an annotation, and Ctrl+Z takes back typing. Enter and
        // Escape are handled on the box itself, which is why they never arrive here.
        // The size box borrows it the same way while a number is being typed into it.
        if (AnnotationCanvas.IsTyping || _sizeBox.IsEditing)
        {
            return;
        }

        var control = IsDown(VirtualKey.Control);
        var shift = IsDown(VirtualKey.Shift);

        switch (e.Key)
        {
            case VirtualKey.Escape:
                e.Handled = true;

                // The ring is the first thing Escape closes: it is in front of everything
                // and it is taking the pointer, so it is what the user is looking at.
                if (_colorWheel.IsShown)
                {
                    _colorWheel.Dismiss();
                    return;
                }

                // An armed sampler is the first thing Escape gives up. It takes over
                // the click, so leaving it armed while Escape did something else would
                // leave the pointer doing nothing the user asked for.
                if (AnnotationToolbar.IsSamplingColor)
                {
                    SetColorSampling(false);
                    return;
                }

                // A move in flight is the next thing given up, and giving it up leaves the
                // region where it was rather than where the pointer has dragged it to.
                if (_movingFrom is not null)
                {
                    EndRegionMove(keep: false);
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

            case VirtualKey.Tab when !IsAnnotating && _selectionStart is null:
                // Handled, or Tab would move focus off the overlay's root and the next
                // key would go somewhere else. Only before a region is chosen: once the
                // toolbar is up, Tab is how it is walked with the keyboard.
                e.Handled = true;
                ToggleWindowSnap();
                return;

            case VirtualKey.F when !IsAnnotating && _selectionStart is null && SnapEnabled:
                // The way to the whole display when a click means the window under the
                // pointer. With snap off the click already means this, so F would be a
                // second way to do the same thing.
                e.Handled = true;
                EnterAnnotationPhase(_layout.FrameRegionOf(_monitor));
                return;

            case VirtualKey.Number0 when control:
                // What every other program means by it: back to actual size. Zooming out
                // a notch at a time to find 100% again is the part of a zoom nobody wants.
                e.Handled = true;
                ApplyViewport(Viewport.Identity);
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
    /// overlay can: which pixels a colour sample comes from, and what each action means
    /// here.
    /// </summary>
    private void WireToolbar()
    {
        AnnotationToolbar.Bind(_editor, _settings);
        AnnotationToolbar.Changed += (_, _) =>
        {
            RenderAnnotations();

            // Changing tool changes what the options row holds and so how wide it is, and
            // the size box is placed around the strips: both have to settle together, or
            // the row grows under the box that was placed to avoid it.
            if (_selection is { } region)
            {
                RepositionChrome(region);
            }
        };

        AnnotationToolbar.ColorSamplingToggled += (_, armed) => SetColorSampling(armed);
        AnnotationToolbar.EffectsChanged += (_, options) =>
        {
            _effects = options;
            if (_selection is { } region)
            {
                AnnotationCanvas.Present(PixelsFor(region), region, _placement);
            }
        };
        AnnotationToolbar.CommandInvoked += (_, command) => RunToolbarCommand(command);
        AnnotationToolbar.FrameStyleChosen += (_, index) => FrameStyleChosen(index);
    }

    /// <summary>
    /// Hangs the zoom and the pan off the capture, so both are one assignment away.
    /// </summary>
    private void WireZoom()
    {
        var transform = new TransformGroup();
        transform.Children.Add(_zoom);
        transform.Children.Add(_pan);
        ZoomHost.RenderTransform = transform;
    }

    /// <summary>
    /// Zooms about the pointer. The overlay is 1:1 with the display, which is the wrong
    /// magnification for choosing a region a few pixels across — a button's border, a line
    /// of small text — and it is exactly those the user is peering at when they reach for
    /// the wheel.
    /// </summary>
    private void SelectionCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        // Taken against the window rather than the capture: the anchor has to be where the
        // pointer is on screen, which is what stays still while everything under it grows.
        var anchor = e.GetCurrentPoint(OverlayRoot).Position;
        var wheel = e.GetCurrentPoint(SelectionCanvas).Properties.MouseWheelDelta;
        if (wheel == 0)
        {
            return;
        }

        e.Handled = true;
        ApplyViewport(_viewport.ZoomedAt(
            wheel > 0 ? ZoomStep : 1 / ZoomStep,
            new CapturePoint(anchor.X, anchor.Y),
            LayoutBounds));
    }

    /// <summary>
    /// Carries out a new viewport: the transform, the chrome around the selection, and the
    /// line that says how far in it is.
    /// </summary>
    private void ApplyViewport(Viewport viewport)
    {
        if (viewport == _viewport)
        {
            return;
        }

        _viewport = viewport;
        _zoom.ScaleX = viewport.Scale;
        _zoom.ScaleY = viewport.Scale;
        _pan.X = viewport.OffsetX;
        _pan.Y = viewport.OffsetY;

        if (_selection is { } region)
        {
            RepositionChrome(region);
        }

        // Back to actual size puts the standing instruction back, because the zoom reading
        // it replaced was the only thing the pill was saying.
        if (!viewport.IsIdentity)
        {
            Hint($"{viewport.Scale * 100:0}% • Scroll to zoom • Middle-drag to pan");
        }
        else if (IsAnnotating)
        {
            Hint(string.Empty);
        }
        else
        {
            ShowIdleInstruction();
        }
    }

    /// <summary>
    /// Puts the colour ring above everything else on the overlay. It is drawn wherever the
    /// pointer is, which is over the marks, the grips and the toolbar by turns.
    /// </summary>
    private void WireColorWheel() => OverlayRoot.Children.Add(_colorWheel);

    /// <summary>
    /// Takes the colour the ring is pointing at, if any, and closes it.
    /// </summary>
    private void TakeWheelColor()
    {
        if (_colorWheel.HoveredColor is { } picked)
        {
            AnnotationToolbar.ApplyPickedColor(picked);
        }

        _colorWheel.Dismiss();
    }

    /// <summary>
    /// Binds the size box: what it reads out, and what typing into it does.
    /// </summary>
    private void WireSizeBox()
    {
        SizeBoxLayer.Children.Add(_sizeBox);
        _sizeBox.Visibility = Visibility.Collapsed;
        _sizeBox.SizeCommitted += (_, request) => ApplyTypedSize(request);
        _sizeBox.PresetPicked += (_, preset) => ApplyPreset(preset);

        // The overlay's keyboard is only on loan to the fields. Without this, Escape and
        // every tool shortcut would keep going to a text box the user has finished with.
        _sizeBox.EditingEnded += (_, _) => OverlayRoot.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Resizes the region to a typed size, around its own middle.
    /// </summary>
    private void ApplyTypedSize(SizeRequest request)
    {
        if (_selection is not { } current)
        {
            return;
        }

        // Same as moving it: a window capture typed to a different size is no longer the
        // window, so it becomes an ordinary region cropped out of the screenshot.
        _regionIsAdjustable = true;

        ApplyRegion(SelectionSizing.Resize(
            current,
            request.Width,
            request.Height,
            MonitorBounds,
            _lockedAspect,
            request.Edited));
    }

    /// <summary>
    /// Takes a shape or an exact size from the presets menu.
    /// </summary>
    /// <remarks>
    /// A shape is held from here on, so dragging a grip keeps it. An exact size is a
    /// one-off and clears whatever shape was being held: asking for 1920 × 1080 while a
    /// 1 : 1 lock quietly squared it off would be the menu contradicting itself.
    /// </remarks>
    private void ApplyPreset(ResolutionPreset preset)
    {
        if (_selection is not { } current)
        {
            return;
        }

        _regionIsAdjustable = true;

        if (preset.IsExact)
        {
            _lockedAspect = null;
            ApplyRegion(SelectionSizing.Resize(current, preset.Width, preset.Height, MonitorBounds));
            return;
        }

        _lockedAspect = preset.Aspect;
        if (preset.Aspect is { } aspect)
        {
            ApplyRegion(SelectionSizing.ApplyAspect(current, aspect, MonitorBounds));
        }
    }

    /// <summary>
    /// What the action buttons do over a live capture.
    /// </summary>
    /// <remarks>
    /// Copy, save and pin each end the capture with one destination rather than with
    /// whatever the preferences say. A button named Copy that also wrote a file because
    /// auto-save happened to be on would be the button lying about what it does; Enter is
    /// the press that means "the usual".
    /// </remarks>
    private void RunToolbarCommand(ToolbarCommand command)
    {
        switch (command)
        {
            case ToolbarCommand.Undo:
                _editor.Undo();
                RenderAnnotations();
                return;

            case ToolbarCommand.Redo:
                _editor.Redo();
                RenderAnnotations();
                return;

            case ToolbarCommand.Cancel:
                // The whole capture, not this display's overlay: the owner tears every
                // window down, the same as Escape.
                Cancelled?.Invoke(this, EventArgs.Empty);
                return;

            case ToolbarCommand.MoveSelection:
                BeginRegionMove();
                return;

            case ToolbarCommand.OpenEditor:
                _ = OpenInEditorAsync();
                return;

            case ToolbarCommand.ReadText:
                _ = ReadTextAsync();
                return;

            case ToolbarCommand.Redact:
                _ = RedactPiiAsync();
                return;

            case ToolbarCommand.Translate:
#if !OFFLINE
                _ = TranslateAsync();
#endif
                return;

            case ToolbarCommand.InvertColors:
                ToggleInvert();
                return;

            case ToolbarCommand.Beautify:
                ToggleBeautify();
                return;

            case ToolbarCommand.ScrollCapture:
                RequestScrollCapture();
                return;

            case ToolbarCommand.Record:
                RequestRecording();
                return;

            case ToolbarCommand.Copy:
                _ = CompleteAsync(CaptureOutcome.Copy);
                return;

            case ToolbarCommand.Save:
                _ = CompleteAsync(CaptureOutcome.Save);
                return;

            case ToolbarCommand.Pin:
                _ = CompleteAsync(CaptureOutcome.Pin);
                return;

            case ToolbarCommand.SaveAs:
                _ = SaveAsAsync();
                return;

            case ToolbarCommand.Share:
                _ = ShareAsync();
                return;

            default:
                // Choosing a tool and choosing a colour are the toolbar's own business
                // and never reach the host.
                return;
        }
    }

    /// <summary>
    /// The pixels the preview shows for a region: the window's own capture or the
    /// screenshot under it, adjusted and turned if those switches are on.
    /// </summary>
    /// <remarks>
    /// One place, because the preview is rebuilt from four different events — the region
    /// being chosen, a grip being dragged, the Adjust sliders and the invert switch — and each of
    /// them getting its own answer about where the pixels come from is how a snapped
    /// window quietly turns back into a screenshot of the desktop.
    /// </remarks>
    private CapturedFrame PixelsFor(CaptureRegion region)
    {
        var source = _capturedWindow ?? NativeScreenCaptureService.Crop(_desktopFrame, region);

        if (!_effects.IsIdentity)
        {
            source = new CapturedFrame(
                source.VirtualX,
                source.VirtualY,
                source.Width,
                source.Height,
                ImageEffects.Apply(source.Width, source.Height, source.BgraPixels, _effects));
        }

        if (!_inverted)
        {
            return source;
        }

        return new CapturedFrame(
            source.VirtualX,
            source.VirtualY,
            source.Width,
            source.Height,
            FrameTransforms.Invert(source.Width, source.Height, source.BgraPixels));
    }

    /// <summary>
    /// Turns the capture's colours over, and back.
    /// </summary>
    /// <remarks>
    /// Shown rather than promised: the turned image is exactly the size of the one it
    /// replaces, so the preview can simply be rebuilt from it — which also means the
    /// marks drawn on top keep their own colours, the way they do on macshot.
    /// </remarks>
    private void ToggleInvert()
    {
        if (_selection is not { } region)
        {
            return;
        }

        _inverted = !_inverted;
        AnnotationToolbar.Inverted = _inverted;
        AnnotationCanvas.Present(PixelsFor(region), region, _placement);
    }

    /// <summary>
    /// Turns the gradient frame on or off for this capture.
    /// </summary>
    /// <remarks>
    /// A switch rather than something done to the pixels there and then, which is what
    /// macshot's own button is. The frame is larger than the region it surrounds, so
    /// there is nowhere inside the selection to show it — the button lighting up and the
    /// hint line are what say it is armed, and pressing it again takes it back off.
    /// </remarks>
    private void ToggleBeautify()
    {
        if (!IsAnnotating)
        {
            return;
        }

        _beautify = !_beautify;
        AnnotationToolbar.Beautified = _beautify;

        Hint(_beautify
            ? $"Framed in {BeautifyRenderer.Styles[_settings.Current.ToBeautifyOptions().StyleIndex].Name}"
            : "Frame removed");
    }

    /// <summary>
    /// A background chosen from behind the Frame button, which arms the frame as well as
    /// choosing it.
    /// </summary>
    /// <remarks>
    /// Arming is the whole reason to pick one. Leaving the frame off would mean choosing a
    /// background, watching nothing change, and then having to find the button that turns
    /// on the thing already chosen.
    /// </remarks>
    private void FrameStyleChosen(int styleIndex)
    {
        if (!IsAnnotating)
        {
            return;
        }

        _beautify = true;
        AnnotationToolbar.Beautified = true;
        Hint($"Framed in {BeautifyRenderer.Styles[styleIndex].Name}");
    }

    /// <summary>
    /// The capture as it is delivered: what was drawn, framed if the user asked for it.
    /// </summary>
    /// <remarks>
    /// The frame goes on last, after the marks are burned in, so an arrow drawn to the
    /// edge of the region stays on the screenshot rather than crossing the background it
    /// is mounted on.
    /// </remarks>
    private CapturedFrame? Finished()
    {
        if (AnnotationCanvas.ToFrame() is not { } finished)
        {
            return null;
        }

        if (!_beautify)
        {
            return finished;
        }

        var (width, height, pixels) = BeautifyRenderer.Render(
            finished.Width,
            finished.Height,
            finished.BgraPixels,
            _settings.Current.ToBeautifyOptions());

        return new CapturedFrame(finished.VirtualX, finished.VirtualY, width, height, pixels);
    }

    /// <summary>
    /// The capture as it is delivered, together with what it can be reopened from and
    /// what the window it came from is called. Null when there is nothing to deliver.
    /// </summary>
    /// <remarks>
    /// The editable pair is withheld from a framed capture. The background a frame puts
    /// around the image is not one of the marks, so the pixels and the marks would
    /// reopen as the picture without it — a different picture from the one that was
    /// approved, and silently so. Archiving nothing for it is the honest answer until
    /// the frame is something the editor can be handed back.
    /// </remarks>
    private CaptureCompletion? Completed(CaptureOutcome outcome)
    {
        return Finished() is { } finished
            ? new CaptureCompletion(
                finished,
                outcome,
                _beautify ? null : AnnotationCanvas.ToEditable(),
                _capturedWindowTitle)
            : null;
    }

    /// <summary>
    /// Asks for the window behind the region to be scrolled and the region stitched.
    /// </summary>
    /// <remarks>
    /// The window is resolved here rather than by the owner because this is where the
    /// windows are known: they were listed next to the screenshot, before any overlay
    /// existed, so the list holds what the user is actually looking at and not macshot's
    /// own always-on-top windows.
    /// </remarks>
    private void RequestScrollCapture()
    {
        if (_selection is not { } region)
        {
            return;
        }

        if (WindowBehind(region) is not { } window)
        {
            Hint(L("There is no window behind that region to scroll"));
            return;
        }

        ScrollCaptureRequested?.Invoke(
            this,
            new ScrollCaptureRequest(window, _layout.FrameToVirtual(region)));
    }

    /// <summary>
    /// The frontmost window the region's middle sits on, or null when it sits on the
    /// desktop.
    /// </summary>
    /// <remarks>
    /// The middle rather than any overlap, and the frontmost rather than the largest:
    /// windows overlap, and the one under the point the user centred the region on is
    /// the one they were looking at.
    /// </remarks>
    private CaptureWindow? WindowBehind(CaptureRegion region)
    {
        var centre = new CapturePoint(region.X + (region.Width / 2), region.Y + (region.Height / 2));

        foreach (var window in _snapCandidates)
        {
            if (!window.Bounds.IsEmpty && window.Bounds.Contains(centre.X, centre.Y))
            {
                return window;
            }
        }

        return null;
    }

    /// <summary>
    /// What the region this overlay is about to take is for.
    /// </summary>
    /// <remarks>
    /// Every menu item that needs a rectangle opens the same overlay "Capture Area" does
    /// and sets this. Without it those items would each open an overlay indistinguishable
    /// from the capture one and leave the user to find the right toolbar button, which is
    /// not what any of them say they do.
    /// </remarks>
    public CaptureIntent Intent { get; set; }

    /// <summary>Asks for the region to be recorded rather than captured.</summary>
    private void RequestRecording()
    {
        if (_selection is not { } region)
        {
            return;
        }

        RecordingRequested?.Invoke(this, new RecordingRequest(_monitor, _layout.FrameToVirtual(region)));
    }

    /// <summary>
    /// Hands the marked-up pixels to the editor window instead of to delivery.
    /// </summary>
    /// <remarks>
    /// The marks go across as pixels rather than as annotations. They were drawn against
    /// the whole virtual desktop and the editor's image starts at its own origin, so
    /// carrying them as objects would need every one shifted — and the user asked for the
    /// image they are looking at, not for a second chance at the arrow.
    /// </remarks>
    private async Task OpenInEditorAsync()
    {
        if (!IsAnnotating)
        {
            return;
        }

        await AnnotationCanvas.FlushAsync();
        if (Finished() is { } finished)
        {
            EditorRequested?.Invoke(this, finished);
        }
    }

    /// <summary>
    /// Binds the shared drawing surface. The sprite scale comes from this window's XAML
    /// root, and the hint line is where the surface reports what it is doing.
    /// </summary>
    private void WireCanvas()
    {
        AnnotationCanvas.Bind(
            _editor,
            () => OverlayRoot.XamlRoot?.RasterizationScale ?? _monitor.Scale,
            message => Hint(message));
        AnnotationCanvas.StampEmoji = () => AnnotationToolbar.StampEmoji;
        AnnotationCanvas.TypingEnded += (_, _) =>
        {
            OverlayRoot.Focus(FocusState.Programmatic);
            Hint(string.Empty);
        };
    }

    /// <remarks>
    /// Not routed through <see cref="RunRecognitionAsync"/> like the other two readers:
    /// this one has a second await in it, for the QR codes, and that callback is not
    /// asynchronous. macshot reads text and codes in one Vision pass; here they are two
    /// different engines and the window waits for both.
    /// </remarks>
    private async Task ReadTextAsync()
    {
        if (!IsAnnotating)
        {
            return;
        }

        var previousHint = HintText.Text;
        Hint(L("Reading text..."));
        try
        {
            var lines = await AnnotationCanvas.RecognizeAsync();

            // With the capture, so the results window shows what the words were read
            // out of — the overlay it came from is about to be dismissed.
            var frame = AnnotationCanvas.ToFrame();
            var codes = await TextRecognizer.ScanQrCodesAsync(frame);
            Hint(previousHint);

            var window = new TextRecognitionWindow(
                TextRecognizer.ToText(lines),
                _settings,
                frame,
                codes);

            // The overlay is always on top, so the results window would open behind
            // it. Reading the text ends the capture, the same way it does on macOS.
            Cancelled?.Invoke(this, EventArgs.Empty);
            window.Activate();
        }
        catch (Exception exception)
        {
            Hint(exception.Message);
        }
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
                Hint(L("No personal data found in the selection"));
                return;
            }

            // One AddRange rather than a loop, so a single Ctrl+Z takes the whole
            // run back off. This is what the document's snapshot history buys.
            _editor.Document.AddRange(annotations);
            RenderAnnotations();
            Hint($"Redacted {annotations.Count} • Ctrl+Z to undo • Enter to finish");
        });
    }

    /// <summary>
    /// Asks where to put the capture, writes it there, and ends the capture.
    /// </summary>
    /// <remarks>
    /// The dialog is run from here rather than from the owner because it needs a window
    /// to belong to, and by the time the owner has the pixels every overlay is gone. A
    /// dismissed dialog leaves the capture where it was: cancelling a save is not
    /// cancelling the capture.
    /// </remarks>
    private async Task SaveAsAsync()
    {
        if (!IsAnnotating)
        {
            return;
        }

        await AnnotationCanvas.FlushAsync();
        if (Completed(CaptureOutcome.SaveAs) is not { } completion)
        {
            return;
        }

        try
        {
            if (await SavePrompt.WriteAsync(this, completion.Frame, _settings.Current, completion.WindowTitle) is not null)
            {
                CaptureCompleted?.Invoke(this, completion);
            }
        }
        catch (Exception exception)
        {
            Hint(exception.Message);
        }
    }

    /// <summary>
    /// Opens the system share pane over the capture.
    /// </summary>
    /// <remarks>
    /// The overlay stays up until a target is picked, and only then ends the capture.
    /// The pane belongs to this window, so dismissing the overlay first would take the
    /// pane down with it — which is also why this is not one of the outcomes
    /// <see cref="CompleteAsync"/> delivers.
    /// </remarks>
    private async Task ShareAsync()
    {
        if (!IsAnnotating)
        {
            return;
        }

        await AnnotationCanvas.FlushAsync();
        if (Finished() is not { } finished)
        {
            return;
        }

        try
        {
            await ShareSheet.ShowAsync(
                this,
                finished,
                _settings.Current,
                () => Cancelled?.Invoke(this, EventArgs.Empty));
        }
        catch (Exception exception)
        {
            Hint(exception.Message);
        }
    }

#if !OFFLINE
    /// <summary>
    /// Lays a translation over the text in the selection, in place, the way macshot
    /// does — as opposed to reading it out into a window.
    /// </summary>
    private async Task TranslateAsync()
    {
        if (!IsAnnotating)
        {
            return;
        }

        Hint(L("Translating..."));
        try
        {
            Hint(await TranslationPlacement.RunAsync(AnnotationCanvas, _settings.Current, CancellationToken.None));
        }
        catch (Exception exception)
        {
            Hint(exception.Message);
        }
    }
#endif

    /// <summary>
    /// Runs OCR over the selection and hands the result to the caller. Failures land
    /// in the hint line rather than a dialog: the overlay is a borderless
    /// always-on-top window covering the screen, and it already has somewhere to say
    /// things.
    /// </summary>
    private async Task RunRecognitionAsync(Action<IReadOnlyList<RecognizedLine>> handle)
    {
        if (!IsAnnotating)
        {
            return;
        }

        var previousHint = HintText.Text;
        Hint(L("Reading text..."));
        try
        {
            var lines = await AnnotationCanvas.RecognizeAsync();
            Hint(previousHint);
            handle(lines);
        }
        catch (Exception exception)
        {
            Hint(exception.Message);
        }
    }

    /// <summary>
    /// Delivers the pixels the preview is already showing. There is no separate
    /// export render: the preview was produced by the Core rasterizer at capture
    /// resolution over this exact crop, so re-rendering could only introduce a
    /// difference between what was approved and what is handed over.
    /// </summary>
    private async Task CompleteAsync(CaptureOutcome outcome = CaptureOutcome.Deliver)
    {
        if (!IsAnnotating)
        {
            return;
        }

        // Text still in the entry box is part of the capture the moment the user
        // finishes it, and asking for the capture is finishing it. Every queued placement
        // has to land before the pixels are taken, or the delivered image is missing a
        // mark the user made.
        await AnnotationCanvas.FlushAsync();

        if (_selection is { } taken)
        {
            RememberSelection(taken);
        }

        if (Completed(outcome) is { } completion)
        {
            CaptureCompleted?.Invoke(this, completion);
        }
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
        AnnotationToolbar.SetColorSampling(armed);
        Instruct(armed ? SamplingHint : string.Empty);

        if (!armed)
        {
            _loupe?.Hide();
        }
    }

    /// <summary>
    /// The sampler's magnified circle, built the first time a colour is picked.
    /// </summary>
    /// <remarks>
    /// Lazily, because most captures never arm the sampler and it holds a bitmap of its
    /// own; kept afterwards, because it is rebuilt on every pointer move otherwise.
    /// </remarks>
    private SamplerLoupe Loupe() => _loupe ??= new SamplerLoupe(LoupeLayer, _placement, _desktopFrame);

    /// <summary>
    /// Takes the colour under the pointer and puts it on the toolbar.
    /// </summary>
    private void TakeSampledColor(CapturePoint point)
    {
        var sampled = SampleAt(point);
        AnnotationToolbar.ApplyPickedColor(sampled);
        SetColorSampling(false);
        Hint($"Took {sampled.ToHex()}");

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

    private void RenderAnnotations() => AnnotationCanvas.Render();

    private CapturePoint ToFrame(PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(SelectionCanvas).Position;
        return _layout.PointerToFrame(_monitor, position.X, position.Y);
    }

    /// <summary>
    /// The pointer where the overlay's chrome is arranged, rather than where its pixels
    /// are. The colour ring is drawn by WinUI, so it is placed in layout units.
    /// </summary>
    private CapturePoint ToLayoutPoint(PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(OverlayRoot).Position;
        return new CapturePoint(position.X, position.Y);
    }

    private static EditorModifiers ToModifiers(PointerRoutedEventArgs e) =>
        e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) ? EditorModifiers.Constrain : EditorModifiers.None;

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    /// <summary>
    /// Darkens everything that is not being captured.
    /// </summary>
    /// <remarks>
    /// The region itself is left alone, so what is inside the marquee is the capture as it
    /// will be delivered rather than a tinted approximation of it — the colour being
    /// captured is often the reason for the capture. Four rectangles around the region
    /// rather than one with a hole in it: the hole would be a path to rebuild on every
    /// pointer move, and this is four numbers to change.
    /// </remarks>
    private void UpdateDim(CaptureRegion? clear)
    {
        var screen = LayoutBounds;

        if (clear is not { } hole || hole.IsEmpty)
        {
            // Nothing chosen yet, so all of it is "not being captured".
            Cover(DimTop, screen);
            Cover(DimBottom, default);
            Cover(DimLeft, default);
            Cover(DimRight, default);
            return;
        }

        Cover(DimTop, new CaptureRegion(screen.X, screen.Y, screen.Width, hole.Y - screen.Y));
        Cover(DimBottom, new CaptureRegion(screen.X, hole.Bottom, screen.Width, screen.Bottom - hole.Bottom));
        Cover(DimLeft, new CaptureRegion(screen.X, hole.Y, hole.X - screen.X, hole.Height));
        Cover(DimRight, new CaptureRegion(hole.Right, hole.Y, screen.Right - hole.Right, hole.Height));
    }

    private static void Cover(Rectangle target, CaptureRegion where)
    {
        // A region with no area is a side of the selection that is against the edge of the
        // screen. Hidden rather than drawn at zero size, because a Rectangle with a stroke
        // would still be a line there.
        if (where.IsEmpty)
        {
            target.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(target, where.X);
        Canvas.SetTop(target, where.Y);
        target.Width = where.Width;
        target.Height = where.Height;
        target.Visibility = Visibility.Visible;
    }

    /// <summary>Draws the marquee, which stays in layout units because it is chrome.</summary>
    private void DrawMarquee(Point start, Point end)
    {
        var region = CaptureRegion.FromPoints(start.X, start.Y, end.X, end.Y);
        UpdateDim(region);
        Canvas.SetLeft(SelectionRectangle, region.X);
        Canvas.SetTop(SelectionRectangle, region.Y);
        SelectionRectangle.Width = region.Width;
        SelectionRectangle.Height = region.Height;
        SelectionRectangle.Visibility = Visibility.Visible;
        PlaceHint();
    }

    /// <summary>
    /// Says something on the overlay, or takes the pill away when there is nothing to say.
    /// </summary>
    /// <remarks>
    /// Empty means gone rather than blank, and the annotation phase says nothing at all —
    /// which is what macOS does with it. A line of instructions standing over a chosen
    /// region for as long as it is being worked on is a strip of the capture the user
    /// cannot see past, and it is at its least useful exactly then: by that point they
    /// have already dragged out a region and are holding a tool.
    /// </remarks>
    private void Hint(string text)
    {
        HintText.Text = text;

        // Line two belongs to the idle instruction alone. Collapsed here rather than at
        // each other call site, so that anything the overlay reports replaces the whole
        // pill instead of leaving a stale line about window snap under it.
        HintSnapLine.Visibility = Visibility.Collapsed;

        if (text.Length == 0)
        {
            HintPill.Visibility = Visibility.Collapsed;
            return;
        }

        HintPill.Visibility = Visibility.Visible;
        PlaceHint();
    }

    /// <summary>Whether a click will take the window under the pointer.</summary>
    private bool SnapEnabled => _settings.Current.WindowSnapEnabled;

    /// <summary>Whether the user has asked for the instructions to stay off the overlay.</summary>
    private bool HideInstructions => _settings.Current.HideCaptureInstructions;

    /// <summary>
    /// Says something the user could have worked out for themselves, which is what the
    /// hide-instructions setting is about: this is the call that setting silences.
    /// </summary>
    private void Instruct(string text) => Hint(HideInstructions ? string.Empty : text);

    /// <summary>
    /// An instruction and a reading in one line. The reading survives on its own when
    /// instructions are hidden — a sampled colour is not an instruction, and hiding it
    /// would leave the tool with nothing to say.
    /// </summary>
    private void Report(string instruction, string reading) =>
        Hint(HideInstructions ? reading : $"{instruction} • {reading}");

    /// <summary>
    /// The standing instruction, and under it the state of window snap.
    /// </summary>
    /// <remarks>
    /// Both lines depend on the same answer, which is why they are set together: with snap
    /// off, line one must stop telling the user to click a window and line two must stop
    /// saying ON, and the two saying different things is worse than neither being there.
    /// </remarks>
    private void ShowIdleInstruction()
    {
        var snapOn = SnapEnabled;
        Instruct(_remembered is not null
            ? RememberedHint
            : snapOn ? SelectionHint : SelectionHintNoSnap);

        if (HintPill.Visibility != Visibility.Visible)
        {
            return;
        }

        _snapState.Text = snapOn ? "ON" : "OFF";
        _snapState.Foreground = snapOn ? SnapOnBrush : SnapOffBrush;
        HintSnapLine.Visibility = Visibility.Visible;
        PlaceHint();
    }

    /// <summary>
    /// Builds the idle pill's second line, once. Three runs: the label, the state and the
    /// key, of which only the middle one ever changes.
    /// </summary>
    private void BuildSnapLine()
    {
        HintSnapLine.Inlines.Add(new Run { Text = SnapLinePrefix });
        HintSnapLine.Inlines.Add(_snapState);
        HintSnapLine.Inlines.Add(new Run { Text = SnapLineSuffix });
    }

    /// <summary>
    /// Turns window snap on or off, for this capture and the ones after it.
    /// </summary>
    /// <remarks>
    /// Written to the settings file rather than kept in the window, because the answer is
    /// about how the user wants to capture and not about this capture: someone who turns
    /// it off has decided they do not want windows offered, and being asked again on the
    /// next hotkey press would be the toggle not working.
    /// </remarks>
    private void ToggleWindowSnap()
    {
        try
        {
            _settings.Save(_settings.Current with { WindowSnapEnabled = !SnapEnabled });
        }
        catch (Exception exception)
        {
            // Nothing changed: the store takes the new settings only once they are on
            // disk, so a failed write leaves window snap exactly as it was. Said out loud
            // rather than swallowed — the user pressed a key and is owed an answer, and
            // the alternative is a pill that goes on claiming the state they just changed.
            DiagnosticLog.Write($"Could not change the window snap state: {exception.Message}");
            Hint(exception.Message);
            return;
        }

        RefreshWindowSnapState();

        // Every display's overlay shows the state, so every display's overlay is wrong
        // until it is told. macOS notifies through its delegate for the same reason.
        WindowSnapToggled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Redraws what window snap being on or off changes: the highlight and the pill.
    /// </summary>
    internal void RefreshWindowSnapState()
    {
        if (IsAnnotating)
        {
            return;
        }

        _hoveredWindow = null;
        SnapHighlight.Visibility = Visibility.Collapsed;
        ShowIdleInstruction();
    }

    /// <summary>
    /// Puts the pill where what it is about is: the middle of the screen while nothing is
    /// chosen, and just above the region once something is.
    /// </summary>
    /// <remarks>
    /// Both placements are macOS's, and so is each one's shape — the middle of an empty
    /// screen can carry a larger pill than a line sitting against a rectangle the user is
    /// dragging. Measured rather than worked out from constants, unlike the toolbar and the
    /// size box: what a sentence comes to is the whole variable here, and there is no
    /// arithmetic that answers it.
    /// </remarks>
    private void PlaceHint()
    {
        if (HintPill.Visibility != Visibility.Visible)
        {
            return;
        }

        var screen = LayoutBounds;
        var anchor = HintAnchor();

        HintPill.Padding = anchor is null ? new Thickness(14) : new Thickness(10, 5, 10, 5);
        HintPill.CornerRadius = new CornerRadius(anchor is null ? 8 : 6);
        HintPill.Measure(new Size(screen.Width, screen.Height));
        var size = HintPill.DesiredSize;

        if (anchor is not { } region)
        {
            Canvas.SetLeft(HintPill, (screen.Width - size.Width) / 2);
            Canvas.SetTop(HintPill, (screen.Height - size.Height) / 2);
            return;
        }

        // Above the region, or below it when there is no room above — and never off the
        // side, which a wide sentence about a region against the screen's edge would be.
        var top = region.Y - size.Height - HintGap;
        if (top < screen.Y + HintGap)
        {
            top = region.Bottom + HintGap;
        }

        Canvas.SetLeft(HintPill, Math.Clamp(
            region.X + ((region.Width - size.Width) / 2),
            screen.X + HintEdge,
            Math.Max(screen.X + HintEdge, screen.Right - size.Width - HintEdge)));
        Canvas.SetTop(HintPill, Math.Min(top, Math.Max(screen.Y, screen.Bottom - size.Height - HintGap)));
    }

    /// <summary>How far the pill keeps off the region it describes.</summary>
    private const double HintGap = 8;

    /// <summary>
    /// How far the pill keeps off the side of the screen. Less than the gap it keeps from
    /// the region: this one is only there to stop a wide sentence about a region against
    /// the screen's edge being cut off, so it gives up as little of the middle as it can.
    /// </summary>
    private const double HintEdge = 4;

    /// <summary>
    /// What the pill is about, in the units it is placed in, or null when that is the
    /// whole screen because nothing has been chosen or dragged out yet.
    /// </summary>
    /// <remarks>
    /// Through the viewport, like the rest of the chrome: the pill is outside the zoom
    /// transform, so where it goes is where the region appears on screen rather than where
    /// it is on the capture.
    /// </remarks>
    private CaptureRegion? HintAnchor()
    {
        if (_selection is { } chosen)
        {
            return _viewport.ToView(ToLayout(chosen));
        }

        return _selectionStart is { } start && _marqueeAt is { } now
            ? _viewport.ToView(CaptureRegion.FromPoints(start.X, start.Y, now.X, now.Y))
            : null;
    }
}
