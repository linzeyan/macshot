using System.Runtime.InteropServices;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;

using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace Macshot.Windows;

/// <summary>
/// The camera, in a corner of the recorded region.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>WebcamOverlay</c>. The one thing macshot puts on screen during a
/// recording that is meant to be <em>in</em> the file — the HUD and the region border are
/// both placed to stay out of it — which is why it sits inside the region rather than
/// beside it, and why it is up during setup: what it covers has to be seen before the
/// recording starts, not discovered in the finished video.
/// </para>
/// <para>
/// Frames are pulled with a <see cref="MediaFrameReader"/> and drawn into a
/// <see cref="SoftwareBitmapSource"/> rather than started as a preview: WinUI 3 has no
/// <c>CaptureElement</c>, and a reader is the only way to get camera pixels onto a XAML
/// surface in a desktop app.
/// </para>
/// <para>
/// The bubble is cut to a circle or a rounded rectangle with <c>SetWindowRgn</c>, for the
/// same reason the countdown dial is: a WinUI window has no per-pixel alpha, so a round
/// picture has to be a round window.
/// </para>
/// </remarks>
public sealed partial class WebcamWindow : Window
{
    private readonly SoftwareBitmapSource _frames = new();

    private MediaCapture? _camera;
    private MediaFrameReader? _reader;

    /// <summary>
    /// Set while a frame is being handed to the UI thread. Frames arrive faster than they
    /// can be drawn, and queueing every one of them would put the bubble further behind
    /// the room the longer it ran.
    /// </summary>
    private int _drawing;

    private bool _closed;

    public WebcamWindow()
    {
        InitializeComponent();
        Preview.Source = _frames;
        Closed += (_, _) => _closed = true;
    }

    /// <summary>
    /// Puts the bubble in its corner of <paramref name="region"/> and starts the camera.
    /// </summary>
    /// <remarks>
    /// Answers false when there is no camera, or when Windows' camera privacy setting
    /// says no. The caller closes the window then rather than leaving a black circle over
    /// the recording — a bubble showing nothing is worse than no bubble.
    /// </remarks>
    /// <param name="cameraId">
    /// Which camera, from the menu on the toolbar's own webcam switch, or empty for the
    /// first one the machine offers.
    /// </param>
    public async Task<bool> ShowInAsync(
        CaptureRegion region,
        WebcamCorner corner,
        WebcamSize size,
        WebcamShape shape,
        double scale,
        string? cameraId)
    {
        var (x, y, width, height) = WebcamInset.For(region, corner, size, scale);

        var appWindow = this.GetAppWindow();
        appWindow.MakeChromeless().IsAlwaysOnTop = true;
        appWindow.MoveAndResize(new RectInt32(x, y, width, height));

        CircleRing.Visibility = shape is WebcamShape.Circle ? Visibility.Visible : Visibility.Collapsed;
        RectRing.Visibility = shape is WebcamShape.Circle ? Visibility.Collapsed : Visibility.Visible;

        if (shape is WebcamShape.RoundedRect)
        {
            var radius = WebcamInset.CornerRadiusFor(size, shape) * scale;
            RectRing.RadiusX = radius;
            RectRing.RadiusY = radius;
        }

        Cut(shape, width, height, (int)Math.Round(WebcamInset.CornerRadiusFor(size, shape) * scale));

        // Ordered front rather than activated: the overlay behind it is taking the
        // keyboard, and a camera bubble that stole focus would swallow Escape.
        appWindow.Show(activateWindow: false);

        return await StartAsync(cameraId);
    }

