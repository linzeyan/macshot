using Macshot.Windows.Core.Capture;
using Macshot.Windows.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;

namespace Macshot.Windows;

/// <summary>
/// Asks what to do with a finished recording's two sources of sound — macshot's
/// <c>AudioMergeController</c>.
/// </summary>
/// <remarks>
/// <para>
/// Raised once a recording that had both a microphone and system audio stops, before it is
/// delivered anywhere (<c>AppDelegate.swift:2596–2606</c>), with the same title, the same
/// question, a row per track and the same two answers.
/// </para>
/// <para>
/// What the two answers mean here differs from the Mac in one way worth stating plainly.
/// macshot's file holds two tracks and most players decode only the first, so
/// <em>Merge Audio</em> flattens them into one and <em>Keep Separate</em> leaves two. This
/// port has one track with both already summed into it — <see cref="AudioPlan"/> says why —
/// so <em>Merge Audio</em> writes the recording again with the two sources weighed as the
/// sliders say, and <em>Keep Separate</em> delivers the recording as it was made. The
/// wording is macshot's because the wording is the translation key: these six strings are
/// vendored in forty languages, and anything else would come out in English.
/// </para>
/// <para>
/// The panel closes the moment it is answered and the merge runs behind it, which is what
/// macshot does. Here that matters more than it does there: a merge on Windows re-encodes
/// the recording rather than remuxing it, so holding the panel up until it finished would
/// be holding it up for minutes.
/// </para>
/// </remarks>
public sealed partial class AudioMergeWindow : Window
{
    /// <summary>
    /// macshot's panel is 380 x 160 (<c>AudioMergeController.swift:23–24</c>). The width
    /// carries over; the height does not, because a WinUI slider and button are half again
    /// as tall as the AppKit controls that number was measured from.
    /// </summary>
    private const double WidthDips = 380;

    private const double HeightDips = 220;

    /// <summary>Wide enough for the longer of the two labels in most languages.</summary>
    private const double LabelWidthDips = 100;

    /// <summary>
    /// How far one press of an arrow key moves a volume. Fine enough to balance by ear,
    /// coarse enough that the whole range is thirty presses rather than a hundred and fifty.
    /// </summary>
    private const double VolumeStep = 0.05;

    private readonly Action<AudioMergeAnswer> _answered;
    private readonly Dictionary<AudioTrackKind, Slider> _volumes = [];

    private bool _delivered;
    private bool _closed;

    /// <param name="answered">
    /// Called exactly once, with whichever answer the panel was given — including the one
    /// its close button means. A recording is already on disk by the time this window
    /// opens, and dropping the callback would leave it there with nothing to deliver it.
    /// </param>
    public AudioMergeWindow(SettingsStore settings, Action<AudioMergeAnswer> answered)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(answered);

        _answered = answered;

        InitializeComponent();

        // Before the page-wide pass rather than after it, so the labels are translated and
        // given their weight by the same walk as everything else in the window. Anything
        // built after Localize has to do both by hand.
        BuildTrackRows();
        this.Localize();
        this.CloseOnControlW();
        AppThemes.Apply(this, settings.Current.Theme);

        // The close button is macshot's Skip: a recording that has just been made must not
        // be lost because the question about it was dismissed.
        Closed += (_, _) =>
        {
            _closed = true;
            Answer(AudioMergeAnswer.KeepSeparate);
        };
    }

    /// <summary>Puts the panel on the screen and waits to be answered.</summary>
    public void Ask()
    {
        var appWindow = this.GetAppWindow();
        appWindow.UseAppIcon();

        // A question, not a workspace: nothing in it rewards being resized, and macshot's
        // own panel is [.titled, .closable] with no resize control either. Created rather
        // than cast to, for the reason WindowExtensions.MakeChromeless records — a window
        // WinUI made does not report an OverlappedPresenter.
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;

        // macshot's floating panel level. The recording stopped while the user was in
        // whatever they were recording, and the question belongs in front of it.
        presenter.IsAlwaysOnTop = true;
        appWindow.SetPresenter(presenter);
        appWindow.MoveAndResize(Centred());

        // Foreground rather than merely activated, for the same reason macshot calls
        // activate(ignoringOtherApps:) here: macshot is never the foreground app when a
        // recording ends, and a shell that refuses the request leaves the panel on screen
        // with every key still going to the app behind it.
        this.TakeForeground();

        // On the answer that presses Return, which is macshot's key equivalent for it.
        MergeButton.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// One row per track, in the order <see cref="AudioMerge.Order"/> gives — which is the
    /// order the recording's own sources are in.
    /// </summary>
    private void BuildTrackRows()
    {
        foreach (var kind in AudioMerge.Order)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidthDips) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = AudioMerge.Label(kind),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(label);

            var volume = new Slider
            {
                Minimum = AudioMerge.MinimumVolume,
                Maximum = AudioMerge.MaximumVolume,
                Value = AudioMerge.DefaultVolume,
                StepFrequency = VolumeStep,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(volume, 1);
            row.Children.Add(volume);

            _volumes[kind] = volume;
            TrackRows.Children.Add(row);
        }
    }

    private void Merge_Click(object sender, RoutedEventArgs e) =>
        Answer(new AudioMergeAnswer(
            true,
            Volume(AudioTrackKind.Microphone),
            Volume(AudioTrackKind.System)));

    private void KeepSeparate_Click(object sender, RoutedEventArgs e) =>
        Answer(AudioMergeAnswer.KeepSeparate);

    /// <remarks>
    /// macshot gives Skip the Escape key equivalent. On a panel whose other answer costs a
    /// re-encode, the way out has to be the one that is always there.
    /// </remarks>
    private void Panel_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Answer(AudioMergeAnswer.KeepSeparate);
        }
    }

    private double Volume(AudioTrackKind kind) =>
        _volumes.TryGetValue(kind, out var slider) ? AudioMerge.Clamp(slider.Value) : AudioMerge.DefaultVolume;

    /// <summary>
    /// Delivers the answer, once, and closes.
    /// </summary>
    /// <remarks>
    /// Both guards earn their place. The first is what makes closing safe to answer with:
    /// <see cref="Window.Close"/> below raises Closed, which comes back here, and the
    /// recording would otherwise be delivered twice — once merged and once not, into two
    /// editors. The second is for the other direction, a window closed by its own button,
    /// where the answer arrives from inside the close and there is nothing left to close.
    /// </remarks>
    private void Answer(AudioMergeAnswer answer)
    {
        if (_delivered)
        {
            return;
        }

        _delivered = true;
        _answered(answer);

        if (!_closed)
        {
            Close();
        }
    }

    /// <summary>
    /// In the middle of the primary display's work area, which is where macshot's
    /// <c>panel.center()</c> puts it.
    /// </summary>
    private static RectInt32 Centred()
    {
        var monitor = MonitorEnumerator.Enumerate().Layout.Primary;
        var width = (int)(WidthDips * monitor.Scale);
        var height = (int)(HeightDips * monitor.Scale);
        var area = monitor.WorkArea;

        return new RectInt32(
            (int)(area.X + ((area.Width - width) / 2)),
            (int)(area.Y + ((area.Height - height) / 2)),
            width,
            height);
    }
}
