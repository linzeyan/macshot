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
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using static Macshot.Windows.Services.Localization;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Point resolves to
// Macshot.Point and does not compile.
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;

namespace Macshot.Windows;

/// <summary>
/// The standalone editor: one image, the annotation tools, and the operations that
/// change the pixels rather than draw on them.
/// </summary>
/// <remarks>
/// <para>
/// The capture overlay cannot be this. It is a display-sized always-on-top window with
/// no zoom, so a scroll capture taller than the screen has nowhere to be marked up, and
/// cropping or framing a capture there would mean doing it to the screen. This window is
/// where a capture goes once it exists: reopened from the thumbnail, from the recent
/// list, or handed over from the overlay.
/// </para>
/// <para>
/// It draws with the same two controls the overlay does, so the tools behave identically
/// in both. What it adds is the scroll viewer around them and the image operations —
/// crop, flip, frame — which are the reason a separate window is worth having.
/// </para>
/// </remarks>
public sealed partial class EditorWindow : Window
{
    /// <summary>
    /// The two standing hints, looked up rather than held.
    /// </summary>
    /// <remarks>
    /// Properties and not constants: a constant is folded at compile time and would ship
    /// the English into every build, so the window would keep saying it in a session that
    /// had chosen another language. The same reason the overlay's hints are properties.
    /// </remarks>
    private static string StandingHint => L("Draw to annotate • Ctrl+Z undo • Ctrl+S save • Ctrl+C copy");

    private static string CropHint => L("Drag the part to keep • Esc to stop cropping");

    /// <summary>
    /// How much of the work area the window may take when the image is larger. Short of
    /// the whole thing on purpose: a window that opens exactly screen-sized looks like a
    /// full-screen app that has lost its title bar.
    /// </summary>
    private const double MaxWorkAreaShare = 0.9;

    private readonly SettingsStore _settings;
    private readonly AnnotationEditor _editor = new(new AnnotationDocument());

    /// <summary>
    /// The clock behind the pencil's hold-to-select. Built in the constructor rather than
    /// here, because it needs this window's dispatcher.
    /// </summary>
    private readonly PressHold _hold;

    private readonly IFramePlacement _placement = new ImageFramePlacement();

    /// <summary>
    /// What each image operation replaced, and the marks that were live when it ran.
    /// </summary>
    /// <remarks>
    /// An image operation burns the marks into the pixels, so the document's own history
    /// stops describing anything real and is reset. This is what undo needs instead: the
    /// image as it was, and the annotations as objects again. The two timelines never
    /// interleave confusingly because an operation empties one of them.
    /// The frame is not one of these. It is a layer the delivered pixels are taken through,
    /// so it is taken back off by turning it off rather than by undoing it.
    /// </remarks>
    private readonly Stack<(CapturedFrame Frame, Annotation[] Annotations)> _imageUndo = new();

    private CapturedFrame _frame;

    /// <summary>
    /// The marks this window was asked to open with, until it has opened. Applied in
    /// <see cref="ShowAsync"/> rather than in the constructor, because a document reset
    /// before there is a canvas to draw on has nothing to show for itself.
    /// </summary>
    private readonly IReadOnlyList<Annotation>? _opensWith;

    /// <summary>
    /// What the Adjust popover is asking for. A layer over the image rather than
    /// something burnt into it, because the sliders are dragged: an adjustment applied on
    /// every tick would leave an undo stack thirty entries deep for one decision. The
    /// delivered pixels come from the preview, so what is on show is what is handed over.
    /// </summary>
    private ImageEffectsOptions _effects = ImageEffectsOptions.Default;

    /// <summary>
    /// The frame the capture is being seen inside, if any.
    /// </summary>
    /// <remarks>
    /// A layer for the same reason the adjustment is one, and macshot's own answer: its
    /// editor is the overlay's view in a scroll viewer, and beautify there is a switch read
    /// at delivery (<c>DetachedEditorWindowController.swift:385-392</c>) rather than
    /// something done to the pixels. This window used to burn it in as an image operation,
    /// which made a framed capture the one kind that could not be archived in a form it
    /// could be reopened from — the background is not one of the marks, so the marks and
    /// the pixels alone would have reopened as a different picture.
    /// </remarks>
    private BeautifyState _beautify = BeautifyState.Default;

    /// <summary>
    /// The picture behind the frame, decoded from what <see cref="_beautify"/> carries.
    /// </summary>
    /// <remarks>
    /// The capture's own copy rather than whichever picture the setting now names: there is
    /// one custom background on the machine, and a capture archived on last month's would
    /// otherwise reopen on this month's.
    /// </remarks>
    private BeautifyBackdrop? _backdrop;

    /// <summary>
    /// What the backdrop now on screen was drawn from, so it is drawn again only when it
    /// would come out different.
    /// </summary>
    /// <remarks>
    /// The adjust sliders repaint the whole window on every tick of a drag, and the frame
    /// is not what they change: without this, dragging brightness over a framed 4K capture
    /// would scan a frame-sized gradient sixty times a second to arrive at the same picture
    /// each time. The same reason <see cref="BeautifyBackdrop"/> caches its blur.
    /// </remarks>
    private (int Width, int Height, BeautifyOptions Options, double Scale)? _backdropShown;

    private ToggleButton? _cropButton;

    /// <summary>
    /// Built in <see cref="BuildActions"/>, which runs before anything can read them.
    /// Null-forgiving rather than nullable so every use site is not a question about
    /// whether the bar exists yet.
    /// </summary>
    private TextBlock _sizeLabel = null!;

    private Button _zoomButton = null!;

    /// <summary>
    /// Writes the marks back over the capture they belong to. Built with the rest of the
    /// bar and shown only once there is something to commit.
    /// </summary>
    private Button _doneButton = null!;

    /// <summary>
    /// What this capture looked like when it was last written down: at open, and again
    /// after every delivery and every commit.
    /// </summary>
    /// <remarks>
    /// macshot's clean baseline (<c>DetachedEditorWindowController.swift:241-247</c>).
    /// Taken in <see cref="ShowAsync"/> rather than in the constructor, because a capture
    /// reopened with its marks has to be clean with those marks on it — baselined before
    /// they were applied, every reopened capture would offer Done for edits it arrived
    /// with. Given a value here as well, because the default of a struct leaves its
    /// <see cref="ImageEffectsOptions"/> null — harmless to compare against, but not
    /// something to leave lying about in a field.
    /// </remarks>
    private EditorState _saved = new(0, 0, ImageEffectsOptions.Default, BeautifyState.Default);

    private bool _cropping;
    private Point? _cropStart;
    private bool _zoomFitted;

    /// <summary>What one step of the zoom menu multiplies by, as macshot's does.</summary>
    private const double ZoomStep = 1.25;

    /// <summary>
    /// The =/+ and -/_ keys of the main row, which <see cref="VirtualKey"/> has no names
    /// for: the enum stops at the numbers and the numpad, and the OEM range is where a
    /// keyboard's punctuation lives. 187 and 189 are the two macshot binds zoom to.
    /// </summary>
    private const VirtualKey PlusKey = (VirtualKey)187;

    /// <inheritdoc cref="PlusKey"/>
    private const VirtualKey MinusKey = (VirtualKey)189;

    /// <summary>macshot's top bar: 22-tall buttons in a 32-tall bar, 4 apart.</summary>
    private const double BarButtonHeight = 22;

    /// <summary>
    /// What a text button gets instead of macshot's fixed 24 of width. macshot's are
    /// symbols and a symbol is as wide as it is; these are words, and a word is as wide
    /// as it is.
    /// </summary>
    private const double BarButtonPadding = 8;

    private const double BarFontSize = 11;
    private const double BarGap = 4;
    private const double BarGroupGap = 12;

    /// <param name="annotations">
    /// Marks to open with, for a capture reopened from the history with its marks
    /// archived beside it. They are the capture's own marks as objects again, so they can
    /// be moved, restyled and undone rather than only drawn over.
    /// </param>
    /// <param name="state">
    /// The adjustment and the frame the capture was archived carrying. Both open as the
    /// layers they were rather than as pixels, which is what lets the user take them back
    /// off — the point of keeping them as numbers at all.
    /// </param>
    public EditorWindow(
        CapturedFrame frame,
        SettingsStore settings,
        IReadOnlyList<Annotation>? annotations = null,
        CaptureEditState? state = null)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _opensWith = annotations;

