using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Output;
using Macshot.Windows.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using static Macshot.Windows.Services.Localization;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified StorageFile resolves to
// Macshot.Windows.Storage.StorageFile and does not compile.
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Macshot.Windows;

/// <summary>
/// The window a finished recording opens in: play it, keep part of it, and write that
/// part somewhere.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>VideoEditorWindowController</c> as far as its own description of it goes
/// — trimming, exporting and uploading. Its bottom bar is reproduced control for control:
/// play and mute, the MP4/GIF toggle, what the source is, the dimensions, the quality,
/// the GIF frame rate, the estimate, and then Save, Save As, Upload, the folder and Copy.
/// </para>
/// <para>
/// macshot's effects band is here for one of its six effects. A zoom can be placed on
/// the band, dragged, resized and given a level, and the export applies it —
/// <see cref="ZoomVideoCompositor"/>, whose head explains at length why Windows needs a
/// hand-built frame pipeline where macOS has an <c>AVVideoComposition</c>. Censor, cut,
/// freeze, speed and text are still absent; the point of building one effect all the way
/// through was to find out what the other five would cost, and the answer is in that
/// file. The parity notes record the remaining gap.
/// </para>
/// <para>
/// Trimming goes through <see cref="MediaComposition"/>, which is the platform's own
/// editor: a clip with time taken off each end, rendered to a file.
/// </para>
/// <para>
/// A GIF is playback and delivery only, exactly as it is in macshot, whose AVFoundation
/// cannot read one either. The trim bar and the export controls are taken off the window
/// rather than left on to be ignored.
/// </para>
/// <para>
/// Nothing in continuous integration has an encoder or a recording to encode, so all of
/// this is compile-checked only.
/// </para>
/// </remarks>
public sealed partial class VideoEditorWindow : Window
{
    /// <summary>macshot's minimum, and about what the bottom bar needs to fit.</summary>
    private const double MinWidthDips = 900;

    private const double MinHeightDips = 420;

    /// <summary>How much of the window the controls below the picture take.</summary>
    private const double ControlsHeightDips = 172;

    /// <summary>How near a handle a press has to land to count as a drag of it.</summary>
    private const double HandleGrab = 14;

    /// <summary>
    /// How near a pill's end a press has to land to resize rather than move it.
    /// </summary>
    /// <remarks>
    /// Smaller than <see cref="HandleGrab"/> because a pill can be short: a zoom half a
    /// second long on a two-minute recording is a few pixels wide, and a grab margin the
    /// size of the trim handles' would leave no middle to take hold of.
    /// </remarks>
    private const double PillEdgeGrab = 6;

    /// <summary>The levels the box beside the band offers. macshot's range.</summary>
    private static readonly double[] ZoomLevels = [1.2, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0];

    /// <summary>How often the playhead is moved along while something is playing.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(60);

    private static readonly List<VideoEditorWindow> Open = [];

    private readonly SettingsStore _settings;
    private readonly string _path;
    private readonly DispatcherQueueTimer _ticker;

    private MediaPlayer? _player;
    private BitmapImage? _gif;
    private bool _gifIsPlaying;

    private double _duration;
    private int _sourceWidth;
    private int _sourceHeight;
    private int _sourceFrameRate;
    private long _sourceBytes;

    /// <summary>The percentages behind the dimensions menu's entries.</summary>
    private IReadOnlyList<int> _scales = [100];

    private VideoTrim _trim;
    private Handle _dragging;

    /// <summary>The zoom on the band, or nothing when there is none.</summary>
    /// <remarks>
    /// One, not a list. macshot's band stacks as many as fit and gives each a UUID to tell
    /// them apart; this is the first effect of the six to be built here, and one of them is
    /// what says whether the pipeline underneath works.
    /// </remarks>
    private VideoZoomSegment? _zoom;

    private PillGrab _zoomDragging;

    /// <summary>Where in the pill the press landed, so a move does not jump.</summary>
    private double _zoomGrabOffset;

    /// <summary>Where the last export went, which is what the folder button opens.</summary>
    private string? _exported;

    private bool _filling;
    private bool _busy;

    private enum Handle
    {
        None,
        Start,
        End,
        Playhead,
    }

    /// <summary>Which part of the zoom pill a press took hold of.</summary>
    private enum PillGrab
    {
        None,
        Start,
        End,
        Body,
    }

    public VideoEditorWindow(string path, SettingsStore settings)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        InitializeComponent();

        // Every string in the XAML is already the English text macshot keys by, so the
        // window is translated in place rather than written twice.
        this.Localize();
        this.GetAppWindow().UseAppIcon();

