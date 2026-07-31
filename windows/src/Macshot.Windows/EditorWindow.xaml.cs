using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Core.Recognition;
using Macshot.Windows.Rendering;
using Macshot.Windows.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

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
    private const string StandingHint = "Draw to annotate • Ctrl+Z undo • Ctrl+S save • Ctrl+C copy";
    private const string CropHint = "Drag the part to keep • Esc to stop cropping";

    /// <summary>
    /// How much of the work area the window may take when the image is larger. Short of
    /// the whole thing on purpose: a window that opens exactly screen-sized looks like a
    /// full-screen app that has lost its title bar.
    /// </summary>
    private const double MaxWorkAreaShare = 0.9;

    private readonly SettingsStore _settings;
    private readonly AnnotationEditor _editor = new(new AnnotationDocument());
    private readonly IFramePlacement _placement = new ImageFramePlacement();

    /// <summary>
    /// What each image operation replaced, and the marks that were live when it ran.
    /// </summary>
    /// <remarks>
    /// An image operation burns the marks into the pixels, so the document's own history
    /// stops describing anything real and is reset. This is what undo needs instead: the
    /// image as it was, and the annotations as objects again. The two timelines never
    /// interleave confusingly because an operation empties one of them.
    /// </remarks>
    private readonly Stack<(CapturedFrame Frame, Annotation[] Annotations)> _imageUndo = new();

    private CapturedFrame _frame;

    /// <summary>
    /// What the Adjust popover is asking for. A layer over the image rather than
    /// something burnt into it, because the sliders are dragged: an adjustment applied on
    /// every tick would leave an undo stack thirty entries deep for one decision. The
    /// delivered pixels come from the preview, so what is on show is what is handed over.
    /// </summary>
    private ImageEffectsOptions _effects = ImageEffectsOptions.Default;
    private ToggleButton? _cropButton;
    private bool _cropping;
    private Point? _cropStart;
    private bool _zoomFitted;

    public EditorWindow(CapturedFrame frame, SettingsStore settings)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        InitializeComponent();
        this.GetAppWindow().UseAppIcon();
    }

    /// <summary>Raised with the image the user wants kept on top of everything else.</summary>
    public event EventHandler<CapturedFrame>? PinRequested;

    /// <summary>
    /// Raised with the finished image when Done is pressed. The owner decides what
    /// delivery means, exactly as it does for a capture, so this window needs no opinion
    /// about clipboards or folders.
    /// </summary>
    public event EventHandler<CapturedFrame>? Finished;

    public async Task ShowAsync()
    {
        WireToolbar();
        WireCanvas();
        BuildActions();
        Present();

        var appWindow = this.GetAppWindow();
        appWindow.MoveAndResize(PlaceOverImage());
        HintText.Text = StandingHint;
        Activate();
        EditorRoot.Focus(FocusState.Programmatic);

        // Nothing here is asynchronous yet, and making the method async anyway keeps the
        // call sites the same as every other window's — and leaves room for the decode a
        // reopened capture needs.
        await Task.CompletedTask;
    }

    /// <summary>
    /// Shows the current image at its own pixel size, which is what makes one layout unit
    /// one pixel and the placement between marks and pixels the identity.
    /// </summary>
    private void Present()
    {
        ImageHost.Width = _frame.Width;
        ImageHost.Height = _frame.Height;
        Title = $"macshot — {_frame.Width} × {_frame.Height}";
        var shown = _effects.IsIdentity
            ? _frame
            : new CapturedFrame(
                _frame.VirtualX,
                _frame.VirtualY,
                _frame.Width,
                _frame.Height,
                ImageEffects.Apply(_frame.Width, _frame.Height, _frame.BgraPixels, _effects));

        AnnotationCanvas.Present(shown, new CaptureRegion(0, 0, shown.Width, shown.Height), _placement);
    }

    private void WireToolbar()
    {
        // Before binding: it decides which actions the strip carries, and cancelling a
        // capture or moving a region are not among them here.
        AnnotationToolbar.EditorMode = true;

        AnnotationToolbar.Bind(_editor, _settings);
        AnnotationToolbar.Changed += (_, _) => AnnotationCanvas.Render();
        AnnotationToolbar.ColorSamplingToggled += (_, armed) => SetColorSampling(armed);
        AnnotationToolbar.EffectsChanged += (_, options) =>
        {
            _effects = options;
            Present();
        };
        AnnotationToolbar.CommandInvoked += (_, command) => RunToolbarCommand(command);
        AnnotationToolbar.ShowToolbar(true);

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

            case ToolbarCommand.Translate:
#if !OFFLINE
                _ = TranslateAsync();
#endif
                return;

            case ToolbarCommand.InvertColors:
                InvertImage();
                return;

            case ToolbarCommand.Beautify:
                // The style last chosen from the Frame menu, which is where a different
                // one is picked. One press for the usual answer, the menu for the rest —
                // and the pixels change here and then, so Ctrl+Z is the way back.
                FrameImage(_settings.Current.ToBeautifyOptions().StyleIndex);
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
        _cropButton = new ToggleButton { Content = "Crop" };
        _cropButton.Click += Crop_Click;

        var flip = new Button { Content = "Flip" };
        var flipMenu = new MenuFlyout { Placement = FlyoutPlacementMode.Bottom };
        flipMenu.Items.Add(MenuItem("Horizontal", () => FlipImage(horizontal: true)));
        flipMenu.Items.Add(MenuItem("Vertical", () => FlipImage(horizontal: false)));
        flip.Flyout = flipMenu;

        var frame = new Button { Content = "Frame" };
        var frameMenu = new MenuFlyout { Placement = FlyoutPlacementMode.Bottom };
        for (var index = 0; index < BeautifyRenderer.Styles.Count; index++)
        {
            // Captured by value: the loop variable would otherwise be read at click time,
            // by which point it is past the end of the list.
            var chosen = index;
            frameMenu.Items.Add(MenuItem(BeautifyRenderer.Styles[index].Name, () => FrameImage(chosen)));
        }

        frame.Flyout = frameMenu;

        foreach (var button in new FrameworkElement[] { _cropButton, flip, frame })
        {
            ImageOperations.Children.Add(button);
        }
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

        // The image is in pixels and so is an AppWindow's size, so no scaling comes into
        // this. The extra height is the title bar and the toolbar, which the image would
        // otherwise open underneath.
        var width = Math.Clamp(_frame.Width + 48, 640, Math.Max(640, maxWidth));
        var height = Math.Clamp(_frame.Height + 160, 480, Math.Max(480, maxHeight));

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
    /// Only ever out, never in: a small capture blown up to fill the window would be
    /// shown softer than it is, and the marks would be drawn at a size that means
    /// nothing. Only once, or resizing the window would keep overruling the zoom the
    /// user chose.
    /// </remarks>
    private void Scroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_zoomFitted || Scroller.ViewportWidth <= 0 || Scroller.ViewportHeight <= 0)
        {
            return;
        }

        _zoomFitted = true;
        var fit = Math.Min(Scroller.ViewportWidth / _frame.Width, Scroller.ViewportHeight / _frame.Height);
        if (fit < 1)
        {
            Scroller.ChangeView(null, null, (float)Math.Max(fit, Scroller.MinZoomFactor));
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

        if (AnnotationCanvasView.IsPlacedByClick(_editor.Tool))
        {
            AnnotationCanvas.PlaceSprite(ToFrame(e));
            return;
        }

        _editor.PointerPressed(ToFrame(e), ToModifiers(e));
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
            HintText.Text = $"Click to take the colour under the pointer • {SampleAt(ToFrame(e)).ToHex()}";
            return;
        }

        if (e.Pointer.IsInContact)
        {
            _editor.PointerMoved(ToFrame(e), ToModifiers(e));
            AnnotationCanvas.Render();
        }
    }

    private void InputCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        InputCanvas.ReleasePointerCaptures();

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

        _editor.PointerReleased(ToFrame(e), ToModifiers(e));
        AnnotationCanvas.Render();

        // After the release, because a ruler's reading is only knowable once the drag
        // that measures it has stopped.
        AnnotationCanvas.LabelRulers();
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

        if (_editor.Tool != AnnotationTool.Select)
        {
            InputCanvas.UseCursor(InputSystemCursorShape.Cross);
            return;
        }

        if (_editor.SelectionShown is { } shown
            && AnnotationHandles.At(shown, point, _editor.Scale) is { } handle)
        {
            InputCanvas.UseCursor(CursorHints.For(handle.Kind));
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

            case VirtualKey.C when control:
                e.Handled = true;
                _ = CopyAsync();
                return;

            default:
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
    /// Puts the image on one of the gradient backgrounds, and remembers which — the next
    /// capture framed is almost always framed the same way.
    /// </summary>
    private void FrameImage(int styleIndex)
    {
        var options = _settings.Current.ToBeautifyOptions() with { StyleIndex = styleIndex };

        ApplyImageOperation(
            frame =>
            {
                var (width, height, pixels) = BeautifyRenderer.Render(
                    frame.Width,
                    frame.Height,
                    frame.BgraPixels,
                    options);
                return new CapturedFrame(frame.VirtualX, frame.VirtualY, width, height, pixels);
            },
            $"Framed in {BeautifyRenderer.Styles[styleIndex].Name} • Ctrl+Z to undo");

        try
        {
            _settings.Save(_settings.Current with { BeautifyStyleIndex = styleIndex });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The image is already framed. Failing to remember which style is not worth
            // interrupting that.
            DiagnosticLog.Write($"Could not remember the frame style: {exception.Message}");
        }
    }

    /// <summary>
    /// Runs an operation that replaces the pixels: the marks are burned in first, so what
    /// it acts on is the image as the user sees it.
    /// </summary>
    /// <remarks>
    /// Flattening is what makes crop, flip and frame one mechanism instead of three.
    /// Carrying live annotations across a transform would mean a rule per operation for
    /// where each mark lands, and framing has no answer at all for a mark outside the
    /// image it just padded.
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
            HintText.Text = done;

            DiagnosticLog.Verbose($"editor image now {_frame.Width}x{_frame.Height}: {done}");
        }
        catch (Exception exception)
        {
            HintText.Text = exception.Message;
        }
    }

    private async Task ReadTextAsync()
    {
        await RunRecognitionAsync(lines =>
        {
            var window = new TextRecognitionWindow(TextRecognizer.ToText(lines), _settings);
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
                HintText.Text = "No personal data found in the image";
                return;
            }

            _editor.Document.AddRange(annotations);
            AnnotationCanvas.Render();
            HintText.Text = $"Redacted {annotations.Count} • Ctrl+Z to undo";
        });
    }