        var opening = state ?? CaptureEditState.None;
        _effects = opening.Effects;
        _beautify = opening.Beautify;
        InitializeComponent();
        // Every string in the XAML is already the English text macshot keys by,
        // so the page is translated in place rather than written twice.
        this.Localize();

        // After the markup has run: the canvas it redraws is a field the markup creates.
        _hold = new PressHold(DispatcherQueue, _editor, AnnotationCanvas.Render);
        AppThemes.Apply(this, settings.Current.Theme);
        this.GetAppWindow().UseAppIcon();
    }

    /// <summary>Raised with the image the user wants kept on top of everything else.</summary>
    public event EventHandler<CapturedFrame>? PinRequested;

#if !OFFLINE
    /// <summary>Raised with the finished canvas when the Upload button is pressed.</summary>
    public event EventHandler<CapturedFrame>? UploadRequested;
#endif

    /// <summary>
    /// Raised when the user wants another capture added under this one. The owner takes
    /// it, because taking a capture is its job and not this window's, and hands it back
    /// through <see cref="AddCapture"/>.
    /// </summary>
    public event EventHandler? AddCaptureRequested;

    /// <summary>
    /// Raised with the finished image when the user is done with it — Enter to deliver it
    /// again, Done to write the marks back over the capture they belong to. The owner
    /// decides what each means, exactly as it does for a capture, so this window needs no
    /// opinion about clipboards or folders.
    /// </summary>
    /// <remarks>
    /// A completion rather than the image alone, so that what the editor hands over
    /// carries the marks beside the pixels the same way a capture from the overlay does.
    /// Without it a capture edited here would archive as flat pixels, and reopening it a
    /// second time would find nothing left to edit.
    /// </remarks>
    public event EventHandler<CaptureCompletion>? Finished;

    public async Task ShowAsync()
    {
        // Before anything is drawn, because the first Present is what puts the frame on
        // screen and a background that arrived after it would show as the first gradient
        // until something else changed.
        if (_beautify.Background is { Length: > 0 } picture)
        {
            _backdrop = await BeautifyBackgroundStore.DecodeAsync(picture);
        }

        WireToolbar();
        WireCanvas();
        BuildActions();
        Present();

        // After the canvas exists, and as a reset rather than an add: the marks are the
        // ones this capture already had, so undoing straight after opening should not
        // take them off something that was archived with them on.
        if (_opensWith is { Count: > 0 } restored)
        {
            _editor.Document.Reset(restored);
            AnnotationCanvas.Render();
        }

        // After the marks it opens with, so a reopened capture is clean with them on.
        Rebaseline();
        _editor.Document.Changed += (_, _) => RefreshDone();

        var appWindow = this.GetAppWindow();
        appWindow.MoveAndResize(PlaceOverImage());

        // The question macshot asks in windowShouldClose, in the shape Windows asks it.
        // Without it the X is a way to lose every mark on a capture without being told,
        // which is the one thing an editor must not be.
        appWindow.Closing += (_, args) => OfferToKeepEdits(args);
        HintText.Text = StandingHint;
        Activate();
        EditorRoot.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Shows the current image at its own pixel size, which is what makes one layout unit
    /// one pixel and the placement between marks and pixels the identity.
    /// </summary>
    private void Present()
    {
        Title = $"macshot — {_frame.Width} × {_frame.Height}";
        var shown = _effects.IsIdentity
            ? _frame
            : new CapturedFrame(
                _frame.VirtualX,
                _frame.VirtualY,
                _frame.Width,
                _frame.Height,
                ImageEffects.Apply(_frame.Width, _frame.Height, _frame.BgraPixels, _effects));

        // Before the canvas is presented onto it: the surface it draws on is the one this
        // sizes and insets, and presenting into a surface still the previous size would
        // stretch the capture until the next layout pass.
        ShowFrame();

        AnnotationCanvas.Present(shown, new CaptureRegion(0, 0, shown.Width, shown.Height), _placement);
    }

    /// <summary>
    /// How this capture is framed: what it is carrying, with the picture it named.
    /// </summary>
    private BeautifyOptions FrameOptions => _beautify.ToOptions(_backdrop);

    /// <summary>
    /// The size of what is on screen: the capture, and the frame around it when it has one.
    /// </summary>
    /// <remarks>
    /// The same arithmetic <see cref="BeautifyRenderer.Backdrop"/> sizes itself by, rather
    /// than <see cref="ImageHost"/>'s laid-out width — both callers can run before the first
    /// layout pass has given the host one, and the <c>NaN</c> there opens a framed capture
    /// at 1:1 in a window too small for it, which is what the frame used to be baked into
    /// the capture to avoid.
    /// </remarks>
    private (int Width, int Height) PresentedSize
    {
        get
        {
            if (!_beautify.Enabled)
            {
                return (_frame.Width, _frame.Height);
            }

            var framed = BeautifyRenderer.FrameAround(
                new CaptureRegion(0, 0, _frame.Width, _frame.Height),
                FrameOptions,
                _beautify.Scale);

            return ((int)framed.Width, (int)framed.Height);
        }
    }

    /// <summary>
    /// Puts the frame around the image, or takes it away, and leaves the drawing surface
    /// inset by however much of it there is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What is drawn is <see cref="BeautifyRenderer.Backdrop"/> — the background and the
    /// shadow the file will have, with the capture's own area left clear — behind the image
    /// where it already is. Composited that is the same picture
    /// <see cref="BeautifyRenderer.Render"/> makes at delivery, which is the point: the
    /// preview is not a second set of numbers but the ones the export uses. The capture
    /// overlay shows it the same way and for the same reason.
    /// </para>
    /// <para>
    /// The image does not move within its own surface. Growing the host and insetting the
    /// surface leaves a mark's coordinates the pixels it was drawn on, whether or not there
    /// is a frame — so arming one cannot shift the marks, and neither can taking it off.
    /// It also puts the background outside the input canvas, which is what stops a mark
    /// being drawn on ground that is not part of the capture it would be archived against.
    /// </para>
    /// </remarks>
    private void ShowFrame()
    {
        ImageSurface.Width = _frame.Width;
        ImageSurface.Height = _frame.Height;

        if (!_beautify.Enabled)
        {
            ImageSurface.Margin = new Thickness(0);
            ImageHost.Width = _frame.Width;
            ImageHost.Height = _frame.Height;
            FrameBackdrop.Visibility = Visibility.Collapsed;

            // Dropped rather than kept for next time: it is the size of a capture, and the
            // one on screen is the only one worth holding.
            FrameBackdrop.Source = null;
            _backdropShown = null;
            return;
        }

        var options = FrameOptions;
        var wanted = (
            Width: _frame.Width,
            Height: _frame.Height,
            Options: options,
            Scale: _beautify.Scale);

        if (_backdropShown is not { } shown || shown != wanted)
        {
            var (width, height, pixels) = BeautifyRenderer.Backdrop(
                _frame.Width,
                _frame.Height,
                options,
                _beautify.Scale);

            var bitmap = new WriteableBitmap(width, height);
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Write(pixels, 0, pixels.Length);
            }

            ImageHost.Width = width;
            ImageHost.Height = height;
            FrameBackdrop.Source = bitmap;
            _backdropShown = wanted;
        }

        // Where the capture sits inside the frame: the padding on every side, and the title
        // bar as well above it in window mode. Read from the same arithmetic the backdrop
        // was drawn with rather than recomputed, so the clear area and the image cannot
        // land a pixel apart.
        var placed = BeautifyRenderer.FrameAround(
            new CaptureRegion(0, 0, _frame.Width, _frame.Height), options, _beautify.Scale);

        ImageSurface.Margin = new Thickness(-placed.X, -placed.Y, 0, 0);
        FrameBackdrop.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The capture as it is delivered: what was drawn, framed if the user asked for it.
    /// </summary>
    /// <remarks>
    /// The frame goes on last, over the marks, so an arrow drawn to the edge of the image
    /// stays on the screenshot rather than crossing the background it is mounted on. The
    /// capture overlay finishes a capture the same way.
    /// </remarks>
    private CapturedFrame? Delivered()
    {
        if (AnnotationCanvas.ToFrame() is not { } finished)
        {
            return null;
        }

        if (!_beautify.Enabled)
        {
            return finished;
        }

        var (width, height, pixels) = BeautifyRenderer.Render(
            finished.Width,
            finished.Height,
            finished.BgraPixels,
            FrameOptions,
            _beautify.Scale);

        return new CapturedFrame(finished.VirtualX, finished.VirtualY, width, height, pixels);
    }

    private void WireToolbar()
    {
        // Before binding: it decides which actions the strip carries, and cancelling a
        // capture or moving a region are not among them here.
        AnnotationToolbar.EditorMode = true;

        // The row opens one dialog — the frame's background picture — and a common dialog
        // has to be owned by a real window rather than by the control that raised it.
        AnnotationToolbar.OwnerHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);

        AnnotationToolbar.Bind(_editor, _settings);
        AnnotationToolbar.Changed += (_, _) => AnnotationCanvas.Render();
        AnnotationToolbar.ColorSamplingToggled += (_, armed) => SetColorSampling(armed);
        AnnotationToolbar.EffectsChanged += (_, options) =>
        {
            _effects = options;
            Present();

            // By hand, because an adjustment is the one edit that leaves no undo step and
            // no image operation behind — it is a layer the delivered pixels are taken
            // through, so nothing the document raises says it happened.
            RefreshDone();
        };
        AnnotationToolbar.CommandInvoked += (_, command) => RunToolbarCommand(command);

        // Choosing a background arms the frame as well as choosing it, which is what the
        // overlay does with the same picker: leaving it off would mean choosing one,
        // watching nothing change, and then having to find the button that turns on the
        // thing already chosen.
        AnnotationToolbar.FrameStyleChosen += (_, index) => FrameWith(index);
        AnnotationToolbar.ShowToolbar(true);

        // After the strip exists, so the Adjust button is lit for a capture that was
        // archived carrying one — without it the popover would open at nought over an
        // image the editor is already showing adjusted.
        AnnotationToolbar.LoadEffects(_effects);

        // And the Beautify button for the same reason: a capture reopened inside its frame
        // has to show a lit button, or the only way to find out that the frame is on would
        // be to deliver the capture and look at the file.
        AnnotationToolbar.Beautified = _beautify.Enabled;

        // The strips sit at fixed corners of the window here rather than around a
        // selection, so what they are placed against is the window itself — and it is
        // resizable, so they are placed again every time it changes.
        EditorRoot.SizeChanged += (_, args) => AnnotationToolbar.Reposition(
            default,
            new CaptureRegion(0, 0, args.NewSize.Width, args.NewSize.Height));

        Closed += (_, _) => AnnotationToolbar.PersistStyle();
    }

    /// <summary>
    /// What the action buttons do here. Copy, save and pin leave the window open: the
    /// editor is somewhere the user works, and one that closed itself after handing over a
    /// copy would take the rest of the session's marks with it.
    /// </summary>
    private void RunToolbarCommand(ToolbarCommand command)
    {
        switch (command)
        {
            case ToolbarCommand.Undo:
                Undo();
                return;

            case ToolbarCommand.Redo:
                Redo();
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
                InvertImage();
                return;

            case ToolbarCommand.Beautify:
                ToggleFrame();
                return;

            case ToolbarCommand.RemoveBackground:
                _ = RemoveBackgroundAsync();
                return;

            case ToolbarCommand.Copy:
                _ = CopyAsync();
                return;

            case ToolbarCommand.SaveAs:
                _ = SaveAsAsync();
                return;

            case ToolbarCommand.Share:
                _ = ShareAsync();
                return;

            case ToolbarCommand.Save:
                _ = SaveAsync();
                return;

            case ToolbarCommand.Pin:
                _ = PinAsync();
                return;

            case ToolbarCommand.Upload:
#if !OFFLINE
                _ = UploadAsync();
#endif
                return;

            default:
                // Choosing a tool and choosing a colour are the toolbar's own business,
                // and the overlay's actions are not offered here at all.
                return;
        }
    }

    private void WireCanvas()
    {
        AnnotationCanvas.Bind(
            _editor,
            () => EditorRoot.XamlRoot?.RasterizationScale ?? 1,
            message => HintText.Text = message);
        AnnotationCanvas.StampEmoji = () => AnnotationToolbar.StampEmoji;
        AnnotationCanvas.StampPicture = () => AnnotationToolbar.StampPicture;
        AnnotationCanvas.NumberStartAt = () => AnnotationToolbar.NumberStartAt;
        AnnotationCanvas.SmartMarker = () => AnnotationToolbar.SmartMarker;
        AnnotationCanvas.CensorTextOnly = () => AnnotationToolbar.CensorTextOnly;
        AnnotationCanvas.TypingEnded += (_, _) =>
        {
            EditorRoot.Focus(FocusState.Programmatic);
            HintText.Text = StandingHint;
        };
    }

    /// <summary>
    /// Builds the buttons only an editor has: the operations that change the pixels
    /// themselves. Where the marked-up image goes is on the toolbar, which is the same
    /// here as it is over a capture.
    /// </summary>
    private void BuildActions()
    {
        // The reading macshot keeps at the leading edge of its top bar, in the same
        // tabular figures: it changes under crop, frame and add capture, and a size that
        // shifts its own label about as the digits change is hard to read at a glance.
        _sizeLabel = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Opacity = 0.45,
            VerticalAlignment = VerticalAlignment.Center,

            // macshot's 12 from the leading edge, and its 16 before the first button.
            Margin = new Thickness(12, 0, 16, 0),
        };
        Typography.SetNumeralAlignment(_sizeLabel, FontNumeralAlignment.Tabular);

        _cropButton = new ToggleButton { Content = L("Crop") };
        _cropButton.Click += Crop_Click;

        var flip = new Button { Content = L("Flip") };
        var flipMenu = new MenuFlyout { Placement = FlyoutPlacementMode.Bottom };
        flipMenu.Items.Add(MenuItem(L("Horizontal"), () => FlipImage(horizontal: true)));
        flipMenu.Items.Add(MenuItem(L("Vertical"), () => FlipImage(horizontal: false)));
        flip.Flyout = flipMenu;

        // A grid of the backgrounds themselves rather than 48 rows of their names, which
        // is how macshot offers them and the only way the choice can be made by eye.
        var frame = new Button { Content = L("Frame") };
        var frames = new BeautifySwatchGrid();
        var frameFlyout = new Flyout { Placement = FlyoutPlacementMode.Bottom, Content = frames };
        frames.Picked += (_, index) =>
        {
            frameFlyout.Hide();
            FrameWith(index);
        };

        // Opened rather than built each time: painting 48 gradients is not free, and the
        // only thing that changes between openings is which one is ringed. The capture's
        // own background is ringed rather than the setting's, so a reopened framed capture
        // shows the one it is wearing.
        frameFlyout.Opening += (_, _) => frames.Show(
            _beautify.Enabled ? _beautify.StyleIndex : _settings.Current.BeautifyStyleIndex);
        frame.Flyout = frameFlyout;

        // macshot's Add Capture: another capture, taken now, landing under this one. It
        // is what turns the editor from somewhere a screenshot is marked up into
        // somewhere several are put together.
        var add = new Button { Content = L("Add capture") };
        add.Click += (_, _) => AddCaptureRequested?.Invoke(this, EventArgs.Empty);

        // Every label on this bar is localized here rather than by the page-wide pass,
        // which ran in the constructor before the bar existed. Done was the only one that
        // did, so a Chinese editor read 完成 beside Crop, Flip, Frame and Add capture.
        _doneButton = new Button { Content = L("Done"), Visibility = Visibility.Collapsed };
        _doneButton.Click += (_, _) => _ = CommitAsync();

        _zoomButton = new Button { Content = "100% ▾" };
        _zoomButton.Flyout = ZoomMenu();

        // Tabular figures and the same faded 11 medium as the size reading, because the
        // percentage changes under every scroll and a proportional 8 is a different width
        // from a proportional 1.
        _zoomButton.FontSize = BarFontSize;
        _zoomButton.FontWeight = FontWeights.Medium;
        _zoomButton.Opacity = 0.45;
        Typography.SetNumeralAlignment(_zoomButton, FontNumeralAlignment.Tabular);

        ImageOperations.Children.Add(_sizeLabel);

        // macshot's gaps: 4 between the operations that belong together, 12 before the one
        // that does something else entirely.
        Seat(_cropButton, 0);
        Seat(flip, BarGap);
        Seat(frame, BarGap);
        Seat(add, BarGroupGap);

        // Before the zoom reading and 12 clear of it, where macshot puts it
        // (EditorTopBarView.swift:209-212). Louder than its neighbours on purpose: 12
        // semibold in the accent against their faded 11, because it is the one control on
        // this bar that finishes something.
        Seat(_doneButton, 0, ZoomHost);
        _doneButton.Margin = new Thickness(0, 0, BarGroupGap, 0);
        _doneButton.FontSize = 12;
        _doneButton.FontWeight = FontWeights.SemiBold;
        _doneButton.Foreground = ToolbarPalette.AccentBrush;

        Seat(_zoomButton, 0, ZoomHost);

        ShowSize();
        ShowZoom();

        void Seat(Control button, double leading, Panel? host = null)
        {
            button.Height = BarButtonHeight;
            button.MinHeight = BarButtonHeight;
            button.FontSize = BarFontSize;
            button.Padding = new Thickness(BarButtonPadding, 0, BarButtonPadding, 0);
            button.VerticalAlignment = VerticalAlignment.Center;
            button.Margin = new Thickness(leading, 0, 0, 0);
            (host ?? ImageOperations).Children.Add(button);
        }
    }

    /// <summary>
    /// macshot's zoom dropdown, with its entries in its order. The scroll view zooms by
    /// itself, but only a wheel can reach it: without this there is no way to say "100%"
    /// and no reading anywhere of what the current zoom even is.
    /// </summary>
    private MenuFlyout ZoomMenu()
    {
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.Bottom };
        menu.Items.Add(MenuItem(L("Zoom in"), () => ZoomBy(ZoomStep)));
        menu.Items.Add(MenuItem(L("Zoom out"), () => ZoomBy(1 / ZoomStep)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem(L("Fit canvas"), ZoomToFit));
        menu.Items.Add(new MenuFlyoutSeparator());

        foreach (var preset in new[] { 0.5, 1.0, 2.0 })
        {
            menu.Items.Add(MenuItem($"{preset * 100:0}%", () => ZoomTo(preset)));
        }

        return menu;
    }

    private void ZoomBy(double factor) => ZoomTo(Scroller.ZoomFactor * factor);

    private void ZoomTo(double zoom) =>
        Scroller.ChangeView(
            null,
            null,
            (float)Math.Clamp(zoom, Scroller.MinZoomFactor, Scroller.MaxZoomFactor));

    /// <remarks>
    /// Against what is on show rather than against the capture, because a frame around it
    /// is on show too: fitted to the capture alone, a framed one would open with its
    /// background cropped off at the edges of the viewport.
    /// </remarks>
    private void ZoomToFit()
    {
        if (Scroller.ViewportWidth <= 0 || Scroller.ViewportHeight <= 0)
        {
            return;
        }

        ZoomTo(Math.Min(
            Scroller.ViewportWidth / ImageHost.Width,
            Scroller.ViewportHeight / ImageHost.Height));
    }

    /// <summary>Keeps the reading honest however the zoom was changed — menu or wheel.</summary>
    private void Scroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) => ShowZoom();

    /// <remarks>
    /// Silent until the bar exists. The zoom a large capture opens at is chosen during the
    /// first layout pass, which is before the toolbar this writes into has been built — and
    /// the bar reads the zoom itself once it is up, so nothing is lost by saying nothing.
    /// </remarks>
    private void ShowZoom()
    {
        if (_zoomButton is { } button)
        {
            button.Content = $"{Scroller.ZoomFactor * 100:0}% ▾";
        }
    }

    private void ShowSize() => _sizeLabel.Text = $"{_frame.Width} × {_frame.Height}";

    /// <summary>
    /// Adds a capture below this one, growing the canvas to fit it.
    /// </summary>
    /// <remarks>
    /// Burned in rather than left as a movable mark, which is where this parts company
    /// with macshot: growing the canvas goes through the same flatten-and-replace that
    /// crop and flip do, and that mechanism has no way to keep a live annotation across
    /// the operation. It undoes in one step like every other image operation.
    /// </remarks>
    public void AddCapture(CapturedFrame added)
    {
        ArgumentNullException.ThrowIfNull(added);

        ApplyImageOperation(
            existing => new CapturedFrame(
                existing.VirtualX,
                existing.VirtualY,
                Math.Max(existing.Width, added.Width),
                existing.Height + added.Height,
                FrameTransforms.StackBelow(
                    existing.Width,
                    existing.Height,
                    existing.BgraPixels,
                    added.Width,
                    added.Height,
                    added.BgraPixels)),
            "Capture added • Ctrl+Z to undo");
    }

    private static MenuFlyoutItem MenuItem(string text, Action invoke)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => invoke();
        return item;
    }

    /// <summary>
    /// Opens over the image: its own size where that fits, and short of the work area
    /// where it does not.
    /// </summary>
    private RectInt32 PlaceOverImage()
    {
        var monitor = MonitorEnumerator.Enumerate().Layout.Primary;
        var maxWidth = (int)(monitor.WorkArea.Width * MaxWorkAreaShare);
        var maxHeight = (int)(monitor.WorkArea.Height * MaxWorkAreaShare);

        // What is presented rather than what was captured: a frame is a layer around the
        // capture now, so the two differ, and sizing to the pixels inside opened a framed
        // capture showing a corner of its own background.
        //
        // The image is in pixels and so is an AppWindow's size, so no scaling comes into
        // this. The extra height is the title bar and the toolbar, which the image would
        // otherwise open underneath.
        var presented = PresentedSize;
        var width = Math.Clamp(presented.Width + 48, 640, Math.Max(640, maxWidth));
        var height = Math.Clamp(presented.Height + 160, 480, Math.Max(480, maxHeight));

        return new RectInt32(
            (int)(monitor.WorkArea.X + ((monitor.WorkArea.Width - width) / 2)),
            (int)(monitor.WorkArea.Y + ((monitor.WorkArea.Height - height) / 2)),
            width,
            height);
    }

    /// <summary>
    /// Zooms out to fit the first time the viewport has a size, so an image larger than
    /// the window opens whole rather than showing its top-left corner.
    /// </summary>
    /// <remarks>
    /// The rule is <see cref="CaptureFit.OpeningZoom"/>'s: the width is fitted and the
    /// height is scrolled, so a scroll capture ten screens tall opens at a size its text
    /// can be read at instead of at a tenth. Only once, or resizing the window would keep
    /// overruling the zoom the user chose. "Fit canvas" in the zoom menu is still the whole
    /// image on both axes, for whoever wants it.
    /// </remarks>
    private void Scroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_zoomFitted || Scroller.ViewportWidth <= 0 || Scroller.ViewportHeight <= 0)
        {
            return;
        }

        _zoomFitted = true;
        var opening = CaptureFit.OpeningZoom(
            PresentedSize.Width,
            Scroller.ViewportWidth,
            Scroller.MinZoomFactor,
            Scroller.MaxZoomFactor);

        if (opening < 1)
        {
            Scroller.ChangeView(null, null, (float)opening);
        }
    }

    private void InputCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        InputCanvas.CapturePointer(e.Pointer);

        if (_cropping)
        {
            _cropStart = e.GetCurrentPoint(InputCanvas).Position;
            DrawCropRectangle(_cropStart.Value, _cropStart.Value);
            return;
        }

        if (AnnotationToolbar.IsSamplingColor)
        {
            TakeSampledColor(ToFrame(e));
            return;
        }

        var at = ToFrame(e);
        var modifiers = ToModifiers(e);
        var grabbed = _editor.PointerPressed(at, modifiers, PenInput.Of(e));

        // Only where the press drew rather than grabbing: a freehand tool has no click
        // left over to mean "pick this up", so holding still is what does it instead.
        if (!grabbed)
        {
            _hold.Watch(at, modifiers);
        }

        // The press comes first, the placement second: a sprite tool places its mark only
        // where the click did not land on one already drawn. See the same order in
        // CaptureOverlayWindow, and macOS's startAnnotation.
        if (!grabbed && AnnotationCanvasView.IsPlacedByClick(_editor.Tool))
        {
            AnnotationCanvas.PlaceSprite(at);
            return;
        }

        AnnotationCanvas.Render();
    }

    private void InputCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        UpdateCursor(ToFrame(e));

        if (_cropping)
        {
            if (_cropStart is { } start && e.Pointer.IsInContact)
            {
                DrawCropRectangle(start, e.GetCurrentPoint(InputCanvas).Position);
            }

            return;
        }

        if (AnnotationToolbar.IsSamplingColor)
        {
            HintText.Text = L("Click to take the colour under the pointer • {0}", SampleAt(ToFrame(e)).ToHex());
            return;
        }

        if (e.Pointer.IsInContact)
        {
            var at = ToFrame(e);

            // Before the editor is told, so a press that has become a stroke stops being a
            // candidate for the hold before the next sample lands.
            _hold.Moved(at);
            _editor.PointerMoved(at, ToModifiers(e), PenInput.Of(e));
            AnnotationCanvas.Render();
        }
    }

    private void InputCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        InputCanvas.ReleasePointerCaptures();
        _hold.Ended();

        if (_cropping)
        {
            if (_cropStart is { } start)
            {
                var end = e.GetCurrentPoint(InputCanvas).Position;
                _cropStart = null;
                CropTo(CaptureRegion.FromPoints(start.X, start.Y, end.X, end.Y));
            }

            return;
        }

        if (AnnotationToolbar.IsSamplingColor)
        {
            return;
        }

        var committed = _editor.PointerReleased(ToFrame(e), ToModifiers(e), PenInput.Of(e));
        AnnotationCanvas.Render();

        // After the release, because what a ruler reads, what text a highlighter crossed
        // and what words a redaction covers are none of them knowable until the drag that
        // made the mark has stopped.
        AnnotationCanvas.FinishedGesture(committed);
    }

    /// <summary>
    /// Says what a press here would do, by way of the cursor: crop, pick, reshape, move,
    /// or draw. The image is one canvas, and none of those is a control with a hover state
    /// of its own.
    /// </summary>
    private void UpdateCursor(CapturePoint point)
    {
        if (_cropping)
        {
            InputCanvas.UseCursor(InputSystemCursorShape.Cross);
            return;
        }

        if (AnnotationToolbar.IsSamplingColor)
        {
            InputCanvas.UseCursor(InputSystemCursorShape.Cross);
            return;
        }

        // Before the tool is asked: the selected mark's handles are grabbable whatever is
        // in hand, so a crosshair over one would say "draw" where the press reshapes.
        if (_editor.SelectionShown is { } shown
            && AnnotationHandles.At(shown, point, _editor.Scale) is { } handle)
        {
            InputCanvas.UseCursor(CursorHints.For(handle.Kind));
            return;
        }

        if (_editor.Tool != AnnotationTool.Select)
        {
            InputCanvas.UseCursor(InputSystemCursorShape.Cross);
            return;
        }

        InputCanvas.UseCursor(_editor.Document.HitTest(point) is null
            ? InputSystemCursorShape.Arrow
            : InputSystemCursorShape.SizeAll);
    }

    private void EditorRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // While the entry box has focus the keyboard is its: Delete edits the text and
        // Ctrl+Z takes back typing.
        if (AnnotationCanvas.IsTyping)
        {
            return;
        }

        var control = IsDown(VirtualKey.Control);
        var shift = IsDown(VirtualKey.Shift);

        switch (e.Key)
        {
            case VirtualKey.Escape:
                e.Handled = true;

                // Escape gives up whatever is in flight, in the order the user would
                // expect to get it back: the crop mode, then an armed sampler, then a
                // half-drawn mark. It never closes the window — the marks in an editor
                // are work, not a capture waiting to be confirmed.
                if (_cropping)
                {
                    SetCropping(false);
                    return;
                }

                if (AnnotationToolbar.IsSamplingColor)
                {
                    SetColorSampling(false);
                    return;
                }

                if (_editor.Cancel())
                {
                    AnnotationCanvas.Render();
                }

                return;

            case VirtualKey.Enter:
                e.Handled = true;
                _ = FinishAsync();
                return;

            case VirtualKey.Delete or VirtualKey.Back:
                e.Handled = true;
                if (_editor.DeleteSelected())
                {
                    AnnotationCanvas.Render();
                }

                return;

            case VirtualKey.Z when control:
                e.Handled = true;
                if (shift)
                {
                    Redo();
                }
                else
                {
                    Undo();
                }

                return;

            case VirtualKey.Y when control:
                e.Handled = true;
                Redo();
                return;

            case VirtualKey.S when control:
                e.Handled = true;
                _ = SaveAsync();
                return;

            // macshot's Cmd+0, Cmd+1, Cmd+= and Cmd+- (`OverlayView.swift:9114-9145`).
            // The menu on the zoom button was the only way to reach any of them here,
            // which is one press in every other image editor.
            case VirtualKey.Number0 or VirtualKey.NumberPad0 when control:
                e.Handled = true;
                ZoomTo(1);
                return;

            case VirtualKey.Number1 or VirtualKey.NumberPad1 when control:
                e.Handled = true;
                ZoomToFit();
                return;

            case VirtualKey.Add or PlusKey when control:
                e.Handled = true;
                ZoomBy(ZoomStep);
                return;

            case VirtualKey.Subtract or MinusKey when control:
                e.Handled = true;
                ZoomBy(1 / ZoomStep);
                return;

            case VirtualKey.C when control:
                e.Handled = true;
                _ = CopyAsync();
                return;

            // The overlay's rule, for the same reason: whatever Space is bound to would
            // otherwise fire mid-drag and the reposition it exists for could never run.
            case VirtualKey.Space when _editor.CanReposition:
                e.Handled = true;
                return;

            default:
                // The same single keys the overlay answers to, so a tool is reached the
                // same way whichever window the capture ended up in. Modifiers are left
                // alone: Ctrl and Alt belong to this window's commands and the system's.
                if (!control && !IsDown(VirtualKey.Menu) && AnnotationToolbar.TryShortcut(e.Key))
                {
                    e.Handled = true;
                }

                return;
        }
    }

    /// <summary>
    /// One press back along one timeline: the marks first, and the image operations
    /// underneath them.
    /// </summary>
    /// <remarks>
    /// The document is asked first and answers whether it had anything, which is what
    /// keeps the order right — an operation resets the document, so after one there is
    /// nothing in it to undo and the operation itself is next.
    /// </remarks>
    private void Undo()
    {
        if (_editor.Undo())
        {
            AnnotationCanvas.Render();
            return;
        }

        if (!_imageUndo.TryPop(out var previous))
        {
            return;
        }

        _frame = previous.Frame;
        _editor.Document.Reset(previous.Annotations);
        Present();
        ShowSize();
        HintText.Text = StandingHint;
    }

    /// <summary>
    /// Redo covers the marks only. An image operation is not redone: it consumed the
    /// marks that were live when it ran, and offering to replay it would have to
    /// re-flatten annotations that are objects again — which is the operation, not a
    /// replay of it.
    /// </summary>
    private void Redo()
    {
        _editor.Redo();
        AnnotationCanvas.Render();
    }

    private void Crop_Click(object sender, RoutedEventArgs e) => SetCropping(_cropButton?.IsChecked == true);

    private void SetCropping(bool cropping)
    {
        _cropping = cropping;
        _cropStart = null;
        if (_cropButton is { } button)
        {
            button.IsChecked = cropping;
        }

        CropRectangle.Visibility = Visibility.Collapsed;
        HintText.Text = cropping ? CropHint : StandingHint;

        // A crop and a mark are both dragged out, so leaving a tool armed underneath the
        // crop mode would make the next drag ambiguous to the user, not to the code.
        if (cropping && _editor.Cancel())
        {
            AnnotationCanvas.Render();
        }
    }

    private void DrawCropRectangle(Point start, Point end)
    {
        var region = CaptureRegion.FromPoints(start.X, start.Y, end.X, end.Y);
        Canvas.SetLeft(CropRectangle, region.X);
        Canvas.SetTop(CropRectangle, region.Y);
        CropRectangle.Width = region.Width;
        CropRectangle.Height = region.Height;
        CropRectangle.Visibility = Visibility.Visible;
    }

    /// <summary>Keeps the pixels inside <paramref name="region"/> and nothing else.</summary>
    private void CropTo(CaptureRegion region)
    {
        CropRectangle.Visibility = Visibility.Collapsed;

        // A click rather than a drag is not a crop to nothing, it is a miss.
        if (region.Width < 4 || region.Height < 4)
        {
            HintText.Text = CropHint;
            return;
        }

        ApplyImageOperation(
            frame =>
            {
                var (width, height, pixels) = FrameTransforms.Crop(
                    frame.Width,
                    frame.Height,
                    frame.BgraPixels,
                    region);
                return new CapturedFrame(frame.VirtualX, frame.VirtualY, width, height, pixels);
            },
            $"Cropped to {(int)region.Width} × {(int)region.Height} • Ctrl+Z to undo");

        // One crop at a time: staying armed would let the next drag crop the crop, which
        // is rarely what was meant and always a surprise.
        SetCropping(false);
    }

    /// <summary>
    /// Turns every colour over.
    /// </summary>
    /// <remarks>
    /// Done to the pixels here and then, rather than held as a switch the way the
    /// overlay holds it: the editor already has an undo stack for exactly this kind of
    /// change, and inverting twice through it gives back what was there.
    /// </remarks>
    private void InvertImage()
    {
        ApplyImageOperation(
            frame => new CapturedFrame(
                frame.VirtualX,
                frame.VirtualY,
                frame.Width,
                frame.Height,
                FrameTransforms.Invert(frame.Width, frame.Height, frame.BgraPixels)),
            "Colours inverted • Ctrl+Z to undo");
    }

    private void FlipImage(bool horizontal)
    {
        ApplyImageOperation(
            frame => new CapturedFrame(
                frame.VirtualX,
                frame.VirtualY,
                frame.Width,
                frame.Height,
                horizontal
                    ? FrameTransforms.FlipHorizontal(frame.Width, frame.Height, frame.BgraPixels)
                    : FrameTransforms.FlipVertical(frame.Width, frame.Height, frame.BgraPixels)),
            $"Flipped {(horizontal ? "horizontally" : "vertically")} • Ctrl+Z to undo");
    }

    /// <summary>
    /// Turns the frame on or off, which is the one control this window has for it.
    /// </summary>
    /// <remarks>
    /// macshot's Beautify button only ever arms the frame (<c>OverlayView.swift:8031-8044</c>)
    /// and leaves taking it off to the On switch on the options row it opens. That row is
    /// the capture overlay's; this window does not carry one, so the button that put the
    /// frame on has to be the one that takes it off — otherwise a frame armed here could
    /// only be undone by closing the capture without saving it.
    /// </remarks>
    private void ToggleFrame()
    {
        if (!_beautify.Enabled)
        {
            // The style last chosen, which is where a different one is picked from: one
            // press for the usual answer, the Frame menu for the rest.
            FrameWith(_settings.Current.BeautifyStyleIndex);
            return;
        }

        _beautify = _beautify with { Enabled = false };
        AnnotationToolbar.Beautified = false;
        Present();
        RefreshDone();
        HintText.Text = StandingHint;
    }

    /// <summary>
    /// Mounts the capture on one of the backgrounds, and remembers which — the next capture
    /// framed is almost always framed the same way.
    /// </summary>
    /// <remarks>
    /// The frame's own measurements come from the settings, which is where the capture
    /// overlay's options row writes them. This window has no such row, so what it can
    /// change is which background — and a capture reopened inside a frame it was delivered
    /// with keeps that frame's numbers until something is picked here.
    /// </remarks>
    private void FrameWith(int styleIndex)
    {
        var options = _settings.Current.ToBeautifyOptions(BeautifyBackgroundStore.Current) with
        {
            StyleIndex = styleIndex,
            Enabled = true,
        };

        // A capture that came from clicking a window already has a title bar and rounded
        // corners in its pixels, and the state remembers that it did. The overlay applies
        // the same rule to the frame it draws; without it here, reopening such a capture
        // and picking another background would stack a drawn title bar on the real one.
        _beautify = BeautifyState.Of(
            _beautify.IsWindowSnap ? options.ForWindowSnap() : options,
            _beautify.IsWindowSnap,
            EditorRoot.XamlRoot?.RasterizationScale ?? 1,
            BeautifyBackgroundStore.CurrentBytes);

        _backdrop = BeautifyBackgroundStore.Current;

        AnnotationToolbar.Beautified = true;
        Present();

        // By hand, for the reason the adjust sliders are: the frame is a layer the
        // delivered pixels are taken through, so it leaves no undo step and no image
        // operation behind and nothing the document raises says it happened.
        RefreshDone();

        HintText.Text = L(
            "Framed in {0}",
            _beautify.StyleIndex == BeautifyOptions.CustomBackgroundStyle
                ? L("Your own picture")
                : BeautifyRenderer.Styles[_beautify.StyleIndex].Name);

        try
        {
            _settings.Save(_settings.Current with { BeautifyStyleIndex = styleIndex });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The capture is already framed. Failing to remember which style is not worth
            // interrupting that.
            DiagnosticLog.Write($"Could not remember the frame style: {exception.Message}");
        }
    }

    /// <summary>
    /// Runs an operation that replaces the pixels: the marks are burned in first, so what
    /// it acts on is the image as the user sees it.
    /// </summary>
    /// <remarks>
    /// Flattening is what makes crop, flip and add-capture one mechanism instead of three.
    /// Carrying live annotations across a transform would mean a rule per operation for
    /// where each mark lands. The frame is not one of these and never was worth being one:
    /// it has no answer at all for a mark outside the image it just padded, and as a layer
    /// it does not need one.
    /// </remarks>
    private void ApplyImageOperation(Func<CapturedFrame, CapturedFrame> operation, string done)
    {
        try
        {
            var flattened = AnnotationCanvas.ToFrame() ?? _frame;
            var result = operation(flattened);

            _imageUndo.Push((_frame, [.. _editor.Document.Annotations]));
            _frame = result;
            _editor.Document.Reset();
            Present();
            ShowSize();
            HintText.Text = done;

            DiagnosticLog.Verbose($"editor image now {_frame.Width}x{_frame.Height}: {done}");
        }
        catch (Exception exception)
        {
            HintText.Text = exception.Message;
        }
    }

    /// <remarks>
    /// Written out rather than routed through <see cref="RunRecognitionAsync"/>, whose
    /// callback is synchronous: the QR scan is a second await. See the same method on
    /// the overlay.
    /// </remarks>
    private async Task ReadTextAsync()
    {
        var previousHint = HintText.Text;
        HintText.Text = L("Reading text...");
        try
        {
            var lines = await AnnotationCanvas.RecognizeAsync();

            // The capture rather than what would be delivered: a frame carries no text and
            // no code, and reading one through a background would only mean handing the
            // engine a larger picture with the same words in it.
            var frame = AnnotationCanvas.ToFrame() ?? _frame;
            var codes = await TextRecognizer.ScanQrCodesAsync(frame);
            HintText.Text = previousHint;

            new TextRecognitionWindow(TextRecognizer.ToText(lines), _settings, frame, codes).Activate();
        }
        catch (Exception exception)
        {
            HintText.Text = exception.Message;
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
                HintText.Text = L("No personal data found in the image");
                return;
            }

            _editor.Document.AddRange(annotations);
            AnnotationCanvas.Render();
            HintText.Text = L("Redacted {0} • Ctrl+Z to undo", annotations.Count);
        });
    }

    /// <summary>
    /// Covers every line of text in the image rather than only what looks like a secret.
    /// </summary>
    /// <remarks>
    /// The other half of macshot's auto group, and the one used when the answer is already
    /// known to be "all of it": a panel of somebody else's data, where naming what is
    /// sensitive is work the user should not have to do and a pattern that missed one
    /// would be a leak rather than a missed box.
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
                HintText.Text = L("(No text detected in the selected area)");
                return;
            }

            _editor.Document.AddRange(annotations);
            AnnotationCanvas.Render();
            HintText.Text = L("Redacted {0} • Ctrl+Z to undo", annotations.Count);
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
    /// Covers every face in the image — the redaction the two text passes cannot make.
    /// </summary>
    /// <remarks>
    /// The image itself rather than a crop of it: the editor is already showing exactly
    /// what will be written, so <c>_frame</c> and the document share one coordinate space
    /// and a box found in the pixels is a box on the canvas.
    /// </remarks>
    private async Task RedactFacesAsync()
    {
        var faces = await FaceFinder.FindAsync(_frame);
        if (faces.Count == 0)
        {
            HintText.Text = L("No faces detected in the selected area");
            return;
        }

        AddRedactions(faces);
    }

    /// <summary>
    /// Covers the people in the image, and not only their faces.
    /// </summary>
    /// <remarks>
    /// Through the subject model, because Windows has no human-rectangles pass — see
    /// <c>CaptureOverlayWindow.RedactPeopleAsync</c> for what that costs and why it is
    /// still the right answer for a redaction.
    /// </remarks>
    private async Task RedactPeopleAsync()
    {
        CapturedFrame lifted;
        try
        {
            lifted = await BackgroundRemover.CutOutAsync(_frame, _settings.Current.BackgroundRemoval);
        }
        catch (InvalidOperationException failure)
        {
            HintText.Text = failure.Message;
            return;
        }

        if (SubjectBounds.Of(lifted.BgraPixels, lifted.Width, lifted.Height) is not { } subject)
        {
            HintText.Text = L("No people detected in the selected area");
            return;
        }

        AddRedactions([subject]);
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
        try
        {
            if (await ClipboardImages.PickAsync(WinRT.Interop.WindowNative.GetWindowHandle(this)) is { } picture)
            {
                AnnotationToolbar.UseStampPicture(picture);
            }
        }
        catch (Exception exception)
        {
            // A file that will not decode is the file's fault: the editor carries on with
            // whatever stamp it already had.
            DiagnosticLog.Write($"Could not load the stamp image: {exception}");
            HintText.Text = exception.Message;
        }
    }

    /// <summary>Puts one redaction over each box, as the single undo step one press earns.</summary>
    private void AddRedactions(IReadOnlyList<CaptureRegion> boxes)
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
        AnnotationCanvas.Render();
        HintText.Text = L("Redacted {0} • Ctrl+Z to undo", covered.Count);
    }