        Title = $"macshot — {Path.GetFileName(path)}";

        _ticker = DispatcherQueue.CreateTimer();
        _ticker.Interval = TickInterval;
        _ticker.Tick += (_, _) => FollowPlayhead();

        Closed += (_, _) => Teardown();
    }

#if !OFFLINE
    /// <summary>Raised with the file the user asked to send.</summary>
    public event EventHandler<string>? UploadRequested;
#endif

    /// <summary>Whether the source is a GIF, which nothing here can re-encode.</summary>
    private bool SourceIsGif =>
        string.Equals(Path.GetExtension(_path), ".gif", StringComparison.OrdinalIgnoreCase);

    private bool ExportsGif => SourceIsGif || FormatBox.SelectedIndex == 1;

    private string Extension => ExportsGif ? ".gif" : ".mp4";

    /// <summary>Which of <see cref="VideoExportPlan.ScaleChoices"/> is chosen.</summary>
    private int ExportPercent =>
        DimensionsBox.SelectedIndex >= 0 && DimensionsBox.SelectedIndex < _scales.Count
            ? _scales[DimensionsBox.SelectedIndex]
            : 100;

    private VideoQuality ExportQuality => QualityBox.SelectedIndex >= 0
        ? (VideoQuality)QualityBox.SelectedIndex
        : VideoQuality.High;

    private int GifFrameRate => GifFrameRateBox.SelectedIndex >= 0
        ? GifFrameRateBox.SelectedIndex + VideoExportPlan.MinGifFrameRate
        : VideoExportPlan.DefaultGifFrameRate;

    private int FrameRate => _sourceFrameRate > 0 ? _sourceFrameRate : RecordingPlan.DefaultFrameRate;

    /// <summary>Opens <paramref name="path"/> in an editor, or brings its window forward.</summary>
    /// <param name="prepare">
    /// Run on a window that is being opened, and not on one that was already there. It is
    /// where the owner subscribes to <c>UploadRequested</c>, and subscribing a second
    /// time to the same window would send the same recording twice.
    /// </param>
    /// <remarks>
    /// One window per file. Two editors on the same recording would let two different
    /// trims be exported over each other, and whichever finished last would win without
    /// either of them saying so.
    /// </remarks>
    public static void Show(string path, SettingsStore settings, Action<VideoEditorWindow>? prepare = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        foreach (var window in Open)
        {
            if (string.Equals(window._path, path, StringComparison.OrdinalIgnoreCase))
            {
                window.TakeForeground();
                return;
            }
        }

        var editor = new VideoEditorWindow(path, settings);
        Open.Add(editor);
        editor.Closed += (_, _) => Open.Remove(editor);
        prepare?.Invoke(editor);
        editor.Activate();
        editor.TakeForeground();

        // Deliberately not awaited: the window is on screen while the recording is being
        // read, which for a long one is the difference between a window that opens and a
        // click that appears to have done nothing.
        _ = editor.LoadAsync();
    }

    /// <summary>Reads the recording, sizes the window to it, and fills the controls.</summary>
    private async Task LoadAsync()
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(_path);
            _sourceBytes = (long)(await file.GetBasicPropertiesAsync()).Size;

            if (SourceIsGif)
            {
                await LoadGifAsync(file);
            }
            else
            {
                await LoadVideoAsync(file);
            }

            _trim = VideoTrim.Whole(_duration);

            FillControls();
            PlaceOnScreen();
            DrawTimeline();
            _ticker.Start();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or COMException)
        {
            // Not through L: macshot has no string for this, and inventing a key would
            // look translated and never be.
            StatusText.Text = $"macshot could not open that recording: {error.Message}";
        }
    }

    private async Task LoadVideoAsync(StorageFile file)
    {
        var properties = await file.Properties.GetVideoPropertiesAsync();
        _sourceWidth = (int)properties.Width;
        _sourceHeight = (int)properties.Height;
        _duration = properties.Duration.TotalSeconds;
        _sourceFrameRate = await FrameRateOfAsync(file);

        _player = new MediaPlayer
        {
            Source = MediaSource.CreateFromStorageFile(file),
            AutoPlay = false,
        };

        Player.SetMediaPlayer(_player);
    }

    /// <summary>
    /// How many frames a second the source runs at, for the bitrate an export asks for.
    /// </summary>
    /// <remarks>
    /// Read from the file rather than taken from the recording preference, because the
    /// file need not have been made by this copy of macshot — or by macshot at all — and
    /// a 60 fps recording encoded as though it were 30 comes out at half the bitrate it
    /// needs.
    /// </remarks>
    private static async Task<int> FrameRateOfAsync(StorageFile file)
    {
        try
        {
            var profile = await MediaEncodingProfile.CreateFromFileAsync(file);
            var rate = profile.Video?.FrameRate;

            return rate is { Denominator: > 0 }
                ? (int)Math.Round(rate.Numerator / (double)rate.Denominator)
                : RecordingPlan.DefaultFrameRate;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or COMException)
        {
            return RecordingPlan.DefaultFrameRate;
        }
    }

    /// <summary>Shows a GIF in the image control, since nothing else will play one.</summary>
    private async Task LoadGifAsync(StorageFile file)
    {
        using (var stream = await file.OpenReadAsync())
        {
            var decoder = await BitmapDecoder.CreateAsync(stream);
            _sourceWidth = (int)decoder.PixelWidth;
            _sourceHeight = (int)decoder.PixelHeight;
            _duration = await GifSecondsAsync(decoder);
        }

        // From the path rather than from that stream: an animated image goes on reading
        // its source for as long as it is playing, and a stream closed underneath it
        // stops the animation on whichever frame it had reached.
        var image = new BitmapImage { AutoPlay = true, UriSource = new Uri(file.Path) };

        _gif = image;
        _gifIsPlaying = true;
        GifView.Source = image;
        GifView.Visibility = Visibility.Visible;
        Player.Visibility = Visibility.Collapsed;
        ShowPlayState(true);
    }

    /// <summary>
    /// How long a GIF runs, which only its own frame delays know.
    /// </summary>
    /// <remarks>
    /// Read rather than left at zero because it is the only length the window can show,
    /// and a recording whose duration reads 0:00 looks like one that failed to open.
    /// </remarks>
    private static async Task<double> GifSecondsAsync(BitmapDecoder decoder)
    {
        var total = 0.0;

        for (uint index = 0; index < decoder.FrameCount; index++)
        {
            var frame = await decoder.GetFrameAsync(index);
            var properties = await frame.BitmapProperties.GetPropertiesAsync(["/grctlext/Delay"]);

            // Hundredths of a second, and zero means "as fast as the viewer likes", which
            // every viewer has settled on meaning a tenth.
            var hundredths = properties.TryGetValue("/grctlext/Delay", out var value)
                && value?.Value is ushort centiseconds
                && centiseconds > 0
                    ? centiseconds
                    : 10;

            total += hundredths / 100.0;
        }

        return total;
    }

    private void FillControls()
    {
        _filling = true;
        try
        {
            FormatBox.ItemsSource = new List<string> { "MP4", "GIF" };
            FormatBox.SelectedIndex = SourceIsGif ? 1 : 0;

            _scales = VideoExportPlan.ScaleChoices(_sourceWidth, _sourceHeight);
            DimensionsBox.ItemsSource = _scales
                .Select(percent => VideoExportPlan.DimensionsLabel(_sourceWidth, _sourceHeight, percent))
                .ToList();
            DimensionsBox.SelectedIndex = 0;

            QualityBox.ItemsSource = new List<string> { L("Low"), L("Medium"), L("High") };
            QualityBox.SelectedIndex = (int)VideoQuality.High;

            GifFrameRateBox.ItemsSource = Enumerable
                .Range(
                    VideoExportPlan.MinGifFrameRate,
                    VideoExportPlan.MaxGifFrameRate - VideoExportPlan.MinGifFrameRate + 1)
                .Select(rate => $"{rate} fps")
                .ToList();
            GifFrameRateBox.SelectedIndex = Math.Clamp(
                _settings.Current.GifFrameRate - VideoExportPlan.MinGifFrameRate,
                0,
                GifFrameRateBox.Items.Count - 1);

            // macshot's own label for a zoom level, and the reason the box is filled even
            // with no zoom placed: the row would otherwise resize the moment one was.
            ZoomLevelBox.ItemsSource = ZoomLevels.Select(FormatZoom).ToList();
            ZoomLevelBox.SelectedIndex = Array.IndexOf(ZoomLevels, VideoZoomSegment.DefaultLevel);

            SourceInfoText.Text = _sourceWidth > 0
                ? $"{Bytes(_sourceBytes)}  ·  {_sourceWidth} × {_sourceHeight}"
                : Bytes(_sourceBytes);
        }
        finally
        {
            _filling = false;
        }

        ShowExportChoices();
    }

    /// <summary>
    /// Shows only the choices the chosen format reads, and what the export will cost.
    /// </summary>
    private void ShowExportChoices()
    {
        var gif = ExportsGif;
        var editable = !SourceIsGif;

        // Nothing on this bar means anything for a GIF source: there is no re-encode to
        // choose a size or a rate for, and nothing that could trim it either.
        TrimPanel.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        FormatBox.IsEnabled = editable;
        DimensionsBox.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        QualityBox.Visibility = editable && !gif ? Visibility.Visible : Visibility.Collapsed;
        GifFrameRateBox.Visibility = editable && gif ? Visibility.Visible : Visibility.Collapsed;

        // A GIF has no audio track to silence, so the button would do nothing.
        MuteButton.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;

        // Taken off the window rather than greyed out for a GIF export, which goes through
        // a frame-by-frame encoder of its own that knows nothing about the band. A disabled
        // band would read as one that is temporarily unavailable.
        EffectsPanel.Visibility = editable && !gif ? Visibility.Visible : Visibility.Collapsed;
        ZoomLevelBox.IsEnabled = _zoom is not null;
        ZoomButton.Content = _zoom is null ? L("Add Zoom") : L("Delete Zoom");

        var estimated = editable
            && _sourceWidth > 0
            && VideoExportPlan.ShowsEstimate(_trim.Duration, _duration, ExportPercent, ExportQuality, gif);

        EstimateText.Text = estimated
            ? "~" + Bytes(VideoExportPlan.EstimatedBytes(
                _sourceBytes,
                _trim.Duration,
                _duration,
                ExportPercent,
                ExportQuality,
                _sourceFrameRate,
                GifFrameRate,
                gif))
            : string.Empty;

        SaveButton.IsEnabled = !_busy;
        SaveAsButton.IsEnabled = !_busy;

#if OFFLINE
        UploadButton.Visibility = Visibility.Collapsed;
#else
        // Dark rather than taken off the bar while imgbb is the provider: the button
        // belongs there, and what is missing is a destination that takes video.
        UploadButton.IsEnabled = !_busy
            && Core.Upload.UploadProviders.TakesVideo(_settings.Current.UploadProvider);
#endif
    }

    private void Export_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_filling)
        {
            return;
        }

        if (GifFrameRateBox.SelectedIndex >= 0 && GifFrameRate != _settings.Current.GifFrameRate)
        {
            _settings.Save(_settings.Current with { GifFrameRate = GifFrameRate });
        }

        // A changed choice means the file that was written is no longer what this bar
        // describes, so the folder and Copy buttons go back to pointing at the source.
        _exported = null;
        ShowExportChoices();
    }

    // Playback

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_gif is { } gif)
        {
            _gifIsPlaying = !_gifIsPlaying;

            if (_gifIsPlaying)
            {
                gif.Play();
            }
            else
            {
                gif.Stop();
            }

            ShowPlayState(_gifIsPlaying);
            return;
        }

        if (_player is not { } player)
        {
            return;
        }

        if (player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            player.Pause();
            ShowPlayState(false);
            return;
        }

        // From the start of the kept piece rather than from wherever the playhead was
        // left: what this button plays has to be what Save would write.
        var at = player.PlaybackSession.Position.TotalSeconds;
        if (at < _trim.Start || at >= _trim.End)
        {
            player.PlaybackSession.Position = TimeSpan.FromSeconds(_trim.Start);
        }

        player.Play();
        ShowPlayState(true);
    }

    private void ShowPlayState(bool playing) => PlayButton.Content = playing ? "⏸" : "▶";

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (_player is not { } player)
        {
            return;
        }

        player.IsMuted = !player.IsMuted;
        MuteButton.Content = player.IsMuted ? "🔇" : "🔊";
    }

    /// <summary>
    /// Moves the playhead along with the recording, and stops at the end of the kept
    /// piece.
    /// </summary>
    private void FollowPlayhead()
    {
        if (_player is not { } player || _dragging is Handle.Playhead)
        {
            return;
        }

        if (player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing
            && player.PlaybackSession.Position.TotalSeconds >= _trim.End)
        {
            player.Pause();
            player.PlaybackSession.Position = TimeSpan.FromSeconds(_trim.Start);
            ShowPlayState(false);
        }

        DrawTimeline();
    }

    // Timeline

    private void Timeline_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var x = e.GetCurrentPoint(Timeline).Position.X;
        var toStart = Math.Abs(x - XFor(_trim.Start));
        var toEnd = Math.Abs(x - XFor(_trim.End));

        // Whichever handle is nearer, within grabbing distance of it; past that the press
        // is a scrub, which is what a click on a timeline means everywhere else.
        _dragging = toStart <= HandleGrab && toStart <= toEnd
            ? Handle.Start
            : toEnd <= HandleGrab
                ? Handle.End
                : Handle.Playhead;

        Timeline.CapturePointer(e.Pointer);
        Drag(x);
    }

    private void Timeline_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is not Handle.None)
        {
            Drag(e.GetCurrentPoint(Timeline).Position.X);
        }
    }

    private void Timeline_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        Timeline.ReleasePointerCapture(e.Pointer);
        EndDrag();
    }

    /// <summary>
    /// Ends the drag when the pointer is taken away rather than let go — the window
    /// losing it to something else, say. Without this the handle would go on following
    /// the mouse afterwards.
    /// </summary>
    private void Timeline_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (_dragging is Handle.None)
        {
            return;
        }

        _dragging = Handle.None;
        ShowExportChoices();
    }

    private void Drag(double x)
    {
        var seconds = SecondsFor(x);

        switch (_dragging)
        {
            case Handle.Start:
                _trim = _trim.WithStart(seconds, _duration);
                Seek(_trim.Start);
                _exported = null;
                break;

            case Handle.End:
                _trim = _trim.WithEnd(seconds, _duration);
                Seek(_trim.End);
                _exported = null;
                break;

            case Handle.Playhead:
                Seek(_trim.Clamp(seconds));
                break;

            default:
                return;
        }

        DrawTimeline();
    }

    private void Seek(double seconds)
    {
        if (_player is { } player && _duration > 0)
        {
            player.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, _duration));
        }
    }

    private double XFor(double seconds) =>
        _duration > 0 ? seconds / _duration * Timeline.ActualWidth : 0;

    private double SecondsFor(double x) =>
        Timeline.ActualWidth > 0 ? Math.Clamp(x / Timeline.ActualWidth, 0, 1) * _duration : 0;

    private void DrawTimeline()
    {
        var width = Timeline.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        TimelineTrack.Width = width;

        var start = XFor(_trim.Start);
        var end = XFor(_trim.End);
        TimelineKept.Width = Math.Max(0, end - start);
        Canvas.SetLeft(TimelineKept, start);

        Canvas.SetLeft(TrimStartHandle, start - (TrimStartHandle.Width / 2));
        Canvas.SetLeft(TrimEndHandle, end - (TrimEndHandle.Width / 2));

        var at = _player?.PlaybackSession.Position.TotalSeconds ?? 0;
        Canvas.SetLeft(Playhead, XFor(at));

        ElapsedText.Text = VideoTrim.Format(at);
        DurationText.Text = VideoTrim.Format(_duration);
        SelectionText.Text = L("%@ selected")
            .Replace("%@", VideoTrim.Format(_trim.Duration), StringComparison.Ordinal);

        DrawEffectsBand();
    }

    // Effects band

    /// <summary>Puts the zoom pill under the stretch of timeline it covers.</summary>
    /// <remarks>
    /// Measured against the timeline rather than against the band, though the two are the
    /// same width. What a band is for is that a pill sits under the moment it applies to,
    /// and two widths that could drift apart would eventually put it under a different one.
    /// </remarks>
    private void DrawEffectsBand()
    {
        if (_zoom is not { } zoom)
        {
            ZoomPill.Visibility = Visibility.Collapsed;
            ZoomPillLabel.Visibility = Visibility.Collapsed;
            return;
        }

        var left = XFor(zoom.Start);
        var width = Math.Max(2, XFor(zoom.End) - left);

        ZoomPill.Visibility = Visibility.Visible;
        ZoomPill.Width = width;
        Canvas.SetLeft(ZoomPill, left);

        // Inside the pill where it fits, and out of the way rather than clipped where it
        // does not: a short zoom on a long recording is a few pixels wide, and a label
        // painted over it would be unreadable in either place.
        ZoomPillLabel.Text = FormatZoom(zoom.Level);
        ZoomPillLabel.Visibility = Visibility.Visible;
        Canvas.SetLeft(ZoomPillLabel, width >= 44 ? left + 6 : left + width + 6);
    }

    private void Zoom_Click(object sender, RoutedEventArgs e)
    {
        if (_zoom is not null)
        {
            _zoom = null;
        }
        else if (_duration >= VideoZoomSegment.MinDuration)
        {
            // At the playhead, which is where the user is looking. macshot places it at
            // the point the band was right-clicked; this port has no band menu, so the
            // playhead is the equivalent statement of where.
            var at = _player?.PlaybackSession.Position.TotalSeconds ?? _trim.Start;
            _zoom = VideoZoomSegment.Placed(at, _duration).WithLevel(SelectedZoomLevel);
        }
        else
        {
            StatusText.Text = L("Not enough room here");
            return;
        }

        // What was exported no longer matches the band, so the folder and Copy buttons go
        // back to pointing at the source.
        _exported = null;
        ShowExportChoices();
        DrawTimeline();
    }

    private void ZoomLevel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _zoom is not { } zoom)
        {
            return;
        }

        _zoom = zoom.WithLevel(SelectedZoomLevel);
        _exported = null;
        ShowExportChoices();
        DrawTimeline();
    }

    private void EffectsBand_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_zoom is not { } zoom)
        {
            return;
        }

        var x = e.GetCurrentPoint(EffectsBand).Position.X;
        var left = XFor(zoom.Start);
        var right = XFor(zoom.End);

        // Outside the pill entirely: a press on bare band does nothing, rather than
        // moving the zoom the user was not pointing at.
        if (x < left - PillEdgeGrab || x > right + PillEdgeGrab)
        {
            return;
        }

        _zoomDragging = x <= left + PillEdgeGrab
            ? PillGrab.Start
            : x >= right - PillEdgeGrab
                ? PillGrab.End
                : PillGrab.Body;

        _zoomGrabOffset = SecondsFor(x) - zoom.Start;
        EffectsBand.CapturePointer(e.Pointer);
    }

    private void EffectsBand_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_zoomDragging is PillGrab.None || _zoom is not { } zoom)
        {
            return;
        }

        var seconds = SecondsFor(e.GetCurrentPoint(EffectsBand).Position.X);

        _zoom = _zoomDragging switch
        {
            PillGrab.Start => zoom.WithStart(seconds, _duration),
            PillGrab.End => zoom.WithEnd(seconds, _duration),
            _ => zoom.MovedTo(seconds - _zoomGrabOffset, _duration),
        };

        _exported = null;
        DrawTimeline();
    }

    private void EffectsBand_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EffectsBand.ReleasePointerCapture(e.Pointer);
        EndZoomDrag();
    }

    /// <summary>
    /// Ends the drag when the pointer is taken away rather than let go, for the same
    /// reason <see cref="Timeline_PointerCaptureLost"/> does.
    /// </summary>
    private void EffectsBand_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndZoomDrag();

    private void EndZoomDrag()
    {
        if (_zoomDragging is PillGrab.None)
        {
            return;
        }

        _zoomDragging = PillGrab.None;

        // The ramps are scaled to the segment's length, which the drag may just have
        // changed. Re-applying the level is what rescales them.
        if (_zoom is { } zoom)
        {
            _zoom = zoom.WithLevel(zoom.Level);
        }

        ShowExportChoices();
        DrawTimeline();
    }

    private double SelectedZoomLevel => ZoomLevelBox.SelectedIndex >= 0
        && ZoomLevelBox.SelectedIndex < ZoomLevels.Length
            ? ZoomLevels[ZoomLevelBox.SelectedIndex]
            : VideoZoomSegment.DefaultLevel;

    /// <summary>macshot's <c>formatZoom</c>: one decimal, and an x.</summary>
    private static string FormatZoom(double level) =>
        level.ToString("0.0", CultureInfo.InvariantCulture) + "x";

    // Output

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var folder = string.IsNullOrWhiteSpace(_settings.Current.RecordingDirectory)
            ? ImageDelivery.ResolveDirectory(_settings.Current)
            : _settings.Current.RecordingDirectory;

        var name = FilenameTemplate.Resolve(_settings.Current.RecordingFilenameTemplate, DateTimeOffset.Now);
        await ExportToAsync(Path.Combine(folder, name + Extension));
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };

        // A picker belongs to a window rather than to an app here, and an unpackaged app
        // has to say which window or the call fails outright.
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        picker.FileTypeChoices.Add(ExportsGif ? "GIF" : "MP4", [Extension]);
        picker.SuggestedFileName = Path.GetFileNameWithoutExtension(_path);

        if (await picker.PickSaveFileAsync() is { } chosen)
        {
            await ExportToAsync(chosen.Path);
        }
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var target = _exported ?? _path;
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };

            // The file rather than its pixels, as macshot does it: a recording has no one
            // frame to paste, and what takes a video takes an attachment. The path goes
            // on beside it as text, for the things that only take text.
            package.SetStorageItems([await StorageFile.GetFileFromPathAsync(target)]);
            package.SetText(target);

            Clipboard.SetContent(package);

            // Flushed so the recording survives macshot being quit, which is the point of
            // putting it on the clipboard in the first place.
            Clipboard.Flush();

            StatusText.Text = L("Copied to clipboard!");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException)
        {
            StatusText.Text = error.Message;
        }
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Picked out rather than merely opened: a folder of thirty recordings does
            // not answer "which of these is the one I just wrote".
            using (Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_exported ?? _path}\"")))
            {
            }
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or ObjectDisposedException)
        {
            StatusText.Text = error.Message;
        }
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
#if !OFFLINE
        // Exported first, so what is sent is the piece that was kept rather than the
        // whole recording. An untouched MP4 needs no export at all.
        if (_exported is null && NeedsExport())
        {
            var temporary = Path.Combine(
                Path.GetTempPath(),
                Path.GetFileNameWithoutExtension(_path) + "-trimmed" + Extension);

            if (!await ExportToAsync(temporary))
            {
                return;
            }
        }

        UploadRequested?.Invoke(this, _exported ?? _path);