#if !OFFLINE
    /// <summary>
    /// Lays a translation over the text in the image, in place, rather than reading it
    /// out into a window.
    /// </summary>
    private async Task TranslateAsync()
    {
        HintText.Text = "Translating...";
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
        HintText.Text = "Reading text...";
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
            if (AnnotationCanvas.ToFrame() is { } finished)
            {
                await ImageDelivery.CopyToClipboardAsync(finished);
                HintText.Text = "Copied to the clipboard";
            }
        }
        catch (Exception exception)
        {
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
            if (AnnotationCanvas.ToFrame() is { } finished
                && await SavePrompt.WriteAsync(this, finished, _settings.Current) is { } path)
            {
                HintText.Text = $"Saved to {path}";
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
            if (AnnotationCanvas.ToFrame() is { } finished)
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
            if (AnnotationCanvas.ToFrame() is { } finished)
            {
                var path = await ImageDelivery.SaveAsync(finished, _settings.Current);
                HintText.Text = $"Saved to {path}";
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
            if (AnnotationCanvas.ToFrame() is { } finished)
            {
                PinRequested?.Invoke(this, finished);
            }
        }
        catch (Exception exception)
        {
            HintText.Text = exception.Message;
        }
    }

    private async Task FinishAsync()
    {
        try
        {
            await AnnotationCanvas.FlushAsync();
            if (AnnotationCanvas.ToFrame() is { } finished)
            {
                Finished?.Invoke(this, finished);
            }

            Close();
        }
        catch (Exception exception)
        {
            HintText.Text = exception.Message;
        }
    }

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
        HintText.Text = $"Took {sampled.ToHex()} • {StandingHint}";
    }

    /// <summary>
    /// The colour of the image under a point, read from the unannotated pixels so a mark
    /// already drawn cannot be sampled by accident.
    /// </summary>
    private AnnotationColor SampleAt(CapturePoint point) => PixelEffects.Sample(
        _frame.BgraPixels,
        _frame.Width,
        _frame.Height,
        (int)point.X,
        (int)point.Y);

    private CapturePoint ToFrame(PointerRoutedEventArgs e) =>
        _placement.ToFrame(e.GetCurrentPoint(InputCanvas).Position);

    private static EditorModifiers ToModifiers(PointerRoutedEventArgs e) =>
        e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) ? EditorModifiers.Constrain : EditorModifiers.None;

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
