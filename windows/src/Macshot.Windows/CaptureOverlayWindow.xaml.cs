using System.Runtime.InteropServices.WindowsRuntime;
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
    /// <para>
    /// What a click does is the whole difference between the two, which is why there are
    /// two of them rather than one sentence covering both: with snap on a click takes the
    /// window under the pointer and F is the way to the whole screen, with snap off the
    /// click is the way to the whole screen and there is no window to take.
    /// </para>
    /// <para>
    /// macshot's own two sentences, word for word and separator for separator
    /// (<c>OverlayView.swift:2203–2204</c>), because these strings are the keys its forty
    /// translations are filed under. The port used to say the same thing in its own
    /// words, which resolved against nothing: the pill came up in English on a machine
    /// where every other part of the overlay was translated.
    /// </para>
    /// <para>
    /// Properties rather than constants, and read at each use: the language can change
    /// while macshot is running, and a <c>const</c> would have been folded into the
    /// caller in English at compile time.
    /// </para>
    /// </remarks>
    private static string SelectionHint =>
        L("Click a window  ·  Drag for custom area  ·  F for full screen");

    /// <summary>The same instruction with window snap off.</summary>
    private static string SelectionHintNoSnap => L("Drag to select  ·  Click for full screen");

    /// <summary>
    /// Line two of the idle pill: whether a click will take a window, and the key that
    /// changes the answer. Split where the state goes, which is the one part of it that
    /// is coloured.
    /// </summary>
    private static string SnapLinePrefix => L("Window snap: ");

    private static string SnapLineSuffix => L("  (Tab to toggle)");

    /// <summary>
    /// Shown above the region while it is being dragged out, which is the one moment the
    /// Space key is worth naming: it is what saves a drag whose first corner landed in the
    /// wrong place, and letting go to start again is how someone who has not been told
    /// deals with that.
    /// </summary>
    private static string SelectingHint => L("Hold Space to move. Release to annotate and edit");

    private static string SamplingHint => L("Click to take the colour under the pointer • Esc to stop");

    private static string MovingHint =>
        L("Move the region with the pointer • Click to place it • Esc to leave it");

    private static string RememberedHint =>
        L("Enter to take the last selection again • Drag for a new one • Esc to cancel");

    /// <summary>
    /// Shown while a window is highlighted. Scroll capture is otherwise unfindable:
    /// there is no toolbar yet at hover time, and a gesture nobody is told about is
    /// a feature nobody has.
    /// </summary>
    private static string WindowHint =>
        L("Click to take this window • Shift+click to scroll-capture it • Esc to cancel");

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

    /// <summary>
    /// How far the pointer may wander between the two clicks of a double-click, in
    /// layout units. Windows has a metric for this, but it is in physical pixels and
    /// this is measured where the pointer is reported; four is the same few pixels on
    /// every display and well under the distance a deliberate second click travels.
    /// </summary>
    private const double DoubleClickSlop = 4;

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
    /// The one control the overlay offers before anything has been chosen: it picks the
    /// shape, or the size, the next drag will come out as. Held here rather than declared
    /// in the markup so it can stay internal to the assembly like the toolbar's own parts.
    /// </summary>
    private readonly PreSelectionPresetButton _preSelectionButton = new();

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
    /// The camera bubble shown while a recording is being set up, so what it covers can
    /// be seen before the recording rather than in the finished video.
    /// </summary>
    private WebcamWindow? _webcam;

    /// <summary>
    /// Whether the delivered capture is mounted on a gradient background. Held here
    /// rather than applied when the button is pressed, because the frame goes on after
    /// the marks — what the overlay shows meanwhile is drawn around the region by
    /// <see cref="ShowFrame"/>, which leaves the region itself alone.
    /// </summary>
    private bool _beautify;

    /// <summary>
    /// How far the strips have moved between the region and the frame around it. One is
    /// settled, which is where it stays for as long as the frame is not being switched.
    /// </summary>
    private double _frameAnchorProgress = 1;

    /// <summary>Drives that move. macshot's sixtieth of a second, twelve frames of it.</summary>
    private readonly DispatcherTimer _frameAnchor = new() { Interval = TimeSpan.FromSeconds(1 / 60.0) };

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
    /// Which axis the held auto-measure key is asking for — true for 1 and the vertical
    /// run, false for 2 and the horizontal — or null while neither is held.
    /// </summary>
    private bool? _autoSpanVertical;

    /// <summary>
    /// Where the pointer last was, in the capture's own coordinates.
    /// </summary>
    /// <remarks>
    /// Kept because the auto-measure offer is recomputed from a key press as well as from
    /// a pointer move, and a key press carries no position. Windows has no equivalent of
    /// <c>mouseLocationOutsideOfEventStream</c> that is safe to ask from a keyboard event:
    /// the screen position would have to be mapped back through this window's own
    /// scaling, which is what <see cref="ToFrame"/> already did on the last move.
    /// </remarks>
    private CapturePoint? _pointerAt;

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
    /// The window the chosen region <em>is</em>, when it was chosen by clicking one rather
    /// than by dragging a rectangle. Null for every region that came from a drag.
    /// </summary>
    /// <remarks>
    /// Only recording reads it, and only to record the window itself rather than the
    /// rectangle it currently occupies. Kept as the window rather than as a flag because a
    /// recording opens a capture item on it, which needs the identity and not the bounds.
    /// </remarks>
    private CaptureWindow? _snappedWindow;

    /// <summary>
    /// The open microphone behind the meter in the mic button, while a recording is being
    /// set up with it switched on. Null the rest of the time, which is what says the
    /// microphone is not open.
    /// </summary>
    private MicrophoneMeter? _micMeter;

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

    /// <summary>
    /// What the next drag is to produce: a shape to hold it to, an exact size to place
    /// instead of dragging, or freeform. Read on every pointer move of a marquee drag, so
    /// it is kept here rather than resolved out of the settings each time.
    /// </summary>
    private PreSelectionPreset _preSelection;

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

    /// <summary>Where the last press landed, and when, for spotting a double-click.</summary>
    private Point _lastPressPoint;

    private long _lastPressAt;

    /// <summary>
    /// The lines in this display's pixels, once they have been read. Null until then, and
    /// for the whole capture when the setting is off — which every use of it must survive.
    /// </summary>
    /// <remarks>
    /// Written by the worker that builds it and read on the UI thread. Volatile so the UI
    /// thread is guaranteed to see it rather than a cached null: a snap that never
    /// switches on is the one failure here that would look like the feature not working.
    /// </remarks>
    private volatile BoundarySnapIndex? _boundaries;

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

    /// <summary>
    /// Raised by the gear on the recording strip. The preferences window belongs to the
    /// owner, and it has to outlive the overlay that asked for it.
    /// </summary>
    public event EventHandler? PreferencesRequested;

    public CaptureMonitor Monitor => _monitor;

    /// <summary>True once a region is chosen and the window is accepting annotations.</summary>
    private bool IsAnnotating => _selection is not null;

    public async Task ShowAsync()
    {
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(_monitorFrame.ToDisplayBitmap());
        PreviewImage.Source = source;
        BuildGrips();
        WireZoom();
        WireToolbar();
        WireSizeBox();
        WirePreSelectionButton();
        WireColorWheel();
        WireCanvas();
        WireFrameAnchor();

        // Covers both finishing and cancelling: the owner closes every overlay either
        // way, and a colour picked but not used is still the colour the user wants.
        Closed += (_, _) =>
        {
            AnnotationToolbar.PersistStyle();

            // A camera left running behind a closed window is the one failure here
            // nobody would forgive: the light beside the lens would stay on. An open
            // microphone is the same failure without the light to give it away.
            HideWebcamPreview();
            HideMicMeter();
        };

        var appWindow = this.GetAppWindow();
        var presenter = appWindow.MakeChromeless();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;

        // The client rect, not the window rect: the pointer's origin is the client's
        // origin, so a frame left round the window is a translation of every capture.
        // AppWindow positions in physical pixels, so the display's virtual-space bounds
        // go in unchanged — converting to layout units here would misplace the overlay
        // on every display that is not at 100%.
        appWindow.PlaceClient(new RectInt32(
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
        BuildBoundaryIndex();
    }

    /// <summary>
    /// Reads the lines out of this display's pixels, so a dragged edge can land on one.
    /// </summary>
    /// <remarks>
    /// Off the UI thread and not awaited: it reads every pixel on the display, and the
    /// overlay has to be up and taking a drag immediately. Until it lands, and if it never
    /// does, every snapping call sees a null index and leaves the selection where the
    /// pointer put it — the first second of a capture is worth more than the first second
    /// of snapping.
    /// </remarks>
    private void BuildBoundaryIndex()
    {
        if (!_settings.Current.BoundarySnap)
        {
            return;
        }

        var frame = _monitorFrame;

        // Frame space, not virtual: the index is asked about edges in the coordinates a
        // pointer arrives in, and CapturedFrame carries a virtual origin that is negative
        // on any layout with a display left of or above the primary. Handing that over
        // put every lookup off by the virtual origin, which clamped it to the edge of the
        // capture and left the whole feature silently dead on those layouts.
        var origin = _layout.VirtualToFrame(new CapturePoint(frame.VirtualX, frame.VirtualY));

        _ = Task.Run(() =>
        {
            var index = BoundarySnapIndex.Build(
                frame.BgraPixels,
                frame.Width,
                frame.Height,
                (int)origin.X,
                (int)origin.Y);

            // Assigned from the worker: a reference store is atomic, every read of it is
            // one load on the UI thread, and marshalling back would queue work behind the
            // pointer moves this exists to serve.
            _boundaries = index;
        });
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

        // A click with a ruler being offered takes the offer, and means nothing else: the
        // user is holding a key that has put a measurement under the pointer, so starting
        // to drag a second one by hand is not what the click was for.
        if (_autoSpanVertical is not null && TakeAutoSpan())
        {
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

        // Before the tools get the press, so the second click of a double-click finishes
        // the capture instead of starting a mark on the picture it is about to deliver.
        if (IsDoubleClick(e) && ConfirmOnDoubleClick(ToFrame(e)))
        {
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

            var at = ToFrame(e);
            var grabbed = _editor.PointerPressed(at, ToModifiers(e), PenInput.Of(e));

            // Sprite tools are placed with a click rather than dragged out — but only
            // where the click did not land on a mark already drawn, which the editor has
            // just taken hold of instead. Placing a label or a badge on top of it would
            // leave every text, number and stamp unmovable without switching tools first,
            // where macOS grabs whatever is under the pointer whatever tool is in hand.
            if (!grabbed && AnnotationCanvasView.IsPlacedByClick(_editor.Tool))
            {
                AnnotationCanvas.PlaceSprite(at);
                return;
            }

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
        // First, and before any of the early returns below: the auto-measure offer needs
        // this whatever else the pointer is currently doing, and it is the one reader that
        // is driven by the keyboard rather than by this event.
        _pointerAt = ToFrame(e);
        if (_autoSpanVertical is not null)
        {
            UpdateAutoSpan();
        }

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
                _editor.PointerMoved(ToFrame(e), ToModifiers(e), PenInput.Of(e));
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
        // The selected mark's handles answer to every tool, so the cursor has to as well:
        // a crosshair over a handle that is about to reshape something rather than draw
        // is the interface lying about what the press will do.
        if (_editor.SelectionShown is { } shown
            && AnnotationHandles.At(shown, point, _editor.Scale) is { } handle)
        {
            return CursorHints.For(handle.Kind);
        }

        if (_editor.Tool != AnnotationTool.Select)
        {
            return InputSystemCursorShape.Cross;
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

        // Whatever this release ends, the line saying where an edge landed has said it.
        // Left up, it would sit across the capture as something the user has to work out
        // the meaning of — and it is not in the delivered pixels, which makes it worse.
        HideBoundaryGuides();

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
            HideBoundaryGuides();
            _resizing = SelectionHandle.None;
            ApplyRegion(resized);
            return;
        }

        if (IsAnnotating)
        {
            var committed = _editor.PointerReleased(ToFrame(e), ToModifiers(e), PenInput.Of(e));
            RenderAnnotations();

            // After the release, because what a ruler reads, what text a highlighter
            // crossed and what words a redaction covers are none of them knowable until
            // the drag that made the mark has stopped.
            AnnotationCanvas.FinishedGesture(committed);
            return;
        }

        if (_selectionStart is not { } start)
        {
            return;
        }

        var end = e.GetCurrentPoint(SelectionCanvas).Position;
        var taken = DrawMarquee(start, end);
        HideBoundaryGuides();
        _selectionStart = null;
        _marqueeAt = null;
        ShowIdleInstruction();

        // With an exact size chosen there was never a gesture to measure: the press placed
        // a box the menu had already sized, and asking how far the pointer travelled would
        // turn every one of them into a click for the whole screen. macshot measures the
        // rectangle rather than the gesture for this reason (OverlayView.swift:6584).
        if (_preSelection.IsExact && !taken.IsEmpty)
        {
            EnterAnnotationPhase(taken);
            return;
        }

        // The gesture measured where the pointer went, not where the snap put it: a drag
        // of two pixels that landed on a line is still a click asking for the window
        // under it.
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

        if (!taken.IsEmpty)
        {
            EnterAnnotationPhase(taken);
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

            EnterAnnotationPhase(window.Bounds, captured, window.Title, window);

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

    /// <param name="snapped">
    /// The window the region is, when it came from clicking one. Defaulted to null so
    /// every other way of choosing a region clears it rather than having to remember to.
    /// </param>
    private void EnterAnnotationPhase(
        CaptureRegion region,
        CapturedFrame? capturedWindow = null,
        string? windowTitle = null,
        CaptureWindow? snapped = null)
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
        _snappedWindow = snapped;
        _capturedWindow = capturedWindow;
        _capturedWindowTitle = windowTitle;
        _regionIsAdjustable = capturedWindow is null;
        AnnotationToolbar.SnappedWindow = windowTitle is not null;

        // The row opens one dialog — the frame's background picture — and this window is
        // topmost, so a dialog it does not own would open behind it.
        AnnotationToolbar.OwnerHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SnapHighlight.Visibility = Visibility.Collapsed;

        // The next drag has become this one. Taken away here rather than left to the pill,
        // because the automatic intents below never clear the pill — a quick capture or a
        // scroll would carry the button into the finished picture.
        PlacePreSelectionButton(shown: false);

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

        // macshot's auto modes. A region chosen for its text, for a scroll or for a quick
        // capture is not a region to draw on, so the toolbar never appears — putting one
        // up for the one frame before the intent takes over would be a bar that flickers
        // past. A scroll with nothing behind it is the one intent that can fail before it
        // starts, and that one falls back to the toolbar rather than to nothing.
        //
        // Recording is deliberately not one of them: it gets a strip of its own, because
        // whether the microphone was on is not something a recording can be asked
        // afterwards.
        var automatic = Intent is CaptureIntent.Recognize or CaptureIntent.Quick
            || (Intent is CaptureIntent.Scroll && WindowBehind(region) is not null);

        if (!automatic)
        {
            AnnotationToolbar.RecordingSetup = Intent is CaptureIntent.Record;
            AnnotationToolbar.ShowToolbar(true);
            _sizeBox.Visibility = Visibility.Visible;

            // Before the chrome is placed, because a frame armed from the last capture
            // moves the toolbar out to its edge and placing it twice would show the jump.
            _beautify = _settings.Current.BeautifyEnabled;
            AnnotationToolbar.Beautified = _beautify;
            ShowFrame();

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
            // Nothing to start: the strip is already the recording one, and Start is the
            // user's press. What this does is put the camera up, so the bubble can be
            // seen and dragged before it is in the file rather than after, and open the
            // microphone, so its button says whether it is hearing anything.
            ShowWebcamPreview();
            ShowMicMeter();
            break;
        case CaptureIntent.Recognize:
            _ = ReadTextAsync();
            break;
#if !OFFLINE
        case CaptureIntent.Translate:
            _ = TranslateAsync();
            break;
#endif
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
        BoundaryGuideX.Stroke = ToolbarPalette.AccentBrush;
        BoundaryGuideY.Stroke = ToolbarPalette.AccentBrush;

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

        // Onto the lines in the picture before the shape is applied, the order macshot
        // uses: a snap afterwards would pull one edge off the ratio the user asked to
        // hold, where this way the ratio is worked out from where the edge ended up.
        var snapped = BoundarySnapping.Resize(dragged, _resizing, Boundaries, BoundaryRadius);
        ShowBoundaryGuides(snapped);

        // A held shape applies to the grips too. One that only applied to typed numbers
        // would come apart the first time anyone dragged an edge, which is how the region
        // is adjusted the rest of the time.
        return _lockedAspect is { } aspect
            ? SelectionSizing.ConstrainToAspect(snapped.Region, aspect, _resizing, MonitorBounds)
            : snapped.Region;
    }

    /// <summary>
    /// The lines a dragged edge may land on, or none while Alt is held.
    /// </summary>
    /// <remarks>
    /// Alt is macshot's Option: the way past the snap when the edge belongs a pixel off
    /// the border rather than on it. Without it there is no way to place an edge inside
    /// the radius of a line at all, and a feature that cannot be switched off mid-gesture
    /// is one the user has to visit the settings to escape.
    /// </remarks>
    private BoundarySnapIndex? Boundaries => IsDown(VirtualKey.Menu) ? null : _boundaries;

    /// <summary>How near a line has to be, in this display's pixels.</summary>
    private double BoundaryRadius => BoundarySnapping.Radius * _monitor.Scale;

    /// <summary>
    /// Draws the lines the selection just landed on, right across the display.
    /// </summary>
    /// <remarks>
    /// Across the display rather than along the region, unlike the guides that line marks
    /// up with each other: this one is saying the edge is now exactly on something in the
    /// picture, which a line no longer than the region cannot show.
    /// </remarks>
    private void ShowBoundaryGuides(BoundarySnap snap)
    {
        var bounds = LayoutBounds;

        if (snap.GuideX is { } x)
        {
            var at = _layout.FrameToPointer(_monitor, new CapturePoint(x, 0)).X;
            BoundaryGuideX.X1 = at;
            BoundaryGuideX.X2 = at;
            BoundaryGuideX.Y1 = 0;
            BoundaryGuideX.Y2 = bounds.Height;
            BoundaryGuideX.Visibility = Visibility.Visible;
        }
        else
        {
            BoundaryGuideX.Visibility = Visibility.Collapsed;
        }

        if (snap.GuideY is { } y)
        {
            var at = _layout.FrameToPointer(_monitor, new CapturePoint(0, y)).Y;
            BoundaryGuideY.X1 = 0;
            BoundaryGuideY.X2 = bounds.Width;
            BoundaryGuideY.Y1 = at;
            BoundaryGuideY.Y2 = at;
            BoundaryGuideY.Visibility = Visibility.Visible;
        }
        else
        {
            BoundaryGuideY.Visibility = Visibility.Collapsed;
        }
    }

    private void HideBoundaryGuides()
    {
        BoundaryGuideX.Visibility = Visibility.Collapsed;
        BoundaryGuideY.Visibility = Visibility.Collapsed;
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
        // is where the region appears on screen, not where it is on the capture. Around
        // the frame rather than the region once one is armed — the strips sit against the
        // edge of what is being made, and with a frame on, that edge is the frame's.
        var anchor = _viewport.ToView(ToLayout(ChromeAnchor(region)));

        // The box, though, stays on the capture — macshot hangs it off selectionRect and
        // never off the expanded anchor (OverlayView.swift:2321 against 5077–5096). With a
        // frame armed that puts it inside the gradient, tight against the pixels whose
        // size it is reporting, rather than adrift out beyond the frame's edge.
        var capture = _viewport.ToView(ToLayout(region));
        var screen = LayoutBounds;

        // The region's own size, not the frame's: this is the number the user is
        // choosing, and the padding around it is not part of what they picked.
        _sizeBox.Show(region.Width, region.Height);

        // The box first, so the toolbar knows what to keep clear of, and the box again
        // once the strips have settled. Each is placed around the other, and a single pass
        // would leave whichever went first sitting under the other.
        PlaceSizeBox(capture, screen);
        AnnotationToolbar.Reposition(anchor, screen, _sizeBoxBounds);
        PlaceSizeBox(capture, screen);
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
    /// <summary>
    /// Says that the region has stopped being the window it was clicked out of.
    /// </summary>
    /// <remarks>
    /// The two together rather than each on its own: the grips coming back and the
    /// recording stopping following are the same fact, and a site that set one without
    /// the other would either record a window the user has moved the region off, or leave
    /// a region that cannot be adjusted still claiming to be a window.
    /// </remarks>
    private void RegionIsNoLongerAWindow()
    {
        _regionIsAdjustable = true;
        _snappedWindow = null;
    }

    private void BeginRegionMove()
    {
        if (_selection is not { } region || _movingFrom is not null)
        {
            return;
        }

        // Moving a window capture makes it an ordinary region: its pixels stop being the
        // window's own the moment it is over something else, so from here it is cropped
        // out of the screenshot like any other and its grips come back with it.
        RegionIsNoLongerAWindow();

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
        HideBoundaryGuides();
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

        var moved = SelectionHandles.ClampTo(
            new CaptureRegion(
                pointer.X - _moveGrip.Value.X,
                pointer.Y - _moveGrip.Value.Y,
                original.Width,
                original.Height),
            MonitorBounds);

        // The whole region shifts onto the line, because a move may not resize what is
        // being moved: whichever of the two edges is nearer one wins the axis.
        var snapped = BoundarySnapping.Move(moved, Boundaries, BoundaryRadius);
        ShowBoundaryGuides(snapped);

        _movePending = SelectionHandles.ClampTo(snapped.Region, MonitorBounds);
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
        HideBoundaryGuides();
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

        // After the region is settled rather than during the drag, for the reason the
        // pixels are: the frame is the size of a capture to paint, and the marquee is
        // what says where the edges are on the way there.
        ShowFrame();

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

            // Ahead of the single-key tool shortcuts below, which is where a bare 1 would
            // otherwise land. Only with the ruler in hand, so the digits still pick tools
            // for every other one.
            case VirtualKey.Number1 or VirtualKey.NumberPad1
                or VirtualKey.Number2 or VirtualKey.NumberPad2
                when IsAnnotating && !control && !shift && !IsDown(VirtualKey.Menu)
                    && _editor.Tool == AnnotationTool.Measure:
                e.Handled = true;
                OfferAutoSpan(e.Key is VirtualKey.Number1 or VirtualKey.NumberPad1);
                return;

            default:
                // A single key is a shortcut only once a region is chosen: before that
                // there is no toolbar to shadow, and P would arm a pencil the user cannot
                // see and cannot draw with. Modifiers are left alone — Ctrl and Alt are how
                // this window's own commands and the system's are told apart from a letter.
                if (!control && !IsDown(VirtualKey.Menu) && IsAnnotating
                    && AnnotationToolbar.TryShortcut(e.Key))
                {
                    e.Handled = true;
                }

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
        AnnotationToolbar.FrameStyleChosen += (_, _) => FrameStyleChosen();
        AnnotationToolbar.FrameOptionsChanged += (_, _) => FrameOptionsChanged();
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
            Hint(L("{0:0}% • Scroll to zoom • Middle-drag to pan", viewport.Scale * 100));
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

        _sizeBox.PixelsPerPoint = _monitor.Scale;
        _sizeBox.ShowPoints = _settings.Current.ResolutionUnitIsPoints;
        _sizeBox.KeepRatio = _settings.Current.KeepAspectRatio;

        // Whatever the last capture left behind is in hand before the first drag, so a
        // region dragged out is already the shape that was chosen — and a ratio is the only
        // one of the three that becomes a lock, because an exact size is placed rather than
        // dragged. macshot takes the same value at the same moment
        // (OverlayView.swift:9946, :6662-6669).
        _preSelection = _settings.Current.ActivePreSelection;
        _lockedAspect = _preSelection.Ratio;
        _sizeBox.LockedAspect = _lockedAspect;

        _sizeBox.UnitPicked += (_, points) =>
        {
            _sizeBox.ShowPoints = points;
            _settings.Save(_settings.Current with { ResolutionUnitIsPoints = points });

            // Re-read straight away: the panel is still open over the box, and a unit that
            // only took effect on the next pointer move would look like it had not.
            if (_selection is { } region)
            {
                _sizeBox.Show(region.Width, region.Height);
            }
        };

        _sizeBox.KeepRatioToggled += (_, on) => KeepRatioChanged(on);

        // The overlay's keyboard is only on loan to the fields. Without this, Escape and
        // every tool shortcut would keep going to a text box the user has finished with.
        _sizeBox.EditingEnded += (_, _) => OverlayRoot.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Binds the button that shapes the next drag, which stands under the instruction while
    /// nothing has been chosen.
    /// </summary>
    private void WirePreSelectionButton()
    {
        PreSelectionLayer.Children.Add(_preSelectionButton);
        _preSelectionButton.Visibility = Visibility.Collapsed;
        _preSelectionButton.KeepRatio = _sizeBox.KeepRatio;
        _preSelectionButton.Update(_preSelection);
        _preSelectionButton.PresetPicked += (_, preset) => PickPreSelectionPreset(preset);
        _preSelectionButton.KeepRatioToggled += (_, on) => KeepRatioChanged(on);
    }

    /// <summary>
    /// Whether a shape picked anywhere outlives the capture it was picked on.
    /// </summary>
    /// <remarks>
    /// Shared by the two panels that carry the switch — the size box's and the
    /// pre-selection button's. They are the same panel showing the same stored answer, and
    /// one of them left holding the state before the flick would show it wrong the next
    /// time it opened.
    /// </remarks>
    private void KeepRatioChanged(bool on)
    {
        _sizeBox.KeepRatio = on;
        _preSelectionButton.KeepRatio = on;

        _settings.Save(_settings.Current with
        {
            KeepAspectRatio = on,

            // Stored as it is switched on, so the shape in hand is the one that
            // carries over — switching it on and then having to re-pick the shape
            // would make the switch look like it did nothing.
            KeepAspectRatioValue = on ? _lockedAspect ?? 0 : _settings.Current.KeepAspectRatioValue,
        });
    }

    /// <summary>
    /// Takes a shape or a size for the drag that has not happened yet.
    /// </summary>
    /// <remarks>
    /// A ratio becomes the lock the marquee is held to as it is dragged out; an exact size
    /// clears the lock, because from here the press places a box of that size rather than
    /// dragging one. macshot's <c>setPreSelectionPreset</c> does both
    /// (<c>OverlayView.swift:2777-2791</c>).
    /// </remarks>
    private void PickPreSelectionPreset(ResolutionPreset preset)
    {
        StorePreSelection(preset.IsExact
            ? PreSelectionPreset.OfSize(preset.Width, preset.Height)
            : PreSelectionPreset.OfRatio(preset.Aspect ?? 0));

        _lockedAspect = _preSelection.Ratio;
        _sizeBox.LockedAspect = _lockedAspect;

        // The pill is unchanged, but the button on it now says something else — and it is
        // placed and repainted from here.
        PlaceHint();
    }

    /// <summary>
    /// Remembers what the next drag is to produce, on this overlay and in the settings.
    /// </summary>
    private void StorePreSelection(PreSelectionPreset preset)
    {
        _preSelection = preset;
        _preSelectionButton.Update(preset);

        try
        {
            _settings.Save(_settings.Current.WithPreSelection(preset));
        }
        catch (Exception exception)
        {
            // This capture still honours the choice — only the next one loses it. Said out
            // loud rather than swallowed, because the button goes on showing the shape and
            // would otherwise be the only thing that knew.
            DiagnosticLog.Write($"Could not remember the pre-selection preset: {exception.Message}");
        }
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
        RegionIsNoLongerAWindow();

        ApplyRegion(SelectionSizing.Resize(
            current,
            request.Width,
            request.Height,
            MonitorBounds,
            _lockedAspect,
            request.Edited));

        // A size typed over the one a preset placed is the user leaving that preset. Left
        // stored, the next capture would open with a box they have just resized away from,
        // and the menu would go on ticking a size the region is not. macshot clears it from
        // this same commit (OverlayView.swift:2482-2483, :2726-2732).
        if (_preSelection.IsExact && _selection is { } resized
            && (Math.Abs(resized.Width - _preSelection.Width) >= 0.5
                || Math.Abs(resized.Height - _preSelection.Height) >= 0.5))
        {
            StorePreSelection(PreSelectionPreset.Freeform);
        }
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

        RegionIsNoLongerAWindow();

        // Whether the choice reaches the next capture is the keep-ratio switch's business,
        // and that is the whole difference between picking 16 : 9 for this screenshot and
        // working in 16 : 9. With it off the next drag starts freeform whatever was picked
        // here, which is macshot's rule at OverlayView.swift:2577-2589 and :2628-2635.
        StorePreSelection(!_sizeBox.KeepRatio
            ? PreSelectionPreset.Freeform
            : preset.IsExact
                ? PreSelectionPreset.OfSize(preset.Width, preset.Height)
                : PreSelectionPreset.OfRatio(preset.Aspect ?? 0));

        if (preset.IsExact)
        {
            HoldAspect(null);
            ApplyRegion(SelectionSizing.Resize(current, preset.Width, preset.Height, MonitorBounds));
            return;
        }

        HoldAspect(preset.Aspect);
        if (preset.Aspect is { } aspect)
        {
            ApplyRegion(SelectionSizing.ApplyAspect(current, aspect, MonitorBounds));
        }
    }

    /// <summary>
    /// Takes a shape, and writes it down when it is to outlive this capture.
    /// </summary>
    /// <remarks>
    /// Only written while keep-ratio is on. With it off, a shape picked here is for this
    /// capture and the next drag starts freeform, which is macshot's rule too — the switch
    /// is the whole difference between the two.
    /// </remarks>
    private void HoldAspect(double? aspect)
    {
        _lockedAspect = aspect;
        _sizeBox.LockedAspect = aspect;

        if (_sizeBox.KeepRatio)
        {
            _settings.Save(_settings.Current with { KeepAspectRatioValue = aspect ?? 0 });
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

            case ToolbarCommand.RedactAllText:
                _ = RedactAllTextAsync();
                return;

            case ToolbarCommand.RedactFaces:
                _ = RedactFacesAsync();
                return;

            case ToolbarCommand.RedactPeople:
                _ = RedactPeopleAsync();
                return;

            case ToolbarCommand.LoadStampImage:
                _ = LoadStampImageAsync();
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
                ArmBeautify();
                return;

            case ToolbarCommand.RemoveBackground:
                _ = RemoveBackgroundAsync();
                return;

            case ToolbarCommand.ScrollCapture:
                RequestScrollCapture();
                return;

            case ToolbarCommand.Record:
                EnterRecordingSetup();
                return;

            case ToolbarCommand.StartRecording:
                RequestRecording();
                return;

            case ToolbarCommand.CancelRecording:
                LeaveRecordingSetup();
                return;

            case ToolbarCommand.Webcam:
                ShowWebcamPreview();
                return;

            // The switch itself is the toolbar's, as the other four recording switches
            // are. What arrives here is the consequence of it: the microphone has to be
            // opened or closed to match, and the toolbar has nowhere to keep one.
            case ToolbarCommand.MicAudio:
                ShowMicMeter();
                return;

            case ToolbarCommand.RecordingSettings:
                PreferencesRequested?.Invoke(this, EventArgs.Empty);
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

            case ToolbarCommand.Upload:
#if !OFFLINE
                // Asked before the overlays come down, so the question is in front of the
                // capture it is about rather than over whatever was behind it.
                if (!_settings.Current.UploadConfirm
                    || Upload.UploadConfirm.Ask(
                        WinRT.Interop.WindowNative.GetWindowHandle(this),
                        _settings.Current.UploadProvider))
                {
                    _ = CompleteAsync(CaptureOutcome.Upload);
                }
#endif
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
    /// macshot's own button is: the frame goes on at the end, over the marks, and until
    /// then it is shown around the region rather than baked into it.
    /// </remarks>
    private void ArmBeautify()
    {
        if (!IsAnnotating || _beautify)
        {
            return;
        }

        // Written down rather than held for this capture alone: the row's On switch reads
        // it back, and a frame that vanished between two captures while the switch still
        // said On would be the row lying about what the file will look like.
        _settings.Save(_settings.Current with { BeautifyEnabled = true });
        ShowFrameFromSettings();
    }

    /// <summary>
    /// Repaints the frame from the settings, arming or disarming it to match.
    /// </summary>
    /// <remarks>
    /// The one way in for everything the frame's options row does. It reads rather than is
    /// told, because the row writes its answer to the settings before it says anything —
    /// the padding, the corner, the shadow, the background and the switch all arrive the
    /// same way, and the export reads them from the same place.
    /// </remarks>
    /// <summary>
    /// How this region is framed: the settings, with the card the region can actually take.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A snapped window already carries a real title bar in its pixels, so window mode
    /// would draw a second one above the first. macshot renders that region through a
    /// separate path that lays down no synthetic chrome at all
    /// (<c>BeautifyRenderer.swift:480-492</c>) and takes the W/R segments off the row;
    /// forcing the mode here is the same picture, and it is what the hidden segments have
    /// to mean — a control that is not there cannot be the only thing preventing the wrong
    /// frame.
    /// </para>
    /// <para>
    /// The corner goes the same way and for the same reason: those pixels arrive already
    /// rounded to the shell's own radius, so the card has to be cut to match or the frame
    /// shows a sliver of gradient inside each corner. macshot uses 10 there
    /// (<c>OverlayView.swift:3018</c>), which is macOS's window corner; Windows 11 rounds
    /// its own at 8.
    /// </para>
    /// </remarks>
    private BeautifyOptions FrameOptions
    {
        get
        {
            var options = _settings.Current.ToBeautifyOptions(BeautifyBackgroundStore.Current);
            return _capturedWindowTitle is null
                ? options
                : options with { Mode = BeautifyMode.Rounded, CornerRadius = ShellWindowCorner };
        }
    }

    /// <summary>What Windows 11 rounds a top-level window's corners to, in points.</summary>
    private const double ShellWindowCorner = 8;

    private void FrameOptionsChanged()
    {
        if (!IsAnnotating)
        {
            return;
        }

        ShowFrameFromSettings();
    }

    private void ShowFrameFromSettings()
    {
        var armed = _settings.Current.BeautifyEnabled;
        var arriving = armed != _beautify;

        _beautify = armed;
        AnnotationToolbar.Beautified = armed;
        ShowFrame();

        // Nothing is said about it. macshot announces neither the frame arriving nor its
        // going, and the reason shows once the pill has somewhere to be: it would stand
        // over the capture for as long as it lasted, saying what the user can already see
        // has happened to the picture underneath it.
        if (arriving)
        {
            MoveChromeToFrame();
            return;
        }

        // A padding drag moves the strips without the frame arriving or leaving, and
        // animating them from where they are to where they are is a flinch.
        if (_selection is { } region)
        {
            RepositionChrome(region);
        }
    }

    /// <summary>
    /// Paints the frame around the region, or takes it away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What the user sees is <see cref="BeautifyRenderer.Backdrop"/> — the background and
    /// the shadow the file will have, with the capture's own area left clear — laid over
    /// the capture where it already is. Composited that is the same picture
    /// <see cref="BeautifyRenderer.Render"/> makes, which is the point: a preview drawn
    /// by other means would be a promise the file may not keep, and the padding, the
    /// corner and both shadows here are not a second set of numbers but the ones the
    /// export uses.
    /// </para>
    /// <para>
    /// The region is not touched. Growing what the capture is presented at would move
    /// every mark on it, change what the grips snap to and change what is delivered, so
    /// the frame is drawn beside the region instead: it grows outwards from an origin
    /// that stays put.
    /// </para>
    /// <para>
    /// Repainted whenever the picture in it would differ — the switch, a background
    /// chosen from the picker, a region re-cropped, a slider on the frame's options row.
    /// The three measurements are read from the settings on each pass rather than held,
    /// which is what lets that row change them without telling this anything.
    /// </para>
    /// </remarks>
    private void ShowFrame()
    {
        var region = _selection ?? default;

        // Never while a recording is being set up. What that region delivers is a video,
        // which no frame is ever put around, so a gradient sitting over it would promise
        // something the file cannot have — macshot guards its own preview on the same two
        // states (OverlayView.swift:1886).
        if (!_beautify
            || AnnotationToolbar.RecordingSetup
            || (int)region.Width <= 0
            || (int)region.Height <= 0)
        {
            BeautifyFrame.Visibility = Visibility.Collapsed;

            // Dropped rather than kept for next time: it is the size of a capture, and
            // the overlay holds one per display.
            BeautifyFrame.Source = null;
            return;
        }

        var options = FrameOptions;
        var (width, height, pixels) = BeautifyRenderer.Backdrop(
            (int)region.Width,
            (int)region.Height,
            options,
            _monitor.Scale);

        var bitmap = new WriteableBitmap(width, height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels, 0, pixels.Length);
        }

        var placed = ToLayout(BeautifyRenderer.FrameAround(region, options, _monitor.Scale));
        Canvas.SetLeft(BeautifyFrame, placed.X);
        Canvas.SetTop(BeautifyFrame, placed.Y);
        BeautifyFrame.Width = placed.Width;
        BeautifyFrame.Height = placed.Height;
        BeautifyFrame.Source = bitmap;
        BeautifyFrame.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Where the toolbar and the size box hang off, which is the frame once there is one.
    /// </summary>
    /// <remarks>
    /// macshot slides them out to the grown edge rather than jumping them there —
    /// <c>OverlayView.swift:5082–5097</c> — and this is its curve and its twelve frames.
    /// The reason is that the strips sit against the region: moved without the move being
    /// shown, a toolbar that was under your pointer is suddenly a frame's width away, and
    /// the eye has to find it again rather than follow it.
    /// </remarks>
    private CaptureRegion ChromeAnchor(CaptureRegion region)
    {
        if (!_beautify && _frameAnchorProgress >= 1)
        {
            return region;
        }

        var framed = BeautifyRenderer.FrameAround(
            region, FrameOptions, _monitor.Scale);
        if (_frameAnchorProgress >= 1)
        {
            return _beautify ? framed : region;
        }

        // Eased out rather than linear: it leaves quickly and settles, which reads as the
        // strip being carried by the frame rather than driven by a clock.
        var eased = 1 - ((1 - _frameAnchorProgress) * (1 - _frameAnchorProgress));
        var from = _beautify ? region : framed;
        var to = _beautify ? framed : region;

        return new CaptureRegion(
            from.X + ((to.X - from.X) * eased),
            from.Y + ((to.Y - from.Y) * eased),
            from.Width + ((to.Width - from.Width) * eased),
            from.Height + ((to.Height - from.Height) * eased));
    }

    /// <summary>Starts the strips moving between the region and the frame.</summary>
    private void MoveChromeToFrame()
    {
        if (_selection is null)
        {
            return;
        }

        _frameAnchorProgress = 0;
        _frameAnchor.Start();
    }

    private void WireFrameAnchor()
    {
        _frameAnchor.Tick += (_, _) =>
        {
            // A twelfth of the way each frame, which is macshot's step and its fifth of
            // a second.
            _frameAnchorProgress = Math.Min(1, _frameAnchorProgress + 0.08);
            if (_frameAnchorProgress >= 1)
            {
                _frameAnchor.Stop();
            }

            if (_selection is { } region)
            {
                RepositionChrome(region);
            }
        };

        // A timer left running behind a closed overlay would keep the window alive and
        // go on placing a toolbar nobody can see.
        Closed += (_, _) => _frameAnchor.Stop();
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
    private void FrameStyleChosen()
    {
        if (!IsAnnotating)
        {
            return;
        }

        // Through the switch the row reads, not past it: picking a background while the
        // row is open has to leave On ticked, and the picker is reachable from that row.
        _settings.Save(_settings.Current with { BeautifyEnabled = true });

        // The picker writes the style down before this runs, so the repaint reads the
        // background that was just chosen rather than the one before it.
        ShowFrameFromSettings();
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
            FrameOptions,
            _monitor.Scale);

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

    /// <summary>
    /// The language <see cref="CaptureIntent.Translate"/> translates into, or null for
    /// the one the settings name.
    /// </summary>
    /// <remarks>
    /// macshot's <c>autoTranslateOverlayLang</c>: <c>macshot://ocr-translate?target=…</c>
    /// names a language for that one capture without disturbing the saved default, which
    /// is what lets a launcher hold two links for two languages.
    /// </remarks>
    public string? TranslateTarget { get; set; }

    /// <summary>Asks for the region to be recorded rather than captured.</summary>
    /// <remarks>
    /// A region that came from clicking a window asks for that window instead, following
    /// it wherever it goes. The gesture is the one that already takes a still of a window,
    /// rather than a mode of its own: what is being recorded is what was pointed at, and
    /// the highlight said which that was.
    /// </remarks>
    private void RequestRecording()
    {
        if (_selection is not { } region)
        {
            return;
        }

        // Before the request, because the recording opens a bubble of its own and this
        // one is over the same corner of the same region — and a microphone of its own,
        // which this one has no reason to still be holding.
        HideWebcamPreview();
        HideMicMeter();

        RecordingRequested?.Invoke(
            this,
            new RecordingRequest(_monitor, _layout.FrameToVirtual(region), _snappedWindow));
    }

    /// <summary>
    /// Turns the action strip into the recording one, which is what the Record button
    /// does rather than starting anything.
    /// </summary>
    private void EnterRecordingSetup()
    {
        AnnotationToolbar.RecordingSetup = true;
        ShowFrame();
        ShowWebcamPreview();
        ShowMicMeter();
    }

    /// <summary>Goes back to the ordinary strip, having recorded nothing.</summary>
    private void LeaveRecordingSetup()
    {
        AnnotationToolbar.RecordingSetup = false;
        ShowFrame();
        HideWebcamPreview();
        HideMicMeter();
    }

    /// <summary>
    /// Opens the microphone and feeds its level to the button that switches it on, or
    /// closes it again to match what that switch now says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only while the recording is being set up, and never during the recording itself: it
    /// is a check on the microphone before the one moment the answer still matters, and
    /// once the file is being written the strip it draws into is gone.
    /// </para>
    /// <para>
    /// A microphone that will not open leaves the switch on and the bar at nothing, which
    /// is what a machine with the microphone turned off in Windows privacy settings looks
    /// like — and is the reading the meter exists to give.
    /// </para>
    /// </remarks>
    private void ShowMicMeter()
    {
        if (!AnnotationToolbar.RecordingSetup || !_settings.Current.RecordMicAudio)
        {
            HideMicMeter();
            return;
        }

        if (_micMeter is not null)
        {
            return;
        }

        if (MicrophoneMeter.Start() is not { } meter)
        {
            DiagnosticLog.Write("No microphone would open for the level meter, so it stays at nothing.");
            return;
        }

        meter.LevelChanged += (_, level) => AnnotationToolbar.ShowMicLevel(level);
        _micMeter = meter;
    }

    /// <summary>
    /// Closes the microphone and takes the bar down with it.
    /// </summary>
    /// <remarks>
    /// Before the recording starts as well as when the switch goes off: the recording opens
    /// the microphone for itself, and an open stream left behind by a window that is about
    /// to close would be macshot holding the microphone with nothing on screen saying so.
    /// </remarks>
    private void HideMicMeter()
    {
        if (_micMeter is not { } meter)
        {
            return;
        }

        _micMeter = null;
        meter.Dispose();
    }

    /// <summary>
    /// Puts the camera bubble up, or takes it down, to match what the switch now says.
    /// </summary>
    /// <remarks>
    /// Up during setup rather than only once recording starts, because what the bubble
    /// covers has to be seen before the recording — a face over the thing being
    /// demonstrated is not something to discover in the finished video.
    /// </remarks>
    private void ShowWebcamPreview()
    {
        if (!AnnotationToolbar.RecordingSetup
            || !_settings.Current.RecordWebcam
            || _selection is not { } region)
        {
            HideWebcamPreview();
            return;
        }

        if (_webcam is not null)
        {
            return;
        }

        _ = OpenWebcamPreviewAsync(region);
    }

    private async Task OpenWebcamPreviewAsync(CaptureRegion region)
    {
        var bubble = new WebcamWindow();
        _webcam = bubble;

        var settings = _settings.Current;
        var started = await bubble.ShowInAsync(
            _layout.FrameToVirtual(region),
            settings.WebcamCorner,
            settings.WebcamSize,
            settings.WebcamShape,
            _monitor.Scale);

        // Closed rather than left as a black circle over the region. The switch stays on:
        // it is a preference about recordings, and this is one machine's camera failing
        // to open, which the log already says.
        if (!started || !ReferenceEquals(_webcam, bubble))
        {
            if (ReferenceEquals(_webcam, bubble))
            {
                _webcam = null;
            }

            await bubble.StopAsync();
        }
    }

    /// <summary>Takes the camera bubble down and releases the camera with it.</summary>
    private void HideWebcamPreview()
    {
        if (_webcam is not { } bubble)
        {
            return;
        }

        _webcam = null;
        _ = bubble.StopAsync();
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
        AnnotationCanvas.StampPicture = () => AnnotationToolbar.StampPicture;
        AnnotationCanvas.NumberStartAt = () => AnnotationToolbar.NumberStartAt;
        AnnotationCanvas.SmartMarker = () => AnnotationToolbar.SmartMarker;
        AnnotationCanvas.CensorTextOnly = () => AnnotationToolbar.CensorTextOnly;
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

            var text = TextRecognizer.ToText(lines);
            var action = _settings.Current.OcrAction;

            if (action is OcrAction.ShowAndCopy or OcrAction.CopyOnly)
            {
                CopyRecognized(text, codes);
            }

            // Nothing to show, so nothing to open: the capture simply ends with the words
            // on the clipboard, which is the whole of what this answer asks for.
            if (action is OcrAction.CopyOnly)
            {
                Cancelled?.Invoke(this, EventArgs.Empty);
                return;
            }

            var window = new TextRecognitionWindow(text, _settings, frame, codes);

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

    /// <summary>
    /// Puts what was read on the clipboard, for the two answers that ask for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The payloads when there were no words, which is macshot's <c>copyText</c>: a
    /// region holding nothing but a QR code was read for the code, and copying an empty
    /// string over whatever the user already had would be the worst possible answer.
    /// </para>
    /// <para>
    /// A failure is reported in the pill rather than thrown. Another process can hold the
    /// clipboard open, and the region has still been read — with the window coming up
    /// the text is still there to copy by hand.
    /// </para>
    /// </remarks>
    private void CopyRecognized(string text, IReadOnlyList<QrCode> codes)
    {
        var copied = string.IsNullOrWhiteSpace(text) && codes.Count > 0
            ? string.Join(Environment.NewLine, codes.Select(code => code.Value))
            : text;

        if (string.IsNullOrWhiteSpace(copied))
        {
            return;
        }

        try
        {
            ClipboardText.Copy(copied);
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
            var annotations = AutoRedactor.Redact(
                lines,
                RedactionStyle(),
                kinds: _settings.Current.RedactedPiiKinds());

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
            Hint(L("Redacted {0} • Ctrl+Z to undo • Enter to finish", annotations.Count));
        });
    }

    /// <summary>
    /// Covers every line of text in the region rather than only what looks like a secret.
    /// </summary>
    /// <remarks>
    /// The other half of macshot's auto group, and what is used when the answer is already
    /// known to be "all of it": a panel of somebody else's data, where naming what is
    /// sensitive is work the user should not have to do and a pattern that missed one would
    /// be a leak rather than a missed box.
    /// </remarks>
    private async Task RedactAllTextAsync()
    {
        await RunRecognitionAsync(lines =>
        {
            var annotations = AutoRedactor.RedactAllText(lines, _editor.SnapRegion, RedactionStyle());
            if (annotations.Count == 0)
            {
                // macshot's own wording for the same answer, so it arrives translated
                // rather than as one more English string in this port's file.
                Hint(L("(No text detected in the selected area)"));
                return;
            }

            _editor.Document.AddRange(annotations);
            RenderAnnotations();
            Hint(L("Redacted {0} • Ctrl+Z to undo • Enter to finish", annotations.Count));
        });
    }

    /// <summary>
    /// How an automatic redaction is drawn: the censor settings the user chose when that
    /// tool is in hand, and opaque black otherwise.
    /// </summary>
    /// <remarks>
    /// macshot's rule (<c>OverlayView+Popovers.swift:486-487</c>). The options row's two
    /// buttons can only be pressed with the censor tool in hand, so pressing them means the
    /// mode picked beside them; the action strip's button can be pressed holding anything,
    /// and inheriting a translucent marker colour there would produce boxes that still show
    /// what they were placed over.
    /// </remarks>
    private AnnotationStyle RedactionStyle() =>
        _editor.Tool == AnnotationTool.Censor ? _editor.Style : AutoRedactor.DefaultStyle;

    /// <summary>
    /// Covers every face found in the region.
    /// </summary>
    /// <remarks>
    /// The one automatic redaction that does not go through the text engine: it reads the
    /// pixels of the region rather than the words in it. macshot's Faces button
    /// (<c>AutoRedactor.swift:126-169</c>).
    /// </remarks>
    private async Task RedactFacesAsync()
    {
        var region = _editor.SnapRegion;
        if (region.IsEmpty)
        {
            return;
        }

        var frame = PixelsFor(region);
        var faces = await FaceFinder.FindAsync(frame);

        if (faces.Count == 0)
        {
            // Named rather than borrowed from the text pass: "no text" on a photograph of
            // three people would send the user looking for a fault that is not there.
            Hint(L("No faces detected in the selected area"));
            return;
        }

        AddRedactions(faces.Select(face => new CaptureRegion(
            region.X + face.X,
            region.Y + face.Y,
            face.Width,
            face.Height)));
    }

    /// <summary>
    /// Covers the people found in the region, and not only their faces.
    /// </summary>
    /// <remarks>
    /// Windows has no human-rectangles pass to answer this with, so it goes through the
    /// same subject model Remove Background uses and covers what that lifts. Two
    /// consequences, both stated to the user rather than hidden: it needs a Copilot+ PC,
    /// and it comes back with one box round everything it lifted rather than one per
    /// person. For a redaction the second errs the safe way — it covers more than it was
    /// asked to, never less.
    /// </remarks>
    private async Task RedactPeopleAsync()
    {
        var region = _editor.SnapRegion;
        if (region.IsEmpty)
        {
            return;
        }

        CapturedFrame lifted;
        try
        {
            lifted = await BackgroundRemover.CutOutAsync(PixelsFor(region), _settings.Current.BackgroundRemoval);
        }
        catch (InvalidOperationException failure)
        {
            // The model's own reason, which already says whether the machine cannot run it
            // or it found nothing. Both are answers to the press rather than faults.
            Hint(failure.Message);
            return;
        }

        if (SubjectBounds.Of(lifted.BgraPixels, lifted.Width, lifted.Height) is not { } subject)
        {
            Hint(L("No people detected in the selected area"));
            return;
        }

        AddRedactions([new CaptureRegion(
            region.X + subject.X,
            region.Y + subject.Y,
            subject.Width,
            subject.Height)]);
    }

    /// <summary>
    /// Asks for a picture and hands it to the stamp tool to place — macshot's Load Image.
    /// </summary>
    /// <remarks>
    /// Run from the window rather than from the toolbar because a file dialog needs a
    /// window to belong to, and the toolbar is a control without one. A dismissed dialog
    /// leaves the previous stamp in place: cancelling the choice is not clearing it.
    /// </remarks>
    private async Task LoadStampImageAsync()
    {
        if (!IsAnnotating)
        {
            return;
        }

        try
        {
            if (await ClipboardImages.PickAsync(WinRT.Interop.WindowNative.GetWindowHandle(this)) is { } picture)
            {
                AnnotationToolbar.UseStampPicture(picture);
            }
        }
        catch (Exception exception)
        {
            // A file that will not decode is the file's fault, and the row is where the
            // press was: the capture carries on with whatever stamp it already had.
            DiagnosticLog.Write($"Could not load the stamp image: {exception}");
            Hint(exception.Message);
        }
    }

    /// <summary>
    /// Puts one redaction over each of <paramref name="boxes"/>, as one undo step.
    /// </summary>
    /// <remarks>
    /// One AddRange rather than a loop, for the reason the text passes use one: the user
    /// pressed a button once, so Ctrl+Z should take the whole run back rather than
    /// uncovering the faces one at a time.
    /// </remarks>
    private void AddRedactions(IEnumerable<CaptureRegion> boxes)
    {
        var style = RedactionStyle();
        var covered = boxes
            .Select(box => Annotation.Create(
                AnnotationTool.Censor,
                new CapturePoint(box.X, box.Y),
                new CapturePoint(box.Right, box.Bottom),
                style))
            .ToList();

        if (covered.Count == 0)
        {
            return;
        }

        _editor.Document.AddRange(covered);
        RenderAnnotations();
        Hint(L("Redacted {0} • Ctrl+Z to undo • Enter to finish", covered.Count));
    }

    /// <summary>
    /// Lifts the subject out of the region and delivers it with a transparent background.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A finishing move rather than an edit, which is what macshot's overlay does with it
    /// (<c>OverlayWindowController.swift:842</c>): the cut-out is delivered the way any
    /// other finished capture is, so the preferences decide whether it is copied, saved
    /// or shown, and the overlay comes down behind it.
    /// </para>
    /// <para>
    /// No editable pair goes with it, for the reason <see cref="Completed"/> already
    /// withholds one from a framed capture: the marks and the original pixels would
    /// reopen as the picture with its background back, which is not the picture that was
    /// approved.
    /// </para>
    /// </remarks>
    private async Task RemoveBackgroundAsync()
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

        Hint(L("Removing background..."));

        try
        {
            var cut = await BackgroundRemover.CutOutAsync(finished, _settings.Current.BackgroundRemoval);

            if (_selection is { } taken)
            {
                RememberSelection(taken);
            }

            CaptureCompleted?.Invoke(
                this,
                new CaptureCompletion(cut, CaptureOutcome.Deliver, null, _capturedWindowTitle));
        }
        catch (Exception exception)
        {
            // In the pill, and the overlay stays up. The region is still selected and
            // still annotated, so the answer to "there is no subject here" is to drag a
            // different one — which needs the capture not to have ended. The old hint is
            // not put back over it for the same reason: it would erase the answer.
            DiagnosticLog.Write($"Background removal failed: {exception}");
            Hint(exception.Message);
        }
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
            // The saved settings with one field replaced, rather than a second argument
            // threaded through the placement: the target language is the only thing a
            // caller may override, and a copy of the record says so without giving
            // everything else two ways in. A code nobody recognises is ignored, which
            // leaves the language the user chose rather than reaching past it to English.
            var settings = TranslationLanguages.IsKnown(TranslateTarget)
                ? _settings.Current with { TranslateTargetLanguage = TranslateTarget! }
                : _settings.Current;

            Hint(await TranslationPlacement.RunAsync(AnnotationCanvas, settings, CancellationToken.None));
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
        Hint(L("Took {0}", sampled.ToHex()));

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

    /// <summary>
    /// Whether this press is the second of a double-click, by Windows' own reckoning of
    /// how quick and how still that has to be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Counted here rather than taken from WinUI's <c>DoubleTapped</c>, which arrives
    /// only after the second click has been released — by which time that click has
    /// already been handed to a tool — and which the pointer capture this canvas takes
    /// on every press can withhold altogether. A gesture that quietly stops working is
    /// worse than one written out.
    /// </para>
    /// <para>
    /// <c>GetDoubleClickTime</c> rather than a number: how fast a double-click is, is
    /// something the user has already told Windows, and macshot's own gesture answers to
    /// the same setting on its platform.
    /// </para>
    /// </remarks>
    private bool IsDoubleClick(PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(OverlayRoot).Position;
        var now = Environment.TickCount64;
        var quick = now - _lastPressAt <= GetDoubleClickTime()
            && Math.Abs(position.X - _lastPressPoint.X) <= DoubleClickSlop
            && Math.Abs(position.Y - _lastPressPoint.Y) <= DoubleClickSlop;

        // A double-click ends the count rather than feeding it: three clicks are one
        // double-click and a click, not two overlapping double-clicks.
        _lastPressAt = quick ? 0 : now;
        _lastPressPoint = position;
        return quick;
    }

    /// <summary>
    /// Finishes the capture on a double-click inside the region, the same as Enter does.
    /// False when the gesture means something else, and then the press goes on to be
    /// whatever it would have been.
    /// </summary>
    /// <remarks>
    /// <para>
    /// macshot's <c>doubleClickToCopy</c>, on by default. "Copy" is what it usually
    /// amounts to, but what it does is confirm: the capture goes wherever the Enter /
    /// Quick Capture setting sends it, so the gesture and the key can never disagree.
    /// </para>
    /// <para>
    /// Nothing has to be undone first, which is the one place this is simpler than
    /// macshot. A press that travels less than
    /// <see cref="AnnotationEditor.MinimumDragDistance"/> leaves no mark here, so
    /// neither click can litter the capture with the invisible shapes macshot has to
    /// rewind off its undo stack.
    /// </para>
    /// <para>
    /// A double-click on a piece of text means edit that text, never copy — macshot
    /// issue #287. Outside the region it means nothing at all: there is no region under
    /// the pointer to be done with.
    /// </para>
    /// </remarks>
    private bool ConfirmOnDoubleClick(CapturePoint point)
    {
        if (!_settings.Current.DoubleClickToCopy || _selection is not { } region)
        {
            return false;
        }

        if (!region.Contains(point.X, point.Y))
        {
            return false;
        }

        if (_editor.Document.HitTest(point) is { Tool: AnnotationTool.Text })
        {
            return false;
        }

        _ = CompleteAsync();
        return true;
    }

    /// <summary>
    /// Puts the ruler the held key is asking for on the canvas, and keeps it there while
    /// the key is down.
    /// </summary>
    /// <remarks>
    /// The offer follows the pointer, so it is recomputed from <see cref="_pointerAt"/> on
    /// every move as well as on the press that starts it — which is what makes it usable:
    /// the run under the pointer is what the user is looking for, and they find it by
    /// moving the pointer over the thing they want measured rather than by aiming at its
    /// edges. macshot does the same (<c>OverlayView.swift:1130-1133</c>).
    /// </remarks>
    private void OfferAutoSpan(bool vertical)
    {
        _autoSpanVertical = vertical;
        UpdateAutoSpan();
    }

    /// <summary>Recomputes the offered ruler, or takes it back where there is nothing to offer.</summary>
    private void UpdateAutoSpan()
    {
        if (_autoSpanVertical is not { } vertical || _pointerAt is not { } pointer)
        {
            return;
        }

        // Where the desktop capture's first pixel sits in frame space, which is the space
        // the pointer and the region are both measured in. CapturedFrame's own origin is
        // virtual and goes negative the moment a display sits left of or above the
        // primary, so using it here scanned the wrong column and reported the run at the
        // wrong place. See BuildBoundaryIndex, which had the same confusion.
        var origin = _layout.VirtualToFrame(
            new CapturePoint(_desktopFrame.VirtualX, _desktopFrame.VirtualY));

        // The desktop's own pixels rather than the preview's: the preview is the region
        // only, and the run being measured commonly reaches past the edge of it — which is
        // the reading the user wants when they are measuring a margin they are about to
        // crop to.
        var run = AutoMeasure.Run(
            _desktopFrame.BgraPixels,
            _desktopFrame.Width,
            _desktopFrame.Height,
            (int)Math.Round(pointer.X - origin.X),
            (int)Math.Round(pointer.Y - origin.Y),
            vertical);

        if (run is not { } span)
        {
            // The pointer is off the captured desktop, which happens between two monitors
            // of different heights. Nothing to measure, and nothing to leave showing.
            if (_editor.ClearSpan())
            {
                RenderAnnotations();
            }

            return;
        }

        var along = vertical ? origin.Y : origin.X;
        double from = along + span.Start;
        double to = along + span.End;

        // The scan reads the whole desktop, so the switch beside it has to be honoured
        // here rather than by the editor's own clamp — and only when the pointer is inside
        // the region, so a run being measured outside it is still reported whole.
        // macshot's rule (OverlayView.swift:4022-4026).
        var region = _editor.SnapRegion;
        if (_editor.ClampRulerToRegion && !region.IsEmpty && region.Contains(pointer.X, pointer.Y))
        {
            var near = vertical ? region.Y : region.X;
            var far = vertical ? region.Bottom : region.Right;
            from = Math.Clamp(from, near, far);
            to = Math.Clamp(to, near, far);
        }

        _editor.ProposeSpan(
            vertical ? new CapturePoint(pointer.X, from) : new CapturePoint(from, pointer.Y),
            vertical ? new CapturePoint(pointer.X, to) : new CapturePoint(to, pointer.Y));

        RenderAnnotations();
    }

    /// <summary>
    /// Takes the offered ruler, and immediately offers the next one.
    /// </summary>
    /// <returns>Whether there was an offer, and so whether this click meant this.</returns>
    /// <remarks>
    /// A click rather than the key release commits it, so several runs can be measured
    /// without letting go — which is the case this is for: the reason to measure one gap
    /// on a screenshot is usually to compare it with the gap below it. macshot commits on
    /// the same click (<c>OverlayView.swift:5458-5468</c>).
    /// </remarks>
    private bool TakeAutoSpan()
    {
        if (_editor.CommitSpan() is not { } taken)
        {
            return false;
        }

        // The same call the end of a drag makes, and for the same reason: a ruler carries
        // no reading until something renders one onto it, and that is where it happens.
        AnnotationCanvas.FinishedGesture(taken);
        UpdateAutoSpan();
        RenderAnnotations();
        return true;
    }

    private void OverlayRoot_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (_autoSpanVertical is null
            || e.Key is not (VirtualKey.Number1 or VirtualKey.NumberPad1
                or VirtualKey.Number2 or VirtualKey.NumberPad2))
        {
            return;
        }

        e.Handled = true;
        _autoSpanVertical = null;
        if (_editor.ClearSpan())
        {
            RenderAnnotations();
        }
    }

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

    /// <summary>
    /// Alt is macOS's Option: held while the region is being chosen it turns boundary snap
    /// off for the drag (see <see cref="Boundaries"/>), and held while a mark is being
    /// drawn it draws through whatever is under the pointer instead of grabbing it. The
    /// two never overlap — one is the selection phase and the other the annotation phase.
    /// Ctrl is macOS's Control: a press on a line, arrow or ruler bends it through another
    /// anchor.
    /// </summary>
    /// <remarks>
    /// Alt is read from the keyboard rather than from the pointer event, the way
    /// <see cref="Boundaries"/> already reads it: Windows treats Alt as a menu key and
    /// does not reliably carry it in a pointer event's modifiers. Shift and Ctrl are not
    /// menu keys and do arrive on the event, so they are read from it.
    /// <c>EditorWindow.ToModifiers</c> is the same mapping over the same editor and has to
    /// stay in step with this one.
    /// </remarks>
    private static EditorModifiers ToModifiers(PointerRoutedEventArgs e) =>
        (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) ? EditorModifiers.Constrain : EditorModifiers.None)
        | (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control) ? EditorModifiers.AddAnchor : EditorModifiers.None)
        | (IsDown(VirtualKey.Menu) ? EditorModifiers.DrawThrough : EditorModifiers.None);

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

        // The whole wash, not a paler one: macshot's disableSelectionOutsideShadow leaves
        // the screenshot exactly as the screen looked. Set here rather than once at
        // startup so an overlay opened while the settings window is up follows it too.
        // The rectangles behind it are kept in step regardless — the layer is collapsed,
        // so it costs four numbers, and turning the setting back on then shows the wash
        // in the right place instead of wherever the selection was when it went off.
        DimLayer.Visibility = _settings.Current.DisableSelectionShadow
            ? Visibility.Collapsed
            : Visibility.Visible;

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
    /// <summary>
    /// Draws the rubber band between the press and the pointer, and answers the region it
    /// covers in frame space — held to any shape that was asked for, and otherwise with
    /// the corner under the pointer pulled onto any line in the picture it is near.
    /// </summary>
    /// <remarks>
    /// Returns what it drew rather than being told: the snap happens in the capture's own
    /// pixels, so the region the release takes has to be the one this worked out, or
    /// letting go would move the selection off the line it was showing.
    /// </remarks>
    private CaptureRegion DrawMarquee(Point start, Point end)
    {
        var anchor = _layout.PointerToFrame(_monitor, start.X, start.Y);
        var pointer = _layout.PointerToFrame(_monitor, end.X, end.Y);

        // An exact size was chosen, so this is not a drag: the box is already the size it
        // will be and the pointer only says where. Ahead of the ratio and the snap because
        // neither has anything left to decide, which is where macshot returns from too
        // (OverlayView.swift:6687-6692).
        if (_preSelection.IsExact)
        {
            return ShowMarquee(MarqueeShaping.FixedRegion(
                pointer, _preSelection.Width, _preSelection.Height, MonitorBounds));
        }

        var square = IsDown(VirtualKey.Shift);
        var moving = MarqueeShaping.Corner(anchor, pointer, _lockedAspect, square);

        // A drag that has been given a shape is not then nudged off it: the snap would
        // take back exactly the ratio or the square that was asked for, by a pixel or two,
        // with nothing on screen to say why. macOS gates it the same way
        // (OverlayView.swift:6699-6700).
        var held = square || _lockedAspect > 0;
        var snap = BoundarySnapping.Corner(
            anchor,
            moving,
            held ? null : Boundaries,
            BoundaryRadius);

        ShowBoundaryGuides(snap);
        return ShowMarquee(snap.Region);
    }

    /// <summary>
    /// Draws the rubber band over a frame-space region and hands the same region back, so
    /// the release takes exactly what was on screen.
    /// </summary>
    private CaptureRegion ShowMarquee(CaptureRegion region)
    {
        var drawn = ToLayout(region);
        UpdateDim(drawn);
        Canvas.SetLeft(SelectionRectangle, drawn.X);
        Canvas.SetTop(SelectionRectangle, drawn.Y);
        SelectionRectangle.Width = drawn.Width;
        SelectionRectangle.Height = drawn.Height;
        SelectionRectangle.Visibility = Visibility.Visible;
        PlaceHint();
        return region;
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

            // The button lives in the pill and goes with it. macshot hides the two together
            // for the same reason (OverlayView.swift:1648-1663): the pill is what says what
            // the overlay is for, and a lone control in the middle of a dimmed screen —
            // which is what "hide capture instructions" would leave — explains nothing.
            PlacePreSelectionButton(shown: false);
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

        _snapState.Text = snapOn ? L("ON") : L("OFF");
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
    /// chosen, and under the region once something is.
    /// </summary>
    /// <remarks>
    /// Both placements are macOS's, and so is each one's shape — the middle of an empty
    /// screen can carry a larger pill than a line sitting against a rectangle the user is
    /// dragging. The size it comes to is measured rather than worked out from constants,
    /// unlike the toolbar and the size box: what a sentence measures is the whole variable
    /// here, and there is no arithmetic that answers it. Where it then goes is arithmetic,
    /// and lives in <see cref="HintPlacement"/> with the rest of the overlay's placement.
    /// </remarks>
    private void PlaceHint()
    {
        if (HintPill.Visibility != Visibility.Visible)
        {
            return;
        }

        var screen = LayoutBounds;
        var anchor = HintAnchor();

        // The button stands in the pill, so the pill is grown to carry it before it is
        // measured — macshot adds the same block to its own background rectangle
        // (OverlayView.swift:2236-2239). It is only ever offered while the pill is in the
        // middle of the screen, because both conditions for it are conditions for that.
        var carries = ShowsPreSelectionButton;
        var padding = PreSelectionButtonPlacement.Padding;

        HintPill.Padding = anchor is null
            ? new Thickness(
                padding,
                padding,
                padding,
                carries ? padding + PreSelectionButtonPlacement.Reserved(PreSelectionButtonPlacement.Height) : padding)
            : new Thickness(10, 5, 10, 5);

        // The instruction is wider than the button in every language macshot ships, so this
        // only binds on a pill carrying something short — where without it the button would
        // be wider than the slab it is drawn on.
        HintPill.MinWidth = carries
            ? PreSelectionButtonPlacement.LeastWidth(PreSelectionButtonPlacement.Width)
            : 0;

        HintPill.CornerRadius = new CornerRadius(anchor is null ? 8 : 6);
        HintPill.Measure(new Size(screen.Width, screen.Height));
        var size = HintPill.DesiredSize;

        if (anchor is { } region)
        {
            // Everything the overlay has already placed, so the pill is the one that gives
            // way. It is the only piece of chrome here that can move without taking a
            // meaning with it: the size box says what the region measures and the strips
            // say what the tools are, and neither reads the same somewhere else.
            var placed = new List<CaptureRegion>(4) { _sizeBoxBounds };
            placed.AddRange(AnnotationToolbar.Occupies);

            var pill = HintPlacement.For(
                region, screen, new CaptureRegion(0, 0, size.Width, size.Height), placed);

            Canvas.SetLeft(HintPill, pill.X);
            Canvas.SetTop(HintPill, pill.Y);
        }
        else
        {
            Canvas.SetLeft(HintPill, (screen.Width - size.Width) / 2);
            Canvas.SetTop(HintPill, (screen.Height - size.Height) / 2);
        }

        PlacePreSelectionButton(carries);
    }

    /// <summary>
    /// Whether the button that shapes the next drag belongs on screen.
    /// </summary>
    /// <remarks>
    /// While nothing has been chosen and nothing is being dragged out, which is macshot's
    /// idle state (<c>OverlayView.swift:2734-2742</c>). Not while text is being read out of
    /// the capture: a region picked for its words has no shape worth choosing, and macshot
    /// hides it under <c>autoOCRMode</c> for that reason.
    /// </remarks>
    private bool ShowsPreSelectionButton =>
        !IsAnnotating && _selectionStart is null && Intent is not CaptureIntent.Recognize;

    /// <summary>
    /// Puts the button in the strip the pill has left for it along its bottom edge, or
    /// takes it away.
    /// </summary>
    private void PlacePreSelectionButton(bool shown)
    {
        if (!shown)
        {
            _preSelectionButton.Visibility = Visibility.Collapsed;
            return;
        }

        var pill = new CaptureRegion(
            Canvas.GetLeft(HintPill),
            Canvas.GetTop(HintPill),
            HintPill.DesiredSize.Width,
            HintPill.DesiredSize.Height);

        var where = PreSelectionButtonPlacement.For(
            pill,
            new CaptureRegion(
                0, 0, PreSelectionButtonPlacement.Width, PreSelectionButtonPlacement.Height));

        Canvas.SetLeft(_preSelectionButton, where.X);
        Canvas.SetTop(_preSelectionButton, where.Y);
        _preSelectionButton.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// What the pill is about, in the units it is placed in, or null when that is the
    /// whole screen because nothing has been chosen or dragged out yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Through the viewport, like the rest of the chrome: the pill is outside the zoom
    /// transform, so where it goes is where the region appears on screen rather than where
    /// it is on the capture.
    /// </para>
    /// <para>
    /// The frame's edge rather than the region's, the way the toolbar takes it. What the
    /// pill says is about the tool that is running and not about the pixels, so it belongs
    /// outside the gradient — the opposite of the size box, which is the reading of those
    /// pixels and stays tight against them.
    /// </para>
    /// </remarks>
    private CaptureRegion? HintAnchor()
    {
        if (_selection is { } chosen)
        {
            return _viewport.ToView(ToLayout(ChromeAnchor(chosen)));
        }

        return _selectionStart is { } start && _marqueeAt is { } now
            ? _viewport.ToView(CaptureRegion.FromPoints(start.X, start.Y, now.X, now.Y))
            : null;
    }

    /// <summary>How long Windows gives a user to complete a double-click.</summary>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
}