#if !OFFLINE
    /// <summary>
    /// Lays a translation over the text in the image, in place, rather than reading it
    /// out into a window.
    /// </summary>
    private async Task TranslateAsync()
    {
        HintText.Text = L("Translating...");
        try
        {
            HintText.Text =
                await TranslationPlacement.RunAsync(AnnotationCanvas, _settings.Current, CancellationToken.None);
        }
        catch (Exception exception)
        {
            HintText.Text = exception.Message;
        }
    }
#endif

    private async Task RunRecognitionAsync(Action<IReadOnlyList<RecognizedLine>> handle)
    {
        var previousHint = HintText.Text;
        HintText.Text = L("Reading text...");
        try
        {
            var lines = await AnnotationCanvas.RecognizeAsync();
            HintText.Text = previousHint;
            handle(lines);
        }
        catch (Exception exception)
        {
            HintText.Text = exception.Message;
        }
    }

    private async Task CopyAsync()
    {
        try
        {
            await AnnotationCanvas.FlushAsync();
            if (Delivered() is { } finished)
            {
                await ImageDelivery.CopyToClipboardAsync(finished);
                HintText.Text = L("Copied to the clipboard");
            }
        }
        catch (Exception exception)
        {
            HintText.Text = exception.Message;
        }
    }

    /// <summary>
    /// Lifts the subject out of the image and puts the cut-out on the clipboard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The clipboard rather than the canvas, which is what macshot's editor does with it
    /// (<c>DetachedEditorWindowController.swift:551</c>) even though its overlay finishes
    /// the capture instead. There is a reason to keep that split here beyond following
    /// it: every other transform on this window — invert, flip, crop — leaves an opaque
    /// image behind, and the canvas composes against an opaque background. A cut-out put
    /// back on it would be the subject over the old background, which is the picture the
    /// button was pressed to get rid of.
    /// </para>
    /// <para>
    /// The unframed canvas rather than what would be delivered, for the same reason: the
    /// frame <em>is</em> a background, and lifting the subject out of a framed capture
    /// would be asking the model to tell a gradient from a screenshot.
    /// </para>
    /// <para>
    /// So the window is left exactly as it was, and the transparency lives on the
    /// clipboard where a PNG can carry it.
    /// </para>
    /// </remarks>
    private async Task RemoveBackgroundAsync()
    {
        var previousHint = HintText.Text;
        HintText.Text = L("Removing background...");

        try
        {
            await AnnotationCanvas.FlushAsync();
            if (AnnotationCanvas.ToFrame() is not { } finished)
            {
                HintText.Text = previousHint;
                return;
            }

            var cut = await BackgroundRemover.CutOutAsync(finished, _settings.Current.BackgroundRemoval);
            await ImageDelivery.CopyToClipboardAsync(cut);
            HintText.Text = L("Copied to the clipboard");
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"Background removal failed: {exception}");
            HintText.Text = exception.Message;
        }
    }

    /// <summary>
    /// Asks where to put the image and writes it there, leaving the window open the way
    /// every other delivery here does.
    /// </summary>
    private async Task SaveAsAsync()
    {
        try
        {
            await AnnotationCanvas.FlushAsync();
            if (Delivered() is { } finished
                && await SavePrompt.WriteAsync(this, finished, _settings.Current) is { } path)
            {
                HintText.Text = L("Saved to {0}", path);
            }
        }
        catch (Exception exception)
        {
            HintText.Text = exception.Message;
        }
    }

    /// <summary>
    /// Opens the system share pane over the editor. The window stays open afterwards:
    /// unlike the overlay there is nothing here to dismiss, and the image is still
    /// being worked on.
    /// </summary>
    private async Task ShareAsync()
    {
        try
        {
            await AnnotationCanvas.FlushAsync();
            if (Delivered() is { } finished)
            {
                await ShareSheet.ShowAsync(this, finished, _settings.Current);
            }
        }
        catch (Exception exception)
        {
            HintText.Text = exception.Message;
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            await AnnotationCanvas.FlushAsync();
            if (Delivered() is { } finished)
            {
                // Null means the user dismissed the dialog they asked for, which is not a
                // failure and not a save: the hint is left saying whatever it said.
                if (await SavePrompt.SaveAsync(this, finished, _settings.Current) is { } path)
                {
                    HintText.Text = L("Saved to {0}", path);
                }
            }
        }
        catch (Exception exception)
        {
            HintText.Text = exception.Message;
        }
    }

    private async Task PinAsync()
    {
        try
        {
            await AnnotationCanvas.FlushAsync();
            if (Delivered() is { } finished)
            {
                PinRequested?.Invoke(this, finished);
            }
        }
        catch (Exception exception)
        {
            HintText.Text = exception.Message;
        }
    }

