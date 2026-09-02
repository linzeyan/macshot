using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Core.Output;
using Macshot.Windows.Rendering;
using Macshot.Windows.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
using Windows.UI;
using WinRT.Interop;

namespace Macshot.Windows;

/// <summary>
/// The window a finished recording opens in: play it, keep part of it, put effects over
/// it, and write the result somewhere.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>VideoEditorWindowController</c>. Its bottom bar is reproduced control for
/// control: play and mute, the MP4/GIF toggle, what the source is, the dimensions, the
/// quality, the GIF frame rate, the estimate, and then Save, Save As, Upload, the folder
/// and Copy.
/// </para>
/// <para>
/// The effects band carries macshot's six: zoom, censor, cut, speed, freeze and text. What
/// each one <em>is</em> lives in Core — <see cref="VideoEffects"/> and the segment types
/// around it — and what an export made from them looks like is decided by
/// <see cref="VideoTimeline"/>. This file is the band: where the pills are drawn, what
/// dragging one does, and which of the three export paths the result needs.
/// </para>
/// <para>
/// <strong>Where the band differs from macshot's.</strong> macOS drives it entirely from
/// context menus — right-click the band to add, right-click a pill for everything it can
/// be set to. This port puts the same choices on a row beside the band instead: a picker
/// for what to add, an Add and a Delete, and one box that shows whatever the selected pill
/// has to set. The set of choices is the same; where they live is not. A caption's text
/// and its weight get a second row, which is up only while a caption is selected.
/// </para>
/// <para>
/// Trimming with nothing on the band goes through <see cref="MediaComposition"/>, which is
/// the platform's own editor, and so does a recording with only cuts on it — one clip per
/// surviving stretch. Anything else goes through <see cref="VideoEffectsCompositor"/>,
/// whose head explains at length why Windows needs a hand-built frame pipeline where macOS
/// has an <c>AVVideoComposition</c>.
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

    /// <summary>macshot's band metrics: a 22-tall row with 2 between rows.</summary>
    private const double RowHeight = 22;

    private const double RowGap = 2;

    private const double RowStride = RowHeight + RowGap;

    /// <summary>Clear above and below the stack, so a row's pill is not clipped.</summary>
    private const double BandInset = 2;

    /// <summary>
    /// How many rows the band grows to before it stops.
    /// </summary>
    /// <remarks>
    /// macshot's four, but scrolled rather than capped: past four rows its band scrolls
    /// inside a scroll view. This port stops instead, because a scroll view here would
    /// take a working band and make it depend on a nested-scrolling behaviour nothing in
    /// this session could photograph. Effects past the fourth row are still exported —
    /// what is lost is the pill, not the effect.
    /// </remarks>
    private const int MaxBandRows = 4;

    /// <summary>
    /// How wide a freeze's pill is drawn.
    /// </summary>
    /// <remarks>
    /// macshot's 62. A freeze is a single instant and has no width to map, so the pill is
    /// given one — a rectangle of no width could not be clicked on.
    /// </remarks>
    private const double FreezePillWidth = 62;

    /// <summary>The levels the box beside the band offers. macshot's range.</summary>
    private static readonly double[] ZoomLevels = [1.2, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0];

    /// <summary>The censor styles, in macshot's menu order.</summary>
    private static readonly VideoCensorStyle[] CensorStyles =
        [VideoCensorStyle.Solid, VideoCensorStyle.Pixelate, VideoCensorStyle.Blur];

    /// <summary>Which effect the picker offers, in macshot's menu order.</summary>
    private static readonly VideoEffectKind[] EffectKinds =
    [
        VideoEffectKind.Zoom,
        VideoEffectKind.Censor,
        VideoEffectKind.Cut,
        VideoEffectKind.Speed,
        VideoEffectKind.Freeze,
        VideoEffectKind.Text,
    ];

    private static readonly VideoTextAlignment[] CaptionAlignments =
        [VideoTextAlignment.Left, VideoTextAlignment.Centre, VideoTextAlignment.Right];

    /// <summary>macshot's pill colours, one per kind.</summary>
    /// <remarks>
    /// Taken from <c>EffectsBandView.draw</c> rather than chosen here: the colour is how a
    /// user tells a censor from a caption at a glance on a band four rows deep, and two
    /// products that disagreed about which is which would be worse than either.
    /// </remarks>
    private static Color PillColor(VideoEffectKind kind) => kind switch
    {
        VideoEffectKind.Zoom => Color.FromArgb(0xCC, 0x40, 0x8C, 0xFF),
        VideoEffectKind.Censor => Color.FromArgb(0xCC, 0xF2, 0x59, 0x59),
        VideoEffectKind.Cut => Color.FromArgb(0xCC, 0x2A, 0x2A, 0x2A),
        VideoEffectKind.Speed => Color.FromArgb(0xCC, 0x33, 0xA6, 0x99),
        VideoEffectKind.Freeze => Color.FromArgb(0xCC, 0x8C, 0x59, 0xD9),
        _ => Color.FromArgb(0xCC, 0xFF, 0xC7, 0x4D),
    };

    /// <summary>How often the playhead is moved along while something is playing.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(60);

    private static readonly List<VideoEditorWindow> Open = [];

    private readonly SettingsStore _settings;
    private readonly string _path;
    private readonly DispatcherQueueTimer _ticker;

    /// <summary>Everything on the band. See <see cref="VideoEffects"/>.</summary>
    private readonly VideoEffects _effects = new();

    /// <summary>Where each pill was drawn, so a press can be matched to one.</summary>
    private readonly List<BandPill> _pills = [];

    private MediaPlayer? _player;
    private BitmapImage? _gif;
    private bool _gifIsPlaying;

    private double _duration;
    private int _sourceWidth;
    private int _sourceHeight;
    private int _sourceFrameRate;
    private long _sourceBytes;

    /// <summary>Whether the recording has a track the export has to carry.</summary>
    private bool _sourceHasAudio;

    /// <summary>The percentages behind the dimensions menu's entries.</summary>
    private IReadOnlyList<int> _scales = [100];

    private VideoTrim _trim;
    private Handle _dragging;

    /// <summary>Which pill is selected, or nothing.</summary>
    /// <remarks>
    /// A kind and a position rather than macshot's UUID. The segments are values in lists,
    /// so there is no identity to carry; deleting one shifts what is behind it, which is
    /// why <see cref="Select"/> is the only thing that ever sets this.
    /// </remarks>
    private (VideoEffectKind Kind, int Index)? _selected;

    private PillGrab _pillDragging;

    /// <summary>Where in the pill the press landed, so a move does not jump.</summary>
    private double _pillGrabOffset;

    private RectGrab _rectDragging;

    /// <summary>Where in the rectangle the press landed, in the overlay's own units.</summary>
    private global::Windows.Foundation.Point _rectGrabOffset;

    /// <summary>How many rows deep the band was last drawn.</summary>
    private int _bandRows = 1;

    /// <summary>
    /// Whether the band's pills have to be built again.
    /// </summary>
    /// <remarks>
    /// The playhead is moved sixteen times a second while something is playing, and the
    /// band is redrawn from the same place. Rebuilding a dozen shapes and text blocks at
    /// that rate is work the band never needs: what a pill looks like changes when the
    /// effects change, when the selection moves, or when the window is resized, and
    /// nothing else on this timer touches any of the three.
    /// </remarks>
    private bool _bandDirty = true;

    /// <summary>The timeline width the pills were last measured against.</summary>
    private double _bandWidth;

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

    /// <summary>Which part of a pill a press took hold of.</summary>
    private enum PillGrab
    {
        None,
        Start,
        End,
        Body,
    }

    /// <summary>Which part of the rectangle on the picture a press took hold of.</summary>
    private enum RectGrab
    {
        None,
        Body,
        Corner,
    }

    /// <summary>Where a pill ended up, for hit-testing a press against it.</summary>
    private readonly record struct BandPill(
        VideoEffectKind Kind,
        int Index,
        double Left,
        double Right,
        int Row);

    public VideoEditorWindow(string path, SettingsStore settings)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        InitializeComponent();

        // Every string in the XAML is already the English text macshot keys by, so the
        // window is translated in place rather than written twice.
        this.Localize();
        this.GetAppWindow().UseAppIcon();
        this.CloseOnControlW();

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

    /// <summary>Which effect the Add button would place.</summary>
    private VideoEffectKind AddingKind =>
        EffectKindBox.SelectedIndex >= 0 && EffectKindBox.SelectedIndex < EffectKinds.Length
            ? EffectKinds[EffectKindBox.SelectedIndex]
            : VideoEffectKind.Zoom;

    /// <summary>Which kind the options box is currently showing choices for.</summary>
    private VideoEffectKind OptionKind => _selected?.Kind ?? AddingKind;

    /// <summary>How long the exported file will run, with everything on the band applied.</summary>
    private double OutputSeconds => _effects.OutputSeconds(_trim);

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
            StatusText.Text = L("macshot could not open that recording: {0}", error.Message);
        }
    }

    private async Task LoadVideoAsync(StorageFile file)
    {
        var properties = await file.Properties.GetVideoPropertiesAsync();
        _sourceWidth = (int)properties.Width;
        _sourceHeight = (int)properties.Height;
        _duration = properties.Duration.TotalSeconds;

        var probe = await ProbeAsync(file);
        _sourceFrameRate = probe.FrameRate;
        _sourceHasAudio = probe.HasAudio;

        _player = new MediaPlayer
        {
            Source = MediaSource.CreateFromStorageFile(file),
            AutoPlay = false,
        };

        Player.SetMediaPlayer(_player);
    }

    /// <summary>
    /// How many frames a second the source runs at, and whether it has any sound.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from the file rather than taken from the recording preference, because the
    /// file need not have been made by this copy of macshot — or by macshot at all — and
    /// a 60 fps recording encoded as though it were 30 comes out at half the bitrate it
    /// needs.
    /// </para>
    /// <para>
    /// The audio answer is read here rather than assumed for the same reason and one more:
    /// an effects export re-encodes a second time purely to carry the sound, and a silent
    /// recording that claimed to have some would pay for that pass to add nothing.
    /// </para>
    /// </remarks>
    private static async Task<(int FrameRate, bool HasAudio)> ProbeAsync(StorageFile file)
    {
        try
        {
            var profile = await MediaEncodingProfile.CreateFromFileAsync(file);
            var rate = profile.Video?.FrameRate;

            return (
                rate is { Denominator: > 0 }
                    ? (int)Math.Round(rate.Numerator / (double)rate.Denominator)
                    : RecordingPlan.DefaultFrameRate,
                profile.Audio is not null);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or COMException)
        {
            return (RecordingPlan.DefaultFrameRate, false);
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

            // Named through L because these are built after the page-wide pass has run,
            // which only reaches strings that were in the XAML.
            EffectKindBox.ItemsSource = EffectKinds.Select(kind => L(VideoEffectLabels.AddKey(kind))).ToList();
            EffectKindBox.SelectedIndex = 0;

            CaptionAlignBox.ItemsSource = new List<string> { L("Left"), L("Center"), L("Right") };
            CaptionAlignBox.SelectedIndex = 1;

            SourceInfoText.Text = _sourceWidth > 0
                ? $"{Bytes(_sourceBytes)}  ·  {_sourceWidth} × {_sourceHeight}"
                : Bytes(_sourceBytes);
        }
        finally
        {
            _filling = false;
        }

        FillOptions();
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

        AddButton.Content = L(VideoEffectLabels.AddKey(AddingKind));
        DeleteButton.Content = L(VideoEffectLabels.DeleteKey(_selected?.Kind ?? AddingKind));
        DeleteButton.IsEnabled = _selected is not null;

        // A cut has nothing to set: its whole statement is where it starts and where it
        // ends, both of which are the pill. An empty box beside it would look broken.
        OptionBox.Visibility = OptionKind is VideoEffectKind.Cut
            ? Visibility.Collapsed
            : Visibility.Visible;

        CaptionPanel.Visibility = _selected?.Kind is VideoEffectKind.Text
            ? Visibility.Visible
            : Visibility.Collapsed;

        ShowRectOverlay();

        var estimated = editable
            && _sourceWidth > 0
            && VideoExportPlan.ShowsEstimate(OutputSeconds, _duration, ExportPercent, ExportQuality, gif);

        EstimateText.Text = estimated
            ? "~" + Bytes(VideoExportPlan.EstimatedBytes(
                _sourceBytes,
                OutputSeconds,
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
            .Replace("%@", VideoTrim.Format(OutputSeconds), StringComparison.Ordinal);

        DrawEffectsBand();
        DrawRectOverlay();
    }

    // Effects band

    /// <summary>
    /// Draws a pill for every segment on the band, stacked into rows where two of them
    /// run at the same moment.
    /// </summary>
    /// <remarks>
    /// Measured against the timeline rather than against the band, though the two are the
    /// same width. What a band is for is that a pill sits under the moment it applies to,
    /// and two widths that could drift apart would eventually put it under a different one.
    /// </remarks>
    private void DrawEffectsBand()
    {
        if (!_bandDirty && Math.Abs(_bandWidth - Timeline.ActualWidth) < 0.5)
        {
            return;
        }

        _bandDirty = false;
        _bandWidth = Timeline.ActualWidth;

        _pills.Clear();
        EffectsBand.Children.Clear();
        EffectsBand.Children.Add(BandHintText);

        BandHintText.Visibility = _effects.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(BandHintText, 8);

        if (_duration <= 0 || Timeline.ActualWidth <= 0)
        {
            return;
        }

        var placed = Placements();
        var rows = VideoBandRows.Assign(placed.Select(pill => pill.Span).ToList());
        var rowCount = Math.Min(MaxBandRows, VideoBandRows.RowCount(rows));

        _bandRows = rowCount;
        EffectsBand.Height = (rowCount * RowStride) - RowGap + (BandInset * 2);

        for (var index = 0; index < placed.Count; index++)
        {
            var row = rows[index];
            if (row >= rowCount)
            {
                // Past the last row the band grows to. The effect still exports; what is
                // missing is somewhere to draw its pill.
                continue;
            }

            var pill = placed[index];
            var left = pill.Kind is VideoEffectKind.Freeze
                ? Math.Clamp(
                    XFor(pill.Span.Start) - (FreezePillWidth / 2),
                    0,
                    Math.Max(0, Timeline.ActualWidth - FreezePillWidth))
                : XFor(pill.Span.Start);

            var width = pill.Kind is VideoEffectKind.Freeze
                ? FreezePillWidth
                : Math.Max(2, XFor(pill.Span.End) - left);

            // Row 0 is the bottom one, as it is on macOS, so a band that grows does so
            // upwards and the pill already placed does not move under the pointer.
            var top = BandInset + ((rowCount - 1 - row) * RowStride);

            AddPill(pill, left, width, top);
            _pills.Add(new BandPill(pill.Kind, pill.Index, left, left + width, row));
        }
    }

    /// <summary>Every segment on the band, in the order macshot draws them.</summary>
    /// <remarks>
    /// Cuts last so they sit over the others in their row, which is macshot's order and
    /// its reason: a cut removes what the pills beside it would have applied to, and
    /// seeing it on top says so.
    /// </remarks>
    private List<Placement> Placements()
    {
        var placed = new List<Placement>(_effects.Count);

        for (var index = 0; index < _effects.Zooms.Count; index++)
        {
            placed.Add(new Placement(
                VideoEffectKind.Zoom,
                index,
                _effects.Zooms[index].Span,
                VideoEffectLabels.Zoom(_effects.Zooms[index].Level)));
        }

        for (var index = 0; index < _effects.Censors.Count; index++)
        {
            placed.Add(new Placement(
                VideoEffectKind.Censor,
                index,
                _effects.Censors[index].Span,
                L(VideoEffectLabels.StyleKey(_effects.Censors[index].Style))));
        }

        for (var index = 0; index < _effects.Texts.Count; index++)
        {
            placed.Add(new Placement(
                VideoEffectKind.Text,
                index,
                _effects.Texts[index].Span,
                Shortened(_effects.Texts[index].Text)));
        }

        for (var index = 0; index < _effects.Speeds.Count; index++)
        {
            placed.Add(new Placement(
                VideoEffectKind.Speed,
                index,
                _effects.Speeds[index].Span,
                VideoEffectLabels.Speed(_effects.Speeds[index].Factor)));
        }

        for (var index = 0; index < _effects.Freezes.Count; index++)
        {
            var freeze = _effects.Freezes[index];
            placed.Add(new Placement(
                VideoEffectKind.Freeze,
                index,
                new VideoTimeRange(freeze.At, freeze.At),
                VideoEffectLabels.Freeze(freeze.Hold)));
        }

        for (var index = 0; index < _effects.Cuts.Count; index++)
        {
            placed.Add(new Placement(
                VideoEffectKind.Cut,
                index,
                _effects.Cuts[index].Span,
                VideoEffectLabels.Cut(_effects.Cuts[index].Duration)));
        }

        return placed;
    }

    /// <summary>What a caption's pill says. macshot's eighteen characters and an ellipsis.</summary>
    private static string Shortened(string text)
    {
        var line = text.Replace('\n', ' ').Replace('\r', ' ');

        return line.Length > 18 ? string.Concat(line.AsSpan(0, 18), "…") : line;
    }

    private void AddPill(Placement pill, double left, double width, double top)
    {
        var selected = _selected is { } chosen && chosen.Kind == pill.Kind && chosen.Index == pill.Index;

        // Qualified rather than imported: Microsoft.UI.Xaml.Shapes also declares Path,
        // which would make every System.IO.Path call in this file ambiguous.
        var body = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Width = width,
            Height = RowHeight,
            RadiusX = 5,
            RadiusY = 5,
            Fill = new SolidColorBrush(PillColor(pill.Kind)),

            // A ring rather than a lift or a glow, because the band is flat and a pill
            // that grew when selected would move its neighbours.
            Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = selected ? 2 : 0,
        };

        Canvas.SetLeft(body, left);
        Canvas.SetTop(body, top);
        EffectsBand.Children.Add(body);

        var label = new TextBlock
        {
            Text = pill.Label,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),

            // The pill underneath is what a press is matched against, and a label that
            // took the press would make the middle of a wide pill undraggable.
            IsHitTestVisible = false,
        };

        // Inside the pill where it fits, and out of the way rather than clipped where it
        // does not: a short effect on a long recording is a few pixels wide, and a label
        // painted over it would be unreadable in either place.
        Canvas.SetLeft(label, width >= 44 ? left + 6 : left + width + 6);
        Canvas.SetTop(label, top + 3);
        EffectsBand.Children.Add(label);
    }

    /// <summary>One pill's worth of what the band has to draw.</summary>
    private readonly record struct Placement(
        VideoEffectKind Kind,
        int Index,
        VideoTimeRange Span,
        string Label);

    private void EffectKind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_filling)
        {
            return;
        }

        // Picking a different kind to add is also how a selection is let go: the options
        // box can only show one kind's choices, and leaving a pill selected while the box
        // described a different kind would make the next change land somewhere unexpected.
        Select(null);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_duration <= 0)
        {
            return;
        }

        // At the playhead, which is where the user is looking. macshot places an effect
        // where the band was right-clicked; this port has no band menu, so the playhead is
        // the equivalent statement of where.
        var at = _player?.PlaybackSession.Position.TotalSeconds ?? _trim.Start;
        var kind = AddingKind;

        if (_effects.GapFor(kind, at, _duration) is not { } gap)
        {
            StatusText.Text = L("Not enough room here");
            return;
        }

        switch (kind)
        {
            case VideoEffectKind.Zoom:
                if (gap.Duration < VideoZoomSegment.MinDuration)
                {
                    StatusText.Text = L("Not enough room here");
                    return;
                }

                _effects.Zooms.Add(VideoZoomSegment.Placed(at, gap).WithLevel(SelectedZoomLevel));
                Select((kind, _effects.Zooms.Count - 1));
                break;

            case VideoEffectKind.Censor:
                _effects.Censors.Add(VideoCensorSegment.Placed(at, _duration, SelectedCensorStyle));
                Select((kind, _effects.Censors.Count - 1));
                break;

            case VideoEffectKind.Cut:
                _effects.Cuts.Add(VideoCutSegment.Placed(at, _duration));
                Select((kind, _effects.Cuts.Count - 1));
                break;

            case VideoEffectKind.Speed:
                if (gap.Duration < VideoSpeedSegment.MinSourceDuration(SelectedSpeedFactor))
                {
                    StatusText.Text = L("Not enough room here");
                    return;
                }

                _effects.Speeds.Add(VideoSpeedSegment.Placed(at, gap, SelectedSpeedFactor));
                Select((kind, _effects.Speeds.Count - 1));
                break;

            case VideoEffectKind.Freeze:
                _effects.Freezes.Add(VideoFreezeSegment.Placed(at, _duration, SelectedFreezeHold));
                Select((kind, _effects.Freezes.Count - 1));
                break;

            default:
                _effects.Texts.Add(VideoTextSegment.Placed(at, _duration));
                Select((kind, _effects.Texts.Count - 1));
                break;
        }

        Touched();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } chosen)
        {
            return;
        }

        _effects.Remove(chosen.Kind, chosen.Index);
        Select(null);
        Touched();
    }

    /// <summary>
    /// Selects a pill, and points the options row at whatever that pill can be set to.
    /// </summary>
    private void Select((VideoEffectKind Kind, int Index)? pill)
    {
        _selected = pill;
        _bandDirty = true;
        FillOptions();
        FillCaption();
        ShowExportChoices();
        DrawTimeline();
    }

    /// <summary>
    /// Fills the box beside the band with the choices for the kind it is describing.
    /// </summary>
    /// <remarks>
    /// The selected pill's kind when there is one, and otherwise the kind Add would place —
    /// so that the box doubles as the setting a new effect is created with, which is what
    /// the zoom level box already did before there was more than one kind of effect.
    /// </remarks>
    private void FillOptions()
    {
        // Read into a local once. The nullable analyser will not carry "this has a value"
        // from a test into a .Value on a field, so every use below would otherwise have to
        // repeat the test — see the CS8629 row in CLAUDE.md.
        var chosen = _selected;

        _filling = true;
        try
        {
            switch (OptionKind)
            {
                case VideoEffectKind.Zoom:
                    OptionBox.ItemsSource = ZoomLevels.Select(VideoEffectLabels.Zoom).ToList();
                    OptionBox.SelectedIndex = Nearest(
                        ZoomLevels,
                        chosen is { Kind: VideoEffectKind.Zoom, Index: var zoom }
                            ? _effects.Zooms[zoom].Level
                            : VideoZoomSegment.DefaultLevel);
                    break;

                case VideoEffectKind.Censor:
                    OptionBox.ItemsSource = CensorStyles
                        .Select(style => L(VideoEffectLabels.StyleKey(style)))
                        .ToList();
                    OptionBox.SelectedIndex = Array.IndexOf(
                        CensorStyles,
                        chosen is { Kind: VideoEffectKind.Censor, Index: var censor }
                            ? _effects.Censors[censor].Style
                            : VideoCensorStyle.Blur);
                    break;

                case VideoEffectKind.Speed:
                    OptionBox.ItemsSource = VideoSpeedSegment.PresetFactors
                        .Select(VideoEffectLabels.Speed)
                        .ToList();
                    OptionBox.SelectedIndex = Nearest(
                        VideoSpeedSegment.PresetFactors,
                        chosen is { Kind: VideoEffectKind.Speed, Index: var speed }
                            ? _effects.Speeds[speed].Factor
                            : VideoSpeedSegment.DefaultFactor);
                    break;

                case VideoEffectKind.Freeze:
                    OptionBox.ItemsSource = VideoFreezeSegment.PresetHolds
                        .Select(VideoEffectLabels.Freeze)
                        .ToList();
                    OptionBox.SelectedIndex = Nearest(
                        VideoFreezeSegment.PresetHolds,
                        chosen is { Kind: VideoEffectKind.Freeze, Index: var freeze }
                            ? _effects.Freezes[freeze].Hold
                            : VideoFreezeSegment.DefaultHold);
                    break;

                case VideoEffectKind.Text:
                    OptionBox.ItemsSource = VideoTextSegment.PresetFontSizes
                        .Select(preset => L(preset.Name))
                        .ToList();
                    OptionBox.SelectedIndex = Nearest(
                        VideoTextSegment.PresetFontSizes.Select(preset => preset.Size).ToList(),
                        chosen is { Kind: VideoEffectKind.Text, Index: var text }
                            ? _effects.Texts[text].FontSize
                            : VideoTextSegment.DefaultFontSize);
                    break;

                default:
                    // A cut has nothing to set. The box is emptied as well as hidden, so a
                    // stale list cannot be read back by the next selection.
                    OptionBox.ItemsSource = new List<string>();
                    break;
            }
        }
        finally
        {
            _filling = false;
        }
    }

    /// <summary>
    /// Which entry of <paramref name="choices"/> is nearest <paramref name="value"/>.
    /// </summary>
    /// <remarks>
    /// Nearest rather than exact, because a value can arrive from somewhere the list does
    /// not contain — a segment widened to fit a factor, say. An exact match that failed
    /// would leave the box blank, which reads as a control that has lost its setting.
    /// </remarks>
    private static int Nearest(IReadOnlyList<double> choices, double value)
    {
        var best = 0;
        for (var index = 1; index < choices.Count; index++)
        {
            if (Math.Abs(choices[index] - value) < Math.Abs(choices[best] - value))
            {
                best = index;
            }
        }

        return best;
    }

    private double SelectedZoomLevel => OptionKind is VideoEffectKind.Zoom
        && OptionBox.SelectedIndex >= 0
        && OptionBox.SelectedIndex < ZoomLevels.Length
            ? ZoomLevels[OptionBox.SelectedIndex]
            : VideoZoomSegment.DefaultLevel;

    private VideoCensorStyle SelectedCensorStyle => OptionKind is VideoEffectKind.Censor
        && OptionBox.SelectedIndex >= 0
        && OptionBox.SelectedIndex < CensorStyles.Length
            ? CensorStyles[OptionBox.SelectedIndex]
            : VideoCensorStyle.Blur;

    private double SelectedSpeedFactor => OptionKind is VideoEffectKind.Speed
        && OptionBox.SelectedIndex >= 0
        && OptionBox.SelectedIndex < VideoSpeedSegment.PresetFactors.Count
            ? VideoSpeedSegment.PresetFactors[OptionBox.SelectedIndex]
            : VideoSpeedSegment.DefaultFactor;

    private double SelectedFreezeHold => OptionKind is VideoEffectKind.Freeze
        && OptionBox.SelectedIndex >= 0
        && OptionBox.SelectedIndex < VideoFreezeSegment.PresetHolds.Count
            ? VideoFreezeSegment.PresetHolds[OptionBox.SelectedIndex]
            : VideoFreezeSegment.DefaultHold;

    private double SelectedFontSize => OptionKind is VideoEffectKind.Text
        && OptionBox.SelectedIndex >= 0
        && OptionBox.SelectedIndex < VideoTextSegment.PresetFontSizes.Count
            ? VideoTextSegment.PresetFontSizes[OptionBox.SelectedIndex].Size
            : VideoTextSegment.DefaultFontSize;

    private void Option_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _selected is not { } chosen)
        {
            return;
        }

        switch (chosen.Kind)
        {
            case VideoEffectKind.Zoom:
                _effects.Zooms[chosen.Index] = _effects.Zooms[chosen.Index].WithLevel(SelectedZoomLevel);
                break;

            case VideoEffectKind.Censor:
                _effects.Censors[chosen.Index] = _effects.Censors[chosen.Index].WithStyle(SelectedCensorStyle);
                break;

            case VideoEffectKind.Speed:
                _effects.Speeds[chosen.Index] =
                    _effects.Speeds[chosen.Index].WithFactor(SelectedSpeedFactor, _duration);
                break;

            case VideoEffectKind.Freeze:
                _effects.Freezes[chosen.Index] = _effects.Freezes[chosen.Index].WithHold(SelectedFreezeHold);
                break;

            case VideoEffectKind.Text:
                _effects.Texts[chosen.Index] = _effects.Texts[chosen.Index] with { FontSize = SelectedFontSize };
                break;

            default:
                return;
        }

        Touched();
    }

    /// <summary>Puts the selected caption's own text and weight into the row below.</summary>
    private void FillCaption()
    {
        if (_selected is not { Kind: VideoEffectKind.Text, Index: var index })
        {
            return;
        }

        _filling = true;
        try
        {
            var caption = _effects.Texts[index];
            CaptionBox.Text = caption.Text;
            CaptionBoldBox.IsChecked = caption.Bold;
            CaptionItalicBox.IsChecked = caption.Italic;
            CaptionAlignBox.SelectedIndex = Array.IndexOf(CaptionAlignments, caption.Alignment);
        }
        finally
        {
            _filling = false;
        }
    }

    private void Caption_Changed(object sender, TextChangedEventArgs e)
    {
        if (_filling || _selected is not { Kind: VideoEffectKind.Text, Index: var index })
        {
            return;
        }

        // Not through WithText, which refuses an empty caption: this fires on every
        // keystroke, and rejecting the moment the field is cleared would put the old text
        // back under the cursor. The refusal happens when the caption is rasterized.
        _effects.Texts[index] = _effects.Texts[index] with
        {
            Text = CaptionBox.Text,
        };

        Touched();
    }

    private void CaptionStyle_Changed(object sender, RoutedEventArgs e)
    {
        if (_filling || _selected is not { Kind: VideoEffectKind.Text, Index: var index })
        {
            return;
        }

        var alignment = CaptionAlignBox.SelectedIndex >= 0
            && CaptionAlignBox.SelectedIndex < CaptionAlignments.Length
                ? CaptionAlignments[CaptionAlignBox.SelectedIndex]
                : VideoTextAlignment.Centre;

        _effects.Texts[index] = _effects.Texts[index] with
        {
            Bold = CaptionBoldBox.IsChecked is true,
            Italic = CaptionItalicBox.IsChecked is true,
            Alignment = alignment,
        };

        Touched();
    }

    private void EffectsBand_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(EffectsBand).Position;
        var hit = PillAt(point);

        if (hit is not { } pill)
        {
            // A press on bare band lets the selection go, which is the only way to get the
            // options row back to describing what Add would place.
            Select(null);
            return;
        }

        Select((pill.Kind, pill.Index));

        _pillDragging = pill.Kind is VideoEffectKind.Freeze
            // A freeze is an instant: there is nothing to resize, only somewhere to put it.
            ? PillGrab.Body
            : point.X <= pill.Left + PillEdgeGrab
                ? PillGrab.Start
                : point.X >= pill.Right - PillEdgeGrab
                    ? PillGrab.End
                    : PillGrab.Body;

        _pillGrabOffset = SecondsFor(point.X) - _effects.SpanOf(pill.Kind, pill.Index).Start;
        EffectsBand.CapturePointer(e.Pointer);
    }

    /// <summary>Which pill a press landed on, or nothing.</summary>
    /// <remarks>
    /// Searched from the end, so the pill drawn last — a cut, which is drawn over its
    /// row-mates deliberately — is the one a press on the overlap takes hold of.
    /// </remarks>
    private BandPill? PillAt(global::Windows.Foundation.Point point)
    {
        for (var index = _pills.Count - 1; index >= 0; index--)
        {
            var pill = _pills[index];
            var top = BandInset + ((_bandRows - 1 - pill.Row) * RowStride);

            if (point.Y >= top
                && point.Y <= top + RowHeight
                && point.X >= pill.Left - PillEdgeGrab
                && point.X <= pill.Right + PillEdgeGrab)
            {
                return pill;
            }
        }

        return null;
    }

    private void EffectsBand_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pillDragging is PillGrab.None || _selected is not { } chosen)
        {
            return;
        }

        var seconds = SecondsFor(e.GetCurrentPoint(EffectsBand).Position.X);
        var moved = seconds - _pillGrabOffset;

        switch (chosen.Kind)
        {
            case VideoEffectKind.Zoom:
                _effects.Zooms[chosen.Index] = _pillDragging switch
                {
                    PillGrab.Start => _effects.Zooms[chosen.Index].WithStart(seconds, _duration),
                    PillGrab.End => _effects.Zooms[chosen.Index].WithEnd(seconds, _duration),
                    _ => _effects.Zooms[chosen.Index].MovedTo(moved, _duration),
                };
                break;

            case VideoEffectKind.Censor:
                _effects.Censors[chosen.Index] = _pillDragging switch
                {
                    PillGrab.Start => _effects.Censors[chosen.Index].WithStart(seconds, _duration),
                    PillGrab.End => _effects.Censors[chosen.Index].WithEnd(seconds, _duration),
                    _ => _effects.Censors[chosen.Index].MovedTo(moved, _duration),
                };
                break;

            case VideoEffectKind.Cut:
                _effects.Cuts[chosen.Index] = _pillDragging switch
                {
                    PillGrab.Start => _effects.Cuts[chosen.Index].WithStart(seconds, _duration),
                    PillGrab.End => _effects.Cuts[chosen.Index].WithEnd(seconds, _duration),
                    _ => _effects.Cuts[chosen.Index].MovedTo(moved, _duration),
                };
                break;

            case VideoEffectKind.Speed:
                _effects.Speeds[chosen.Index] = _pillDragging switch
                {
                    PillGrab.Start => _effects.Speeds[chosen.Index].WithStart(seconds, _duration),
                    PillGrab.End => _effects.Speeds[chosen.Index].WithEnd(seconds, _duration),
                    _ => _effects.Speeds[chosen.Index].MovedTo(moved, _duration),
                };
                break;

            case VideoEffectKind.Freeze:
                // From where the press landed, like every other kind: the pill is 62 wide
                // around an instant that has no width, so taking hold of its edge and
                // having the instant jump under the pointer would be the visible result.
                _effects.Freezes[chosen.Index] = _effects.Freezes[chosen.Index].MovedTo(moved, _duration);
                break;

            default:
                _effects.Texts[chosen.Index] = _pillDragging switch
                {
                    PillGrab.Start => _effects.Texts[chosen.Index].WithStart(seconds, _duration),
                    PillGrab.End => _effects.Texts[chosen.Index].WithEnd(seconds, _duration),
                    _ => _effects.Texts[chosen.Index].MovedTo(moved, _duration),
                };
                break;
        }

        _exported = null;
        _bandDirty = true;
        DrawTimeline();
    }

    private void EffectsBand_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EffectsBand.ReleasePointerCapture(e.Pointer);
        EndPillDrag();
    }

    /// <summary>
    /// Ends the drag when the pointer is taken away rather than let go, for the same
    /// reason <see cref="Timeline_PointerCaptureLost"/> does.
    /// </summary>
    private void EffectsBand_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndPillDrag();

    private void EndPillDrag()
    {
        if (_pillDragging is PillGrab.None)
        {
            return;
        }

        _pillDragging = PillGrab.None;
        Touched();
    }

    // The rectangle on the picture

    private void ShowRectOverlay() =>
        RectOverlay.Visibility =
            (_selected?.Kind is VideoEffectKind.Censor or VideoEffectKind.Text) && !SourceIsGif
                ? Visibility.Visible
                : Visibility.Collapsed;

    private void RectOverlay_SizeChanged(object sender, SizeChangedEventArgs e) => DrawRectOverlay();

    /// <summary>Where the selected censor or caption's rectangle is, or nothing.</summary>
    private CaptureRegion? SelectedRect => _selected switch
    {
        { Kind: VideoEffectKind.Censor, Index: var index } => _effects.Censors[index].Rect,
        { Kind: VideoEffectKind.Text, Index: var index } => _effects.Texts[index].Rect,
        _ => null,
    };

    /// <summary>Where the picture actually is inside the overlay, bars excluded.</summary>
    private CaptureRegion Letterbox => VideoOverlayGeometry.Letterbox(
        RectOverlay.ActualWidth,
        RectOverlay.ActualHeight,
        _sourceWidth,
        _sourceHeight);

    private void DrawRectOverlay()
    {
        if (RectOverlay.Visibility is not Visibility.Visible || SelectedRect is not { } normalized)
        {
            return;
        }

        var box = Letterbox;
        if (box.Width <= 0)
        {
            return;
        }

        var drawn = VideoOverlayGeometry.Denormalize(normalized, box);

        RectFrame.Width = Math.Max(1, drawn.Width);
        RectFrame.Height = Math.Max(1, drawn.Height);
        Canvas.SetLeft(RectFrame, drawn.X);
        Canvas.SetTop(RectFrame, drawn.Y);

        Canvas.SetLeft(RectHandle, drawn.Right - RectHandle.Width);
        Canvas.SetTop(RectHandle, drawn.Bottom - RectHandle.Height);
    }

    private void RectOverlay_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (SelectedRect is not { } normalized)
        {
            return;
        }

        var box = Letterbox;
        var drawn = VideoOverlayGeometry.Denormalize(normalized, box);
        var point = e.GetCurrentPoint(RectOverlay).Position;

        // The corner first, because it overlaps the body and resizing is what a press
        // there is asking for.
        if (Math.Abs(point.X - drawn.Right) <= HandleGrab && Math.Abs(point.Y - drawn.Bottom) <= HandleGrab)
        {
            _rectDragging = RectGrab.Corner;
        }
        else if (drawn.Contains(point.X, point.Y))
        {
            _rectDragging = RectGrab.Body;
            _rectGrabOffset = new global::Windows.Foundation.Point(point.X - drawn.X, point.Y - drawn.Y);
        }
        else
        {
            return;
        }

        RectOverlay.CapturePointer(e.Pointer);
    }

    private void RectOverlay_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_rectDragging is RectGrab.None || SelectedRect is not { } normalized)
        {
            return;
        }

        var box = Letterbox;
        if (box.Width <= 0 || box.Height <= 0)
        {
            return;
        }

        var drawn = VideoOverlayGeometry.Denormalize(normalized, box);
        var point = e.GetCurrentPoint(RectOverlay).Position;

        var moved = _rectDragging is RectGrab.Corner
            ? new CaptureRegion(
                drawn.X,
                drawn.Y,
                Math.Max(1, point.X - drawn.X),
                Math.Max(1, point.Y - drawn.Y))
            : new CaptureRegion(
                point.X - _rectGrabOffset.X,
                point.Y - _rectGrabOffset.Y,
                drawn.Width,
                drawn.Height);

        var back = VideoOverlayGeometry.Normalize(moved, box);

        if (_selected is { Kind: VideoEffectKind.Censor, Index: var censor })
        {
            _effects.Censors[censor] = _effects.Censors[censor].WithRect(back);
        }
        else if (_selected is { Kind: VideoEffectKind.Text, Index: var text })
        {
            _effects.Texts[text] = _effects.Texts[text].WithRect(back);
        }

        _exported = null;
        DrawRectOverlay();
    }

    private void RectOverlay_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        RectOverlay.ReleasePointerCapture(e.Pointer);
        EndRectDrag();
    }

    private void RectOverlay_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndRectDrag();

    private void EndRectDrag()
    {
        if (_rectDragging is RectGrab.None)
        {
            return;
        }

        _rectDragging = RectGrab.None;
        Touched();
    }

    /// <summary>
    /// What every change to the band does: the file already written no longer matches it,
    /// and the bar has to be redrawn around the new length.
    /// </summary>
    private void Touched()
    {
        _exported = null;
        _bandDirty = true;
        ShowExportChoices();
        DrawTimeline();
    }

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
            || _effects.ChangesAnything
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
            else if (_effects.NeedsFramePipeline)
            {
                note = await WriteEffectsMp4Async(source, file);
            }
            else if (_effects.HasCuts)
            {
                await WriteCutMp4Async(source, file);
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
            // The type when there is no message, because several of the WinRT calls an
            // export makes throw a COMException carrying nothing but an HRESULT — and
            // "Export failed:" with an empty half is a report nobody can act on. Logged
            // as well as shown: the status line is one line and is gone with the window.
            var reason = string.IsNullOrWhiteSpace(error.Message)
                ? $"{error.GetType().Name} 0x{error.HResult:X8}"
                : error.Message;

            // The whole exception to the log, one line of it to the window: an HRESULT is
            // what a report of this has to carry, and the call it came out of is what
            // makes it findable.
            DiagnosticLog.Write($"Export failed: {error}");
            StatusText.Text = L("Export failed") + ": " + reason;
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

        var result = await GifExporter.WriteAsync(
            source,
            destination,
            _trim,
            width,
            height,
            GifFrameRate,
            ExportProgress());

        // Said rather than left to be noticed. Not through L, since macshot has no string
        // for either of these limits, neither of which it has.
        var notes = new List<string>(2);

        // A truncated GIF ends before the piece that was asked for does, and a file that
        // quietly stops early is worse than one that says why it did.
        if (result.Truncated)
        {
            notes.Add($"stopped after {result.Frames} frames, which is as long as a GIF goes here");
        }

        // The band is taken off the window while GIF is chosen, but what is on it survives
        // the format being switched back and forth. A GIF is written straight out of the
        // composition by GifExporter, which knows nothing about effects, so an export that
        // said nothing here would look like one that had applied them.
        if (_effects.ChangesAnything)
        {
            notes.Add("the effects band is not applied to a GIF");
        }

        return notes.Count == 0 ? null : string.Join("  ·  ", notes);
    }

    /// <summary>
    /// Writes the MP4 with the band applied, and returns what the caller has to say about
    /// it beyond where it went.
    /// </summary>
    /// <remarks>
    /// A separate path from <see cref="WriteMp4Async"/> and from
    /// <see cref="WriteCutMp4Async"/> rather than one path with effects switched on,
    /// because the three do genuinely different work: <see cref="MediaComposition"/> hands
    /// the file to the platform and lets it re-encode, while
    /// <see cref="VideoEffectsCompositor"/> decodes every frame, draws on it here, and
    /// encodes it again. The last is several times slower, so a recording that does not
    /// need it must not be made to pay for it.
    /// </remarks>
    private async Task<string?> WriteEffectsMp4Async(StorageFile source, StorageFile destination)
    {
        var (width, height) = SizeForExport();
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("This recording does not say what size its frames are.");
        }

        var carried = await VideoEffectsCompositor.WriteAsync(
            source,
            destination,
            _trim,
            _effects,
            await CaptionsAsync(width, height),
            _duration,
            _sourceWidth,
            _sourceHeight,
            width,
            height,
            FrameRate,
            VideoExportPlan.Bitrate(width, height, FrameRate, ExportQuality),
            _sourceHasAudio,
            ExportProgress());

        // Only when the recording had sound and it did not survive, which now means the
        // machine would not decode the track rather than that macshot cannot re-time one.
        // Not through L, since macshot has no string for a failure it does not have.
        return _sourceHasAudio && !carried
            ? "this machine would not decode the recording's audio, so the export has none"
            : null;
    }

    /// <summary>
    /// Rasterizes every caption on the band at the size the export will draw it.
    /// </summary>
    /// <remarks>
    /// Once, before the frames start, because font shaping at video frame rate would cost
    /// more than the encode. The rectangle is measured with no zoom applied — a caption
    /// under a zoom is scaled from this raster rather than re-set at the magnified size,
    /// which is what macshot does too and what keeps the caption's own proportions steady
    /// while the picture behind it moves.
    /// </remarks>
    private async Task<IReadOnlyList<VideoCaption>> CaptionsAsync(int width, int height)
    {
        var whole = new CaptureRegion(0, 0, _sourceWidth, _sourceHeight);
        var scale = EffectsBand.XamlRoot?.RasterizationScale ?? 1;
        var captions = new List<VideoCaption>(_effects.Texts.Count);

        foreach (var text in _effects.Texts)
        {
            if (text.Duration <= 0)
            {
                continue;
            }

            var rect = VideoOverlayGeometry.OutputRect(
                text.Rect,
                whole,
                _sourceWidth,
                _sourceHeight,
                width,
                height);

            // Through WithText here rather than as it was typed: an emptied field must not
            // rasterize to a bare pill sitting on the picture with nothing in it.
            var raster = await VideoCaptionGlyphs.RenderAsync(
                EffectsBand,
                text.WithText(text.Text),
                (int)Math.Round(rect.Width),
                (int)Math.Round(rect.Height),
                height,
                scale);

            if (raster is not null)
            {
                captions.Add(new VideoCaption(text, raster));
            }
        }

        return captions;
    }

    /// <summary>
    /// Writes the MP4 with the cuts taken out of it, through the platform's own editor.
    /// </summary>
    /// <remarks>
    /// One clip per surviving stretch, which is what a cut is to Windows. Worth its own
    /// path rather than folding into the frame pipeline for two reasons: the platform
    /// encodes it in one pass rather than one seek per frame, and it carries the
    /// recording's audio across the cuts itself, in step, with nothing here to get wrong.
    /// </remarks>
    private async Task WriteCutMp4Async(StorageFile source, StorageFile destination)
    {
        var composition = new MediaComposition();

        foreach (var kept in VideoCuts.KeptRanges(_trim.Start, _trim.End, _effects.Cuts))
        {
            // A clip of its own per stretch. The same file opened repeatedly, which is
            // what MediaComposition expects: a clip is a view of a file, not the file.
            var clip = await MediaClip.CreateFromFileAsync(source);
            clip.TrimTimeFromStart = TimeSpan.FromSeconds(kept.Start);
            clip.TrimTimeFromEnd = TimeSpan.FromSeconds(Math.Max(0, _duration - kept.End));
            composition.Clips.Add(clip);
        }

        if (composition.Clips.Count == 0)
        {
            throw new InvalidOperationException(
                "The cuts and the trim between them leave nothing of this recording to export.");
        }

        await RenderAsync(composition, destination);
    }

    private async Task WriteMp4Async(StorageFile source, StorageFile destination)
    {
        var clip = await MediaClip.CreateFromFileAsync(source);
        clip.TrimTimeFromStart = TimeSpan.FromSeconds(_trim.Start);
        clip.TrimTimeFromEnd = TimeSpan.FromSeconds(Math.Max(0, _duration - _trim.End));

        var composition = new MediaComposition();
        composition.Clips.Add(clip);

        await RenderAsync(composition, destination);
    }

    private async Task RenderAsync(MediaComposition composition, StorageFile destination)
    {
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);

        // The audio half of an Auto profile is left unresolved, and asked to encode a
        // track it throws MF_E_ATTRIBUTENOTFOUND — which is every recording made with
        // sound, by any route out of this window. A recording with none gets no audio
        // stream at all rather than a silent one, which is what ScreenRecorder wrote it
        // without. Null-forgiving because the projection does not admit that dropping the
        // stream is allowed, which it is.
        profile.Audio = _sourceHasAudio
            ? AudioEncodingProperties.CreateAac(
                (uint)AudioPlan.SampleRate,
                (uint)AudioPlan.Channels,
                AudioPlan.Bitrate)
            : null!;

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
        //
        // Awaited through AsTask so the platform's own progress reaches the status line.
        // This is the export that looks stalled: the other two write a frame at a time and
        // had something to say between frames, while this one hands the whole file to
        // Windows and used to sit on "Exporting..." until it came back — which is what had
        // people pressing Save again (macshot #323).
        var reason = await composition
            .RenderToFileAsync(destination, MediaTrimmingPreference.Precise, profile)
            .AsTask(ExportProgress(outOf: 100));

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
    /// How far the export has got, in the status line.
    /// </summary>
    /// <param name="outOf">
    /// What a finished export reports. One for the two written here, which count their own
    /// frames; a hundred for <see cref="MediaComposition.RenderToFileAsync"/>, which
    /// reports a percentage.
    /// </param>
    /// <remarks>
    /// Built on the UI thread on purpose: a <see cref="Progress{T}"/> takes the
    /// synchronization context of wherever it was constructed, and all three exports
    /// report from a thread of the encoder's own. Constructed one call deeper — inside the
    /// encoder — the same object would set <c>StatusText</c> from that thread and throw.
    /// </remarks>
    private Progress<double> ExportProgress(double outOf = 1) => new(done =>
        StatusText.Text = L("Exporting...") + $" {done / outOf:P0}");

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