#else
        await Task.CompletedTask;
#endif
    }

    /// <summary>Whether anything asked for differs from the file already on disk.</summary>
    private bool NeedsExport() =>
        !SourceIsGif
        && (!_trim.IsWhole(_duration)
            || ExportPercent < 100
            || ExportsGif
            || _zoom is { IsFlat: false }
            || ExportQuality != VideoQuality.High);

    /// <summary>
    /// Writes the kept piece to <paramref name="destination"/>, and says whether it
    /// worked.
    /// </summary>
    private async Task<bool> ExportToAsync(string destination)
    {
        SetBusy(true);

        try
        {
            var source = await StorageFile.GetFileFromPathAsync(_path);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(destination)!);
            var file = await folder.CreateFileAsync(
                Path.GetFileName(destination),
                CreationCollisionOption.ReplaceExisting);

            string? note = null;

            if (SourceIsGif)
            {
                // Nothing to re-encode and nothing that could have been changed, so the
                // file goes where it was asked for as it is.
                await source.CopyAndReplaceAsync(file);
            }
            else if (ExportsGif)
            {
                note = await WriteGifAsync(source, file);
            }
            else if (_zoom is { IsFlat: false } zoom)
            {
                note = await WriteZoomedMp4Async(source, file, zoom);
            }
            else
            {
                await WriteMp4Async(source, file);
            }

            _exported = file.Path;
            StatusText.Text = L("Saved to %@").Replace("%@", file.Path, StringComparison.Ordinal)
                + (note is null ? string.Empty : "  ·  " + note);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or InvalidOperationException or COMException)
        {
            StatusText.Text = L("Export failed") + ": " + error.Message;
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Writes the GIF, and returns whatever the caller has to say about it beyond where
    /// it went — or null when there is nothing to add.
    /// </summary>
    private async Task<string?> WriteGifAsync(StorageFile source, StorageFile destination)
    {
        var (width, height) = SizeForExport();
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("This recording does not say what size its frames are.");
        }

        // Reported as it goes: a GIF is written frame by frame, and a minute of recording
        // is hundreds of seeks. A bar that does not move reads as one that has stopped.
        var progress = new Progress<double>(done =>
            StatusText.Text = L("Exporting...") + $" {done:P0}");

        var result = await GifExporter.WriteAsync(
            source,
            destination,
            _trim,
            width,
            height,
            GifFrameRate,
            progress);

        // Said rather than left to be noticed: a truncated GIF ends before the piece that
        // was asked for does, and a file that quietly stops early is worse than one that
        // says why it did. Not through L, since macshot has no string for a limit it does
        // not have.
        return result.Truncated
            ? $"stopped after {result.Frames} frames, which is as long as a GIF goes here"
            : null;
    }

    /// <summary>
    /// Writes the MP4 with the band's zoom applied, and returns what the caller has to say
    /// about it beyond where it went.
    /// </summary>
    /// <remarks>
    /// A separate path from <see cref="WriteMp4Async"/> rather than the same one with an
    /// effect switched on, because the two do genuinely different work:
    /// <see cref="MediaComposition"/> hands the file to the platform and lets it re-encode,
    /// while <see cref="ZoomVideoCompositor"/> decodes every frame, magnifies it here and
    /// encodes it again. The second is several times slower, so a recording with no zoom on
    /// it must not be made to pay for one.
    /// </remarks>
    private async Task<string?> WriteZoomedMp4Async(
        StorageFile source,
        StorageFile destination,
        VideoZoomSegment zoom)
    {
        var (width, height) = SizeForExport();
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("This recording does not say what size its frames are.");
        }

        // Reported as it goes, as the GIF export is: a zoom export seeks to every frame,
        // and a bar that does not move on a minute of recording reads as one that has
        // stopped.
        var progress = new Progress<double>(done =>
            StatusText.Text = L("Exporting...") + $" {done:P0}");

        await ZoomVideoCompositor.WriteAsync(
            source,
            destination,
            _trim,
            zoom,
            _sourceWidth,
            _sourceHeight,
            width,
            height,
            FrameRate,
            VideoExportPlan.Bitrate(width, height, FrameRate, ExportQuality),
            progress);

        // Said rather than left to be discovered on playback. The compositor builds the
        // file out of frames it decoded itself, and carrying the recording's audio through
        // that would mean demuxing and re-muxing a track nothing else here touches. Not
        // through L, since macshot has no string for a limit it does not have.
        return "the zoom export carries no audio";
    }

    private async Task WriteMp4Async(StorageFile source, StorageFile destination)
    {
        var clip = await MediaClip.CreateFromFileAsync(source);
        clip.TrimTimeFromStart = TimeSpan.FromSeconds(_trim.Start);
        clip.TrimTimeFromEnd = TimeSpan.FromSeconds(Math.Max(0, _duration - _trim.End));

        var composition = new MediaComposition();
        composition.Clips.Add(clip);

        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);
        var (width, height) = SizeForExport();

        if (width > 0 && height > 0)
        {
            profile.Video.Width = (uint)width;
            profile.Video.Height = (uint)height;
            profile.Video.Bitrate = (uint)VideoExportPlan.Bitrate(width, height, FrameRate, ExportQuality);
        }

        // Precise rather than the nearest key frame: a recording is trimmed to the moment
        // something happens, and key frames seconds apart put the cut on the wrong side
        // of it.
        var reason = await composition.RenderToFileAsync(destination, MediaTrimmingPreference.Precise, profile);

        if (reason != TranscodeFailureReason.None)
        {
            throw new InvalidOperationException($"Windows could not encode the recording ({reason}).");
        }
    }

    /// <summary>
    /// The size to export at, or nothing for a source that never said what size it is —
    /// in which case the encoder keeps whatever the frames turn out to be.
    /// </summary>
    private (int Width, int Height) SizeForExport() =>
        _sourceWidth > 0 && _sourceHeight > 0
            ? VideoExportPlan.Scaled(_sourceWidth, _sourceHeight, ExportPercent)
            : (0, 0);

    /// <summary>
    /// Puts the buttons that would start a second export out of reach while one is
    /// running: two of them writing the same file would race.
    /// </summary>
    private void SetBusy(bool busy)
    {
        _busy = busy;

        if (busy)
        {
            SaveButton.IsEnabled = false;
            SaveAsButton.IsEnabled = false;
            UploadButton.IsEnabled = false;
            StatusText.Text = L("Exporting...");
        }
        else
        {
            ShowExportChoices();
        }
    }

    // Window

    private void PlaceOnScreen()
    {
        var monitor = MonitorEnumerator.Enumerate().Layout.Primary;
        var work = monitor.WorkArea;

        // The picture at its own size where it fits, and never more than four fifths of
        // the work area — a recording of a whole screen is by definition too large to
        // open at full size on that screen.
        var controls = ControlsHeightDips * monitor.Scale;
        var pictureWidth = _sourceWidth > 0 ? _sourceWidth : MinWidthDips * monitor.Scale;
        var pictureHeight = _sourceHeight > 0
            ? _sourceHeight
            : (MinHeightDips - ControlsHeightDips) * monitor.Scale;

        var fit = Math.Min(
            1,
            Math.Min(
                work.Width * 0.8 / pictureWidth,
                ((work.Height * 0.8) - controls) / pictureHeight));

        var width = (int)Math.Max(MinWidthDips * monitor.Scale, pictureWidth * fit);
        var height = (int)Math.Max(MinHeightDips * monitor.Scale, (pictureHeight * fit) + controls);

        this.GetAppWindow().MoveAndResize(new RectInt32(
            (int)(work.X + ((work.Width - width) / 2)),
            (int)(work.Y + ((work.Height - height) / 2)),
            width,
            height));
    }

    private void Teardown()
    {
        _ticker.Stop();
        Player.SetMediaPlayer(null);

        // Disposed rather than left to the finalizer: the player holds the file open, and
        // a recording that cannot be deleted because macshot is still reading it is a
        // failure the user meets in Explorer, where nothing explains it.
        _player?.Dispose();
        _player = null;

        _gif = null;
        GifView.Source = null;
    }

    private static string Bytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} bytes",
    };
}