#if !OFFLINE
    /// <summary>
    /// Sends what is on the canvas, asking first when the preferences say to.
    /// </summary>
    /// <remarks>
    /// Raised to the owner rather than uploaded here, as Pin is: the toast belongs to the
    /// app, not to this window, and an editor closed mid-upload must not take the panel
    /// reporting it away.
    /// </remarks>
    private async Task UploadAsync()
    {
        try
        {
            await AnnotationCanvas.FlushAsync();
            if (Delivered() is not { } finished)
            {
                return;
            }

            if (_settings.Current.UploadConfirm
                && !Upload.UploadConfirm.Ask(
                    WinRT.Interop.WindowNative.GetWindowHandle(this),
                    _settings.Current.UploadProvider))
            {
                return;
            }

            UploadRequested?.Invoke(this, finished);
        }
        catch (Exception exception)
        {
            HintText.Text = exception.Message;
        }
    }
#endif

    /// <summary>
    /// Enter: delivers the capture as it now stands, wherever the preferences send one,
    /// and leaves the window open.
    /// </summary>
    /// <remarks>
    /// Open, because macshot's Enter here is its quick capture and does not close either
    /// (<c>DetachedEditorWindowController.swift:477-495</c>) — and because copy, save and
    /// pin on this same strip already act and stay. An editor is somewhere the user works,
    /// and the one action that closed the window was the one that also wrote a file. Done
    /// is what finishes.
    /// </remarks>
    private async Task FinishAsync()
    {
        try
        {
            await AnnotationCanvas.FlushAsync();
            if (Delivered() is not { } finished)
            {
                return;
            }

            Finished?.Invoke(
                this,
                new CaptureCompletion(finished, CaptureOutcome.Deliver, Editable));

            // What was just delivered is what is written down, so the window is clean and
            // Done goes away. Without this the close would still ask about marks that are
            // already in the file the user asked for.
            Rebaseline();
        }
        catch (Exception exception)
        {
            HintText.Text = exception.Message;
        }
    }

    /// <summary>
    /// Done: writes the marks back over the capture they belong to, and closes.
    /// </summary>
    /// <remarks>
    /// macshot's <c>commitToHistory</c> (<c>:346-361</c>). Not a second delivery — no
    /// clipboard and no file — because the capture was delivered when it was taken and this
    /// is the drawing being finished. That distinction is the whole reason the button
    /// exists: with Enter as the only way out, annotating a capture auto-save had already
    /// written would put a second file beside the first for one keypress.
    /// </remarks>
    private async Task CommitAsync()
    {
        try
        {
            await AnnotationCanvas.FlushAsync();
            CommitEdits();
            Close();
        }
        catch (Exception exception)
        {
            HintText.Text = exception.Message;
        }
    }

    /// <summary>
    /// Hands the marks over and marks the window clean, without touching the window
    /// itself.
    /// </summary>
    /// <remarks>
    /// Synchronous, so the close prompt can use it: a closing handler cannot await, and
    /// cancelling the close to await a commit would mean closing the window a second time
    /// from inside its own close notification. Nothing in flight is flushed here for the
    /// same reason — a label still being typed when the X is pressed is not committed,
    /// which is the same answer macshot gives, since it composites what is on the canvas.
    /// </remarks>
    private void CommitEdits()
    {
        if (Delivered() is not { } finished)
        {
            return;
        }

        Finished?.Invoke(
            this,
            new CaptureCompletion(finished, CaptureOutcome.Commit, Editable));

        Rebaseline();
    }

    /// <summary>
    /// Stops a close that would throw away marks, until the user has said what to do with
    /// them.
    /// </summary>
    /// <remarks>
    /// Neither answer that lets the window go cancels the close: the window is already
    /// closing and both branches simply leave it clean on the way out, so nothing here
    /// re-enters. Only Cancel touches <paramref name="args"/>.
    /// </remarks>
    private void OfferToKeepEdits(AppWindowClosingEventArgs args)
    {
        if (!Edits.DiffersFrom(_saved))
        {
            return;
        }

        switch (UnsavedEditsPrompt.Ask(WinRT.Interop.WindowNative.GetWindowHandle(this)))
        {
            case UnsavedEdits.Keep:
                CommitEdits();
                return;

            case UnsavedEdits.Discard:
                // Marked clean rather than committed, so nothing is written and this
                // handler cannot ask a second time about the same marks.
                Rebaseline();
                return;

            default:
                args.Cancel = true;
                return;
        }
    }

    /// <summary>Everything about this capture the user can have changed.</summary>
    private EditorState Edits =>
        new(_editor.Document.UndoDepth, _imageUndo.Count, _effects, _beautify);

    /// <summary>
    /// What this capture can be reopened from: the pixels as the image operations left
    /// them, the marks, and the two layers they are being seen through.
    /// </summary>
    /// <remarks>
    /// <see cref="_frame"/> rather than what is on the canvas, because both the adjustment
    /// and the frame are layers here and the field is the image underneath them. Archiving
    /// the canvas would bake a layer in and then store the numbers that made it beside it,
    /// so reopening would apply it a second time.
    /// </remarks>
    private EditableCapture? Editable =>
        AnnotationCanvas.ToEditable(_frame, new CaptureEditState(_effects, _beautify));

    /// <summary>Records the capture as written down, and takes Done off the bar.</summary>
    private void Rebaseline()
    {
        _saved = Edits;
        RefreshDone();
    }

    /// <summary>
    /// Offers Done only when there is something to commit.
    /// </summary>
    /// <remarks>
    /// macshot's rule and its reason (<c>:256-257</c>): the overlay has no Done until you
    /// draw, and an editor that always showed one would be offering to finish a capture
    /// nobody had started on.
    /// </remarks>
    private void RefreshDone() => _doneButton.Visibility = Edits.DiffersFrom(_saved)
        ? Visibility.Visible
        : Visibility.Collapsed;

    private void SetColorSampling(bool armed)
    {
        AnnotationToolbar.SetColorSampling(armed);
        HintText.Text = armed
            ? "Click to take the colour under the pointer • Esc to stop"
            : StandingHint;
    }

    private void TakeSampledColor(CapturePoint point)
    {
        var sampled = SampleAt(point);
        AnnotationToolbar.ApplyPickedColor(sampled);
        SetColorSampling(false);
        HintText.Text = L("Took {0} • {1}", sampled.ToHex(), StandingHint);
    }

    /// <summary>
    /// The colour on the canvas under a point.
    /// </summary>
    /// <remarks>
    /// The canvas rather than the image it was opened with, which is macshot's
    /// <c>sampleCanvasColor</c>: a colour already used in a mark can be picked back up.
    /// It also makes the reading agree with the picture when an adjustment is on — reading
    /// <c>_frame</c> reported the colour before Invert had been applied to it.
    /// </remarks>
    private AnnotationColor SampleAt(CapturePoint point) => PixelEffects.Sample(
        _frame.BgraPixels,
        _frame.Width,
        _frame.Height,
        (int)point.X,
        (int)point.Y,
        AnnotationCanvas.Rendered);

    private CapturePoint ToFrame(PointerRoutedEventArgs e) =>
        _placement.ToFrame(e.GetCurrentPoint(InputCanvas).Position);

    /// <summary>
    /// Alt is macOS's Option here as it is over the capture: held, a tool draws over the
    /// marks under the pointer instead of grabbing them. Ctrl is macOS's Control: on the
    /// selected line, arrow or ruler it bends the mark through another anchor, on any other
    /// mark it adds to the selection, and over empty space it sweeps a marquee.
    /// </summary>
    /// <remarks>
    /// Alt from the keyboard rather than from the pointer event: Windows treats it as a
    /// menu key and does not reliably carry it in a pointer event's modifiers. Shift and
    /// Ctrl are not menu keys and do arrive on the event, so they are read from it.
    /// <c>CaptureOverlayWindow.ToModifiers</c> is the same mapping over the same editor and
    /// has to stay in step with this one.
    /// </remarks>
    private static EditorModifiers ToModifiers(PointerRoutedEventArgs e) =>
        (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) ? EditorModifiers.Constrain : EditorModifiers.None)
        | (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control) ? EditorModifiers.Extend : EditorModifiers.None)
        | (IsDown(VirtualKey.Menu) ? EditorModifiers.DrawThrough : EditorModifiers.None)
        | (IsDown(VirtualKey.Space) ? EditorModifiers.Reposition : EditorModifiers.None);

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