    private async Task<bool> StartAsync(string? cameraId)
    {
        try
        {
            var source = await ColourSourceAsync(cameraId);
            if (source is null)
            {
                DiagnosticLog.Write("No camera to put in the recording.");
                return false;
            }

            var reader = await _camera!.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            reader.FrameArrived += Reader_FrameArrived;

            if (await reader.StartAsync() is not MediaFrameReaderStartStatus.Success)
            {
                DiagnosticLog.Write("The camera would not start.");
                return false;
            }

            _reader = reader;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Windows' camera privacy setting. Reported rather than thrown: the recording
            // is still worth making without the camera in it.
            DiagnosticLog.Write("Camera access is turned off for this machine or for macshot.");
            return false;
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"Could not start the camera: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opens the camera and answers the frame source that gives colour pictures.
    /// </summary>
    /// <remarks>
    /// Which camera that is comes from <see cref="CameraDevices"/>, which is also what
    /// fills the menu offering the choice — one place, so that the camera a recording opens
    /// is the one the menu ticked.
    /// </remarks>
    private async Task<MediaFrameSource?> ColourSourceAsync(string? cameraId)
    {
        if (await CameraDevices.ChosenAsync(cameraId) is not { } chosen)
        {
            return null;
        }

        var camera = new MediaCapture();
        await camera.InitializeAsync(new MediaCaptureInitializationSettings
        {
            SourceGroup = chosen,
            SharingMode = MediaCaptureSharingMode.SharedReadOnly,
            StreamingCaptureMode = StreamingCaptureMode.Video,

            // CPU, because the frames are read into a SoftwareBitmap rather than handed
            // to a hardware preview surface there is no way to ask for here.
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
        });

        _camera = camera;

        foreach (var source in camera.FrameSources.Values)
        {
            if (source.Info.SourceKind is MediaFrameSourceKind.Color)
            {
                return source;
            }
        }

        return null;
    }

    private void Reader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        // Dropped rather than queued while the last one is still being drawn: the camera
        // produces frames faster than a XAML surface takes them, and a queue would put
        // the bubble further behind the room the longer the recording ran.
        if (Interlocked.Exchange(ref _drawing, 1) == 1)
        {
            return;
        }

        using var frame = sender.TryAcquireLatestFrame();
        if (frame?.VideoMediaFrame?.SoftwareBitmap is not { } arrived)
        {
            _drawing = 0;
            return;
        }

        // Copied even when the format already matches, not just converted into the one
        // SoftwareBitmapSource takes: the frame is disposed at the end of this method and
        // the drawing happens later, on the UI thread, out of a bitmap that would be gone
        // by then.
        var drawable = SoftwareBitmap.Convert(arrived, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (!_closed)
                {
                    await _frames.SetBitmapAsync(drawable);
                    Waiting.IsActive = false;
                    Waiting.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception exception)
            {
                DiagnosticLog.Write($"Could not draw a camera frame: {exception.Message}");
            }
            finally
            {
                drawable.Dispose();
                _drawing = 0;
            }
        }))
        {
            // The window is going away and its queue is closed.
            drawable.Dispose();
            _drawing = 0;
        }
    }

    /// <summary>Stops the camera and takes the bubble off the screen.</summary>
    /// <remarks>
    /// The camera is released before the window closes, so the light beside it goes out
    /// when the bubble does. A camera left running behind a closed window is the one
    /// failure here nobody would forgive.
    /// </remarks>
    public async Task StopAsync()
    {
        if (_reader is { } reader)
        {
            _reader = null;
            reader.FrameArrived -= Reader_FrameArrived;

            try
            {
                await reader.StopAsync();
            }
            catch (Exception exception)
            {
                DiagnosticLog.Write($"Could not stop the camera cleanly: {exception.Message}");
            }

            reader.Dispose();
        }

        _camera?.Dispose();
        _camera = null;

        Close();
    }

    /// <summary>
    /// Cuts the window to the bubble's shape, since its content cannot be transparent.
    /// </summary>
    private void Cut(WebcamShape shape, int width, int height, int radius)
    {
        var handle = WindowNative.GetWindowHandle(this);

        var region = shape is WebcamShape.Circle
            ? CreateEllipticRgn(0, 0, width, height)
            : CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);

        // Not deleted afterwards: SetWindowRgn takes ownership of what it is given.
        SetWindowRgn(handle, region, true);
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
}
