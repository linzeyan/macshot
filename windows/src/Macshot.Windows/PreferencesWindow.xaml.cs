using System.Diagnostics;
using System.Globalization;
using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Input;
using Macshot.Windows.Core.Localization;
using Macshot.Windows.Core.Output;
using Macshot.Windows.Services;
using Macshot.Windows.Toolbar;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using WinRT.Interop;
using static Macshot.Windows.Services.Localization;

namespace Macshot.Windows;

/// <summary>
/// The settings window, the counterpart of the macOS
/// <c>PreferencesWindowController</c>.
/// </summary>
/// <remarks>
/// <para>
/// A change takes effect as it is made, which is what the macOS window does — it has no
/// Save button either. The alternative is a window whose contents mean nothing until a
/// button is found, and whose button means nothing once it has been pressed.
/// </para>
/// <para>
/// The controls are wired by hand rather than bound, because <see cref="CaptureSettings"/>
/// is an immutable record with no change notification and adding one purely for this
/// window would put UI concerns into Core.
/// </para>
/// </remarks>
public sealed partial class PreferencesWindow : Window
{
    /// <summary>Segoe Fluent Icons: an X, and a circling arrow — macshot's two.</summary>
    private const string ClearGlyph = "\uE894";

    private const string ResetGlyph = "\uE72C";

    /// <summary>The macOS settings window's content size, which this one is.</summary>
    private const double WidthDips = 620;

    private const double HeightDips = 520;

    /// <summary>
    /// How long a change waits before it is written.
    /// </summary>
    /// <remarks>
    /// Dragging the quality slider is one gesture and hundreds of notifications. Each
    /// write re-reads the file into the running app and hands the global shortcuts back to
    /// Windows to take again, so writing every notification would spend a drag doing that
    /// — and a shortcut that momentarily belongs to nobody is one a keypress can miss.
    /// Short enough that letting go of a control and closing the window keeps the change.
    /// </remarks>
    private static readonly TimeSpan WriteDelay = TimeSpan.FromMilliseconds(250);

    private readonly SettingsStore _settings;

    /// <summary>Collects a burst of changes into one write. See <see cref="WriteDelay"/>.</summary>
    private readonly DispatcherTimer _write = new() { Interval = WriteDelay };

    /// <summary>
    /// True while the controls are being filled in from the stored settings, so the
    /// notifications that causes are not mistaken for the user changing something.
    /// </summary>
    private bool _loading;

    /// <summary>Whether a change has been made that is not on disk yet.</summary>
    private bool _pending;

    /// <summary>One tick box per tool, in the order the toolbar keeps them.</summary>
    private readonly Dictionary<AnnotationTool, CheckBox> _toolToggles = [];

    /// <summary>One tick box per hideable toolbar button, by identifier.</summary>
    private readonly Dictionary<string, CheckBox> _actionToggles = new(StringComparer.Ordinal);

    /// <summary>The key each shortcut currently stands on, by identifier.</summary>
    /// <remarks>
    /// Held here rather than read back off the labels, because a label says "Space" and
    /// "None" — words, not keys — and turning those back into what to store would mean
    /// parsing this window's own display text.
    /// </remarks>
    private readonly Dictionary<string, string> _shortcutKeys = new(StringComparer.Ordinal);

    /// <summary>The reading in each shortcut's row, by identifier.</summary>
    private readonly Dictionary<string, TextBlock> _shortcutFields = new(StringComparer.Ordinal);

    /// <summary>
    /// Which shortcut is waiting for a key, or null when none is. Only one row can be
    /// waiting: a keypress that landed on two of them would bind both.
    /// </summary>
    private string? _recordingShortcut;

    private readonly ColorChoice _toolbarBackground = new("Background");
    private readonly ColorChoice _toolbarAccent = new("Accent");
    private readonly ColorChoice _toolbarIcon = new("Icons");

    public PreferencesWindow(SettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        InitializeComponent();
        // Every string in the XAML is already the English text macshot keys by,
        // so the page is translated in place rather than written twice.
        this.Localize();
        ShoutTheSectionHeadings();
        AppThemes.Apply(this, _settings.Current.Theme);

        // The markup selects the first item, which happens while the pages it switches
        // between are still being built — so the handler that would have shown it declined
        // to, and the first page has to be shown from here instead.
        ShowPage(Tabs.SelectedItem as ListViewItem);
        BuildToolsPage();
        BuildShortcutRows();
        Load(_settings.Current);
        PlaceOnScreen();

        _write.Tick += (_, _) => Persist();

        CaptureAreaHotkeyBox.BindingChanged += Setting_Changed;
        CaptureAllScreensHotkeyBox.BindingChanged += Setting_Changed;
        RecordScreenHotkeyBox.BindingChanged += Setting_Changed;

        // A change still waiting out its delay when the window goes is a change the user
        // made and watched take effect on screen.
        Closed += (_, _) => Persist();
    }

    /// <summary>
    /// Builds the parts of the Tools page that come from the toolbar rather than from the
    /// markup, so a tool added later appears here without this page being edited.
    /// </summary>
    private void BuildToolsPage()
    {
        foreach (var tool in ToolbarActions.ToolOrder)
        {
            // 13 to match the markup's rows rather than WinUI's 14: these sit in the same
            // column as the tick boxes the markup declares. Translated here rather than by
            // the page-wide pass, which has already run by the time this row exists.
            var toggle = new CheckBox { Content = L(ToolbarActions.Tooltip(tool)), MinWidth = 0, FontSize = 13 };
            toggle.Checked += Setting_Changed;
            toggle.Unchecked += Setting_Changed;
            _toolToggles[tool] = toggle;
            ToolToggles.Children.Add(toggle);
        }

        foreach (var (actions, host) in new[]
        {
            (ToolbarCustomActions.Bottom, BottomActionToggles),
            (ToolbarCustomActions.Right, RightActionToggles),
        })
        {
            foreach (var action in actions)
            {
                var toggle = new CheckBox { Content = L(action.Label), MinWidth = 0, FontSize = 13 };
                toggle.Checked += Setting_Changed;
                toggle.Unchecked += Setting_Changed;
                _actionToggles[action.Id] = toggle;
                host.Children.Add(toggle);
            }
        }

        foreach (var choice in new[] { _toolbarBackground, _toolbarAccent, _toolbarIcon })
        {
            choice.Changed += Setting_Changed;
            ToolbarColorRow.Children.Add(choice);
        }
    }

    /// <summary>
    /// Builds one row per single-key shortcut: the name, the key it stands on, and the
    /// three things that can be done to it — take a new key, take it off, put it back.
    /// </summary>
    /// <remarks>
    /// Built from <see cref="ToolShortcuts.All"/> rather than written out in the markup so
    /// that this page and the overlay cannot disagree about what exists. macshot's own row:
    /// label, an 80-wide reading, Set, and two small round buttons.
    /// </remarks>
    private void BuildShortcutRows()
    {
        // Cast rather than tested: the style is declared in this window's own markup, and
        // rows silently losing their column would be a worse failure than not opening.
        var labelStyle = (Style)((Grid)Content).Resources["RowLabel"];

        foreach (var shortcut in ToolShortcuts.All)
        {
            var field = new TextBlock
            {
                Width = 80,
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var set = new Button { Content = L("Set"), FontSize = 13, MinWidth = 64 };

            // The keypress is taken on the button itself rather than on the window, so
            // that arming a row and then clicking elsewhere cannot leave a listener behind
            // that swallows the next key typed into some other control.
            set.Click += (_, _) => Arm(shortcut, set);
            set.KeyDown += (_, e) => RecordShortcut(shortcut, set, e);
            set.LostFocus += (_, _) => CancelRecording(shortcut, set);

            var clear = SmallButton(ClearGlyph, L("None"));
            clear.Click += (_, _) =>
            {
                CancelRecording(shortcut, set);
                Assign(shortcut.Id, ToolShortcuts.Unbound);
            };

            var reset = SmallButton(ResetGlyph, L("Reset to default"));
            reset.Click += (_, _) =>
            {
                CancelRecording(shortcut, set);
                Assign(shortcut.Id, shortcut.DefaultKey);
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = $"{L(shortcut.Label)}:", Style = labelStyle });
            row.Children.Add(field);
            row.Children.Add(set);
            row.Children.Add(clear);
            row.Children.Add(reset);

            _shortcutFields[shortcut.Id] = field;
            ToolShortcutRows.Children.Add(row);
        }
    }

    /// <summary>A bordered icon button, the two macshot puts at the end of each row.</summary>
    /// <remarks>
    /// The font is named rather than left to the theme, because the theme's icon font
    /// differs between Windows versions and a glyph that is absent draws as a box.
    /// </remarks>
    private static Button SmallButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 12,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
            },
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    /// <summary>
    /// Puts a row into waiting-for-a-key, having taken any other row out of it.
    /// </summary>
    private void Arm(ToolShortcut shortcut, Button set)
    {
        // Pressing Set again is how a row is taken back out of waiting, which is what
        // macshot does — the button is the only thing to press once it says "Press...".
        if (_recordingShortcut == shortcut.Id)
        {
            CancelRecording(shortcut, set);
            return;
        }

        // A second row cannot be left waiting behind this one: its reading would say "…"
        // forever, and the next key would be claimed by whichever happened to have focus.
        if (_recordingShortcut is { } waiting)
        {
            ShowShortcut(waiting, _shortcutKeys.GetValueOrDefault(waiting, ToolShortcuts.Unbound));
        }

        _recordingShortcut = shortcut.Id;
        set.Content = L("Press...");
        if (_shortcutFields.TryGetValue(shortcut.Id, out var field))
        {
            field.Text = "…";
        }
    }

    /// <summary>
    /// Takes the key pressed while a row is waiting, and binds it.
    /// </summary>
    /// <remarks>
    /// Every key is handled while armed, including Tab and Space — otherwise Tab would
    /// move the focus away mid-assignment and Space would press the button it was meant to
    /// be assigned to. A key nothing can be bound to leaves the row waiting rather than
    /// binding something the overlay could never match.
    /// </remarks>
    private void RecordShortcut(ToolShortcut shortcut, Button set, KeyRoutedEventArgs e)
    {
        if (_recordingShortcut != shortcut.Id)
        {
            return;
        }

        e.Handled = true;

        if (e.Key == VirtualKey.Escape)
        {
            CancelRecording(shortcut, set);
            return;
        }

        // Ctrl and Alt are refused rather than stripped: the overlay only ever looks up
        // plain keys, so binding Ctrl+P would put a shortcut in the list that no press can
        // ever match. Shift is allowed through — a shifted letter is the same letter.
        var key = IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu)
            ? ToolShortcuts.Unbound
            : ShortcutKey.Of(e.Key);

        if (key.Length == 0)
        {
            return;
        }

        _recordingShortcut = null;
        set.Content = L("Set");
        Assign(shortcut.Id, key);
    }

    /// <summary>Leaves a row as it was, if it was the one waiting.</summary>
    private void CancelRecording(ToolShortcut shortcut, Button set)
    {
        if (_recordingShortcut != shortcut.Id)
        {
            return;
        }

        _recordingShortcut = null;
        set.Content = L("Set");
        ShowShortcut(shortcut.Id, _shortcutKeys.GetValueOrDefault(shortcut.Id, shortcut.DefaultKey));
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void Assign(string id, string key)
    {
        ShowShortcut(id, key);
        Apply();
    }

    private void ShowShortcut(string id, string key)
    {
        _shortcutKeys[id] = key;
        if (_shortcutFields.TryGetValue(id, out var field))
        {
            field.Text = L(ToolShortcuts.Describe(key));
        }
    }

    /// <summary>
    /// The shortcuts worth writing down: the ones that are not what this build ships.
    /// </summary>
    /// <remarks>
    /// Storing only the differences is what lets a later version change a default and have
    /// it reach everyone who never touched that row — and an entry that is present and
    /// empty still says "the user took this key off", because an empty string differs from
    /// a default that is a letter.
    /// </remarks>
    private Dictionary<string, string> ChosenShortcuts()
    {
        var chosen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var shortcut in ToolShortcuts.All)
        {
            var key = _shortcutKeys.GetValueOrDefault(shortcut.Id, shortcut.DefaultKey);
            if (!string.Equals(key, shortcut.DefaultKey, StringComparison.Ordinal))
            {
                chosen[shortcut.Id] = key;
            }
        }

        return chosen;
    }

    /// <summary>
    /// Takes a change the moment it is made, after <see cref="WriteDelay"/>.
    /// </summary>
    /// <remarks>
    /// One handler for every control wired in code, whatever its notification signature:
    /// this window writes all of its pages at once, so which control changed makes no
    /// difference to what happens next. Both parameters are the widest and most nullable
    /// they can be, which is what lets one method stand in for every event's delegate.
    /// </remarks>
    private void Setting_Changed(object? sender, object? args) => Apply();

    private void Setting_Toggled(object sender, RoutedEventArgs e) => Apply();

    private void Setting_NumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => Apply();

    private void Setting_SliderChanged(object sender, RangeBaseValueChangedEventArgs e) => Apply();

    private void Setting_SelectionChanged(object sender, SelectionChangedEventArgs e) => Apply();

    /// <summary>
    /// The preview-size slider, which also has a reading of its own — macshot puts one
    /// beside its slider, and a size expressed only as a knob position is a size nobody
    /// can go back to.
    /// </summary>
    private void ThumbnailScale_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        ShowThumbnailScale();
        Apply();
    }

    /// <summary>
    /// Writes the reading beside the slider, if there is one yet.
    /// </summary>
    /// <remarks>
    /// The guard is not defensive padding. A slider whose markup gives it a minimum of 50
    /// has its value coerced from WinUI's default of 0 the moment that attribute is
    /// applied, which raises ValueChanged <em>while the page is still being parsed</em> —
    /// and the reading beside it is the next element in the markup, so it does not exist
    /// yet. Dereferencing it there threw inside <c>InitializeComponent</c>, which is a
    /// constructor, on the UI thread: the settings window did not open and the app went
    /// with it.
    /// </remarks>
    private void ShowThumbnailScale()
    {
        if (ThumbnailScaleReading is not { } reading)
        {
            return;
        }

        reading.Text = $"{ThumbnailScaleSlider.Value:0}%";
    }

    /// <summary>
    /// Takes what a text box holds when the focus leaves it, rather than as it is typed.
    /// </summary>
    /// <remarks>
    /// A filename template is briefly nonsense on the way to being right — <c>{yyy</c> is
    /// three keystrokes into <c>{yyyy}</c> — and storing each of those would put a capture
    /// taken mid-edit under a name nobody chose. Leaving the field is the user saying they
    /// are done with it.
    /// </remarks>
    private void Setting_LostFocus(object sender, RoutedEventArgs e) => Apply();

    /// <summary>Notes that something changed, and starts the wait before it is written.</summary>
    private void Apply()
    {
        if (_loading)
        {
            return;
        }

        _pending = true;
        _write.Stop();
        _write.Start();
    }

    /// <summary>Writes every page, if anything is waiting to be written.</summary>
    private void Persist()
    {
        _write.Stop();

        if (!_pending)
        {
            return;
        }

        // The recorder cannot produce an unusable shortcut, but a hand-edited settings
        // file can, and this window shows what the file held. Refused rather than
        // repaired: normalizing would quietly put the default back, and a shortcut
        // silently reverting to Ctrl+Shift+X reads as macshot ignoring what was set.
        var unreadable = new[]
        {
            CaptureAreaHotkeyBox.Binding,
            CaptureAllScreensHotkeyBox.Binding,
            RecordScreenHotkeyBox.Binding,
        }.Where(text => !HotkeyBinding.TryParse(text, out _)).ToArray();

        if (unreadable.Length > 0)
        {
            StatusText.Text = $"Not a shortcut: {string.Join(", ", unreadable)}. Click it and press the keys — nothing is being kept until then.";
            return;
        }

        var settings = Collect();

        // Before the file, because this one lives outside it: the registry is what
        // actually makes macshot start with Windows, and a checkbox Windows refused has
        // to say so rather than be saved as though it took.
        if (settings.LaunchAtLogin != StartupRegistration.IsEnabled()
            && !StartupRegistration.Set(settings.LaunchAtLogin))
        {
            LaunchAtLoginCheck.IsChecked = StartupRegistration.IsEnabled();
            StatusText.Text = L("Windows would not let macshot change its startup entry.");
            return;
        }

        AppThemes.Apply(this, settings.Theme);

        try
        {
            _settings.Save(settings);
        }
        // Everything, not only the file system failures. This runs from a timer tick with
        // nobody above it to catch anything that escapes, and a preference that cannot be
        // stored is never a reason to take the app down.
        catch (Exception exception)
        {
            StatusText.Text = $"Could not save preferences: {exception.Message}";
            return;
        }

        _pending = false;
        StatusText.Text = string.Empty;
    }

    /// <summary>
    /// Puts every section heading in capitals, after they have been translated.
    /// </summary>
    /// <remarks>
    /// macOS uppercases its headings in code because AppKit has no such transform, and
    /// XAML has none either. Done here rather than by writing the headings in capitals,
    /// because the capitals were what broke the translation: "APPLICATION" is not a key
    /// macshot ships and "Application" is. Uppercasing a translated Chinese or Japanese
    /// heading does nothing, which is exactly what macOS does with it too.
    /// </remarks>
    private void ShoutTheSectionHeadings()
    {
        if (Content is not FrameworkElement root
            || !root.Resources.TryGetValue("SectionHeading", out var found)
            || found is not Style heading)
        {
            return;
        }

        Shout(root, 0);

        void Shout(DependencyObject? node, int depth)
        {
            // The logical tree, not VisualTreeHelper's: five of the six pages are
            // collapsed when this runs, and a collapsed page has no visual children to
            // walk. The cases are the ones LocalizedTree walks, for the same reason —
            // they are what this markup is built out of.
            const int MaxDepth = 64;

            if (node is null || depth > MaxDepth)
            {
                return;
            }

            switch (node)
            {
            case TextBlock text when ReferenceEquals(text.Style, heading):
                text.Text = text.Text.ToUpper(CultureInfo.CurrentCulture);
                break;

            case Panel panel:
                foreach (var child in panel.Children)
                {
                    Shout(child, depth + 1);
                }

                break;

            case ContentControl control:
                Shout(control.Content as DependencyObject, depth + 1);
                break;

            case Border border:
                Shout(border.Child, depth + 1);
                break;
            }
        }
    }

    /// <summary>
    /// Shows the chosen page. All six exist at once and one is visible: a Frame would
    /// rebuild the page on every click, and a change on any page writes every page.
    /// </summary>
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // The first item is selected in the markup, so this can fire while the tree is
        // still being built and before the pages it switches between exist.
        if (GeneralPage is null)
        {
            return;
        }

        ShowPage(Tabs.SelectedItem as ListViewItem);
    }

    private void ShowPage(ListViewItem? item)
    {
        var chosen = item?.Tag as string;

        foreach (var (tag, page) in Pages())
        {
            page.Visibility = tag == chosen ? Visibility.Visible : Visibility.Collapsed;
        }

        // The title says which page, as the macOS window's does. Six pages of settings
        // named only "Settings" is a window whose title bar stops meaning anything the
        // moment the user is looking for one of them in a screenshot or a taskbar. Taken
        // from the tag rather than from the tab, whose content is an icon and a caption.
        Title = chosen is null
            ? $"{BuildVariant.DisplayName} Settings"
            : $"{BuildVariant.DisplayName} Settings — {char.ToUpperInvariant(chosen[0])}{chosen[1..]}";
    }

    private IEnumerable<(string Tag, FrameworkElement Page)> Pages() =>
    [
        ("general", GeneralPage),
        ("capture", CapturePage),
        ("shortcuts", ShortcutsPage),
        ("tools", ToolsPage),
        ("recording", RecordingPage),
        ("about", AboutPage),
    ];

    /// <summary>
    /// Opens at a size the content fits in, in the middle of the primary display.
    /// </summary>
    /// <remarks>
    /// WinUI's default is a small cascaded window, so the first thing anyone does with
    /// macshot's preferences would be to drag it bigger before a single setting can be
    /// read. Centred rather than cascaded because macshot has no other window for this
    /// one to cascade from — it would appear near the top-left corner for no reason.
    /// </remarks>
    private void PlaceOnScreen()
    {
        var monitor = MonitorEnumerator.Enumerate().Layout.Primary;
        var width = (int)(WidthDips * monitor.Scale);
        var height = (int)(HeightDips * monitor.Scale);

        var appWindow = this.GetAppWindow();
        appWindow.UseAppIcon();
        appWindow.MoveAndResize(new RectInt32(
            (int)(monitor.WorkArea.X + ((monitor.WorkArea.Width - width) / 2)),
            (int)(monitor.WorkArea.Y + ((monitor.WorkArea.Height - height) / 2)),
            width,
            height));
    }

    private void Load(CaptureSettings settings)
    {
        // Filling a control notifies exactly as a user changing it does, and every one of
        // these would otherwise write back what was just read.
        _loading = true;
        try
        {
            Fill(settings);
        }
        finally
        {
            _loading = false;
        }
    }

    private void Fill(CaptureSettings settings)
    {
        FormatBox.ItemsSource = Enum.GetValues<CaptureImageFormat>().Select(format => format.ToString()).ToList();
        FormatBox.SelectedIndex = (int)settings.Format;
        QualitySlider.Value = settings.Quality;
        RecordingFormatBox.ItemsSource = Enum.GetValues<RecordingFormat>().Select(format => format.ToString()).ToList();
        RecordingFormatBox.SelectedIndex = (int)settings.RecordingFormat;
        DirectoryBox.Text = settings.SaveDirectory ?? string.Empty;
        TemplateBox.Text = settings.FilenameTemplate;
        RecordingTemplateBox.Text = settings.RecordingFilenameTemplate;
        // Through Core rather than a list here, so a rate the file names and the menu
        // does not offer still selects instead of being written back as 15.
        var rates = RecordingPlan.FrameRateChoices(settings.RecordingFrameRate).ToList();
        RecordingFrameRateBox.ItemsSource = rates;
        RecordingFrameRateBox.SelectedIndex = rates.IndexOf(settings.RecordingFrameRate);
        GifFrameRateBox.Value = settings.GifFrameRate;
        RecordedRegionBorderCheck.IsChecked = settings.ShowRecordedRegionBorder;
        ClickHighlightCheck.IsChecked = settings.ShowClickHighlight;
        KeystrokeCheck.IsChecked = settings.ShowKeystrokes;

        // macshot's two words for the same two choices, so the entries translate.
        KeystrokeModeBox.ItemsSource = new List<string> { L("Shortcuts Only"), L("All Keystrokes") };
        KeystrokeModeBox.SelectedIndex = settings.ShowEveryKeystroke ? 1 : 0;
        RecordSystemAudioCheck.IsChecked = settings.RecordSystemAudio;
        RecordMicAudioCheck.IsChecked = settings.RecordMicAudio;
        RecordingDirectoryBox.Text = settings.RecordingDirectory;

        // "Show in Explorer" rather than macshot's "Show in Finder", and untranslated
        // because of it: the translated string names a macOS app, and a Chinese reader on
        // Windows being told to look in the Finder is worse off than one reading English.
        // The third is the port's own — macshot's is its video editor, which this build
        // has not got, and a menu entry that opens nothing is not the answer.
        RecordingOnStopBox.ItemsSource = new List<string>
        {
            "Show in Explorer",
            L("Copy to clipboard"),
            "Do nothing",
        };
        RecordingOnStopBox.SelectedIndex = (int)settings.RecordingOnStop;
        HideRecordingHudCheck.IsChecked = settings.HideRecordingHud;

        WebcamCheck.IsChecked = settings.RecordWebcam;

        // macshot's own four corners, four sizes and two shapes, in its order, so every
        // entry is a string its translations are keyed by.
        WebcamCornerBox.ItemsSource = new List<string>
        {
            L("Bottom Right"),
            L("Bottom Left"),
            L("Top Right"),
            L("Top Left"),
        };
        WebcamCornerBox.SelectedIndex = (int)settings.WebcamCorner;

        WebcamSizeBox.ItemsSource = new List<string>
        {
            L("Webcam Size Small"),
            L("Webcam Size Medium"),
            L("Webcam Size Large"),
            L("Webcam Size Extra Large"),
        };
        WebcamSizeBox.SelectedIndex = (int)settings.WebcamSize;

        WebcamShapeBox.ItemsSource = new List<string> { L("Circle"), L("Rounded Rectangle") };
        WebcamShapeBox.SelectedIndex = (int)settings.WebcamShape;
        ClipboardCheck.IsChecked = settings.CopyToClipboard;
        AutoSaveCheck.IsChecked = settings.AutoSave;
        ThumbnailCheck.IsChecked = settings.ShowThumbnail;
        ThumbnailSecondsBox.Value = settings.ThumbnailSeconds;

        // macshot's four, in its order, so bottom-right is the first and the default.
        ThumbnailCornerBox.ItemsSource = new List<string>
        {
            L("Bottom Right"),
            L("Bottom Left"),
            L("Top Right"),
            L("Top Left"),
        };
        ThumbnailCornerBox.SelectedIndex = (int)settings.ThumbnailCorner;

        // As a percentage, because that is what the reading beside it says: the setting
        // itself is the multiplier macshot stores.
        ThumbnailScaleSlider.Value = settings.ThumbnailScale * 100;
        ShowThumbnailScale();
        DelaySecondsBox.Value = settings.DelaySeconds;
        HistorySizeBox.Value = settings.HistorySize;
        HistoryUnlimitedCheck.IsChecked = settings.HistoryUnlimited;
        HistorySizeBox.IsEnabled = !settings.HistoryUnlimited;
        RememberSelectionCheck.IsChecked = settings.RememberLastSelection;
        HideInstructionsCheck.IsChecked = settings.HideCaptureInstructions;
        PencilSmoothingBox.ItemsSource = Enum.GetValues<PencilSmoothing>().Select(mode => mode.ToString()).ToList();
        PencilSmoothingBox.SelectedIndex = (int)settings.PencilSmoothing;
        VerboseLoggingCheck.IsChecked = settings.VerboseLogging;
        AutomaticUpdatesCheck.IsChecked = settings.AutomaticUpdateChecks;
        BetaUpdatesCheck.IsChecked = settings.BetaUpdates;

        // The registry rather than the settings file: someone may have taken the entry
        // out from Task Manager's Startup tab, and the box has to say what is true.
        LaunchAtLoginCheck.IsChecked = StartupRegistration.IsEnabled();
        HideTrayIconCheck.IsChecked = settings.HideTrayIcon;

        ThemeBox.ItemsSource = new List<string> { L("Default"), L("Light"), L("Dark") };
        ThemeBox.SelectedIndex = (int)settings.Theme;

        // Each language named in itself, macshot's list in macshot's order: a reader
        // looking for their own language scans endonyms, not English names.
        LanguageBox.ItemsSource = AppLanguages.All.Select(language => language.Name).ToList();
        var chosen = AppLanguages.All
            .Select((language, index) => (language, index))
            .FirstOrDefault(entry => string.Equals(
                entry.language.Code,
                settings.Language,
                StringComparison.OrdinalIgnoreCase));
        LanguageBox.SelectedIndex = chosen.language.Code is null ? 0 : chosen.index;

        CaptureAreaHotkeyBox.Binding = settings.CaptureAreaHotkey;
        CaptureAllScreensHotkeyBox.Binding = settings.CaptureAllScreensHotkey;
        RecordScreenHotkeyBox.Binding = settings.RecordScreenHotkey;

        var shown = settings.EnabledTools();
        foreach (var (tool, toggle) in _toolToggles)
        {
            toggle.IsChecked = shown.Contains(tool);
        }

        foreach (var (id, toggle) in _actionToggles)
        {
            toggle.IsChecked = !settings.HiddenActions.Contains(id, StringComparer.Ordinal);
        }

        ShortcutTooltipsCheck.IsChecked = settings.ShowShortcutsInTooltips;
        foreach (var shortcut in ToolShortcuts.All)
        {
            ShowShortcut(shortcut.Id, ToolShortcuts.KeyFor(shortcut, settings.ToolShortcuts));
        }

        ShowToolbarColors(settings.ToToolbarColors());

        VersionText.Text = $"Version {Version}";
        SettingsPathText.Text = _settings.Path;

        UpdateQualityVisibility();
        UpdateTemplatePreview();
    }

    private void ShowToolbarColors(ToolbarColors colors)
    {
        _toolbarBackground.Color = ToUiColor(colors.Background);
        _toolbarAccent.Color = ToUiColor(colors.Accent);
        _toolbarIcon.Color = ToUiColor(colors.Icon);
    }

    private void ResetToolbarColors_Click(object sender, RoutedEventArgs e)
    {
        ShowToolbarColors(ToolbarColors.Default);
        Apply();
    }

    private static Color ToUiColor(AnnotationColor color) =>
        Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

    private static AnnotationColor ToAnnotationColor(Color color) =>
        new(color.R, color.G, color.B, color.A);

    private CaptureSettings Collect()
    {
        // Built from what is stored rather than from nothing, because this window does
        // not show every setting: the annotation colour, width and line style are
        // chosen on the overlay's own toolbar, and starting from a blank record would
        // hand them back their defaults every time any preference was saved.
        return (_settings.Current with
        {
            Format = SelectedFormat(),
            Quality = (int)QualitySlider.Value,
            RecordingFormat = RecordingFormatBox.SelectedIndex >= 0
                ? (RecordingFormat)RecordingFormatBox.SelectedIndex
                : RecordingFormat.Mp4,
            SaveDirectory = DirectoryBox.Text,
            FilenameTemplate = TemplateBox.Text,
            RecordingFilenameTemplate = RecordingTemplateBox.Text,
            RecordingFrameRate = RecordingFrameRateBox.SelectedItem is int rate
                ? rate
                : _settings.Current.RecordingFrameRate,
            GifFrameRate = double.IsNaN(GifFrameRateBox.Value)
                ? CaptureSettings.Default.GifFrameRate
                : (int)GifFrameRateBox.Value,
            ShowRecordedRegionBorder = RecordedRegionBorderCheck.IsChecked == true,
            ThumbnailCorner = (ThumbnailCorner)Math.Max(ThumbnailCornerBox.SelectedIndex, 0),
            ThumbnailScale = ThumbnailScaleSlider.Value / 100,
            ShowClickHighlight = ClickHighlightCheck.IsChecked == true,
            ShowKeystrokes = KeystrokeCheck.IsChecked == true,
            ShowEveryKeystroke = KeystrokeModeBox.SelectedIndex == 1,
            RecordSystemAudio = RecordSystemAudioCheck.IsChecked == true,
            RecordMicAudio = RecordMicAudioCheck.IsChecked == true,
            RecordingDirectory = RecordingDirectoryBox.Text.Trim(),
            RecordingOnStop = (RecordingOnStop)Math.Max(RecordingOnStopBox.SelectedIndex, 0),
            HideRecordingHud = HideRecordingHudCheck.IsChecked == true,
            RecordWebcam = WebcamCheck.IsChecked == true,
            WebcamCorner = (WebcamCorner)Math.Max(WebcamCornerBox.SelectedIndex, 0),
            WebcamSize = (WebcamSize)Math.Max(WebcamSizeBox.SelectedIndex, 0),
            WebcamShape = (WebcamShape)Math.Max(WebcamShapeBox.SelectedIndex, 0),
            CopyToClipboard = ClipboardCheck.IsChecked == true,
            AutoSave = AutoSaveCheck.IsChecked == true,
            ShowThumbnail = ThumbnailCheck.IsChecked == true,

            // NaN is what an emptied NumberBox reports, and casting that would give a
            // nonsense interval rather than an obviously wrong one.
            ThumbnailSeconds = double.IsNaN(ThumbnailSecondsBox.Value)
                ? CaptureSettings.Default.ThumbnailSeconds
                : (int)ThumbnailSecondsBox.Value,
            DelaySeconds = double.IsNaN(DelaySecondsBox.Value)
                ? CaptureSettings.Default.DelaySeconds
                : (int)DelaySecondsBox.Value,
            HistorySize = double.IsNaN(HistorySizeBox.Value)
                ? CaptureSettings.Default.HistorySize
                : (int)HistorySizeBox.Value,
            HistoryUnlimited = HistoryUnlimitedCheck.IsChecked == true,
            RememberLastSelection = RememberSelectionCheck.IsChecked == true,
            HideCaptureInstructions = HideInstructionsCheck.IsChecked == true,
            PencilSmoothing = PencilSmoothingBox.SelectedIndex >= 0
                ? (PencilSmoothing)PencilSmoothingBox.SelectedIndex
                : PencilSmoothing.Smooth,
            VerboseLogging = VerboseLoggingCheck.IsChecked == true,
            AutomaticUpdateChecks = AutomaticUpdatesCheck.IsChecked == true,
            BetaUpdates = BetaUpdatesCheck.IsChecked == true,
            LaunchAtLogin = LaunchAtLoginCheck.IsChecked == true,
            HideTrayIcon = HideTrayIconCheck.IsChecked == true,
            Theme = ThemeBox.SelectedIndex >= 0 ? (AppTheme)ThemeBox.SelectedIndex : AppTheme.System,
            Language = LanguageBox.SelectedIndex >= 0 && LanguageBox.SelectedIndex < AppLanguages.All.Count
                ? AppLanguages.All[LanguageBox.SelectedIndex].Code
                : AppLanguages.System,
            CaptureAreaHotkey = CaptureAreaHotkeyBox.Binding,
            CaptureAllScreensHotkey = CaptureAllScreensHotkeyBox.Binding,
            RecordScreenHotkey = RecordScreenHotkeyBox.Binding,

            // Stored as what is hidden rather than what is ticked, so a tool added in a
            // later version arrives switched on instead of hidden from everyone who has
            // ever saved this page.
            HiddenTools = [.. _toolToggles
                .Where(entry => entry.Value.IsChecked != true)
                .Select(entry => entry.Key.ToString())],
            HiddenActions = [.. _actionToggles
                .Where(entry => entry.Value.IsChecked != true)
                .Select(entry => entry.Key)],
            ToolShortcuts = ChosenShortcuts(),
            ShowShortcutsInTooltips = ShortcutTooltipsCheck.IsChecked == true,
            ToolbarBackgroundColor = ToAnnotationColor(_toolbarBackground.Color).ToHex(),
            ToolbarAccentColor = ToAnnotationColor(_toolbarAccent.Color).ToHex(),
            ToolbarIconColor = ToAnnotationColor(_toolbarIcon.Color).ToHex(),
        }).Normalized();
    }

    private CaptureImageFormat SelectedFormat() =>
        FormatBox.SelectedIndex >= 0 ? (CaptureImageFormat)FormatBox.SelectedIndex : CaptureImageFormat.Png;

    private void Format_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateQualityVisibility();
        UpdateTemplatePreview();
        Apply();
    }

    private void RecordingFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // The preview carries the extension, and MP4 and GIF do not share one.
        UpdateRecordingTemplatePreview();
        Apply();
    }

    /// <summary>
    /// Keeps the preview in step as the template is typed. What is typed is not stored
    /// until the focus leaves the box — see <see cref="Setting_LostFocus"/>.
    /// </summary>
    private void Template_TextChanged(object sender, TextChangedEventArgs e) => UpdateTemplatePreview();

    private void RecordingTemplate_TextChanged(object sender, TextChangedEventArgs e) =>
        UpdateRecordingTemplatePreview();

    /// <summary>
    /// Keeping everything overrides the count, so the count is greyed out rather than
    /// left looking like it still decides something.
    /// </summary>
    private void HistoryUnlimited_Toggled(object sender, RoutedEventArgs e)
    {
        HistorySizeBox.IsEnabled = HistoryUnlimitedCheck.IsChecked != true;
        Apply();
    }

    /// <summary>Quality has no meaning for a lossless format, so it is not offered for one.</summary>
    private void UpdateQualityVisibility()
    {
        // Called from SelectionChanged, which fires while the XAML tree is still
        // being built and before the panel exists.
        if (QualityPanel is null)
        {
            return;
        }

        QualityPanel.Visibility = SelectedFormat().IsLossy() ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Shows what the template resolves to right now. A template is the one setting
    /// whose effect is invisible until a file has already been written under the
    /// wrong name.
    /// </summary>
    private void UpdateTemplatePreview()
    {
        if (TemplatePreview is null || TemplateBox is null)
        {
            return;
        }

        TemplatePreview.Text = FilenameTemplate.Resolve(TemplateBox.Text, DateTimeOffset.Now)
            + SelectedFormat().FileExtension();
    }

    /// <summary>The same, for the template a recording is named with.</summary>
    private void UpdateRecordingTemplatePreview()
    {
        if (RecordingTemplatePreview is null || RecordingTemplateBox is null)
        {
            return;
        }

        var extension = RecordingFormatBox?.SelectedIndex == (int)RecordingFormat.Gif ? ".gif" : ".mp4";
        RecordingTemplatePreview.Text =
            FilenameTemplate.Resolve(RecordingTemplateBox.Text, DateTimeOffset.Now) + extension;
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");

        // An unpackaged app has no implicit window for the picker to parent itself
        // to, so it has to be told which one to use or the call fails outright.
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            DirectoryBox.Text = folder.Path;
            Apply();
        }
    }

    /// <summary>
    /// Writes the portable half of the preferences to a file the user chooses.
    /// </summary>
    /// <remarks>
    /// Pending edits are written first. The export reads the store, not the controls, so
    /// exporting immediately after typing in a box would otherwise miss what was typed —
    /// the window writes 250 ms after a change, and a user is faster than that.
    /// </remarks>
    private async void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        Persist();

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,

            // macshot's name for the same file, dated so a folder of them sorts.
            SuggestedFileName = $"macshot-settings-{DateTimeOffset.Now:yyyy-MM-dd}",
        };
        picker.FileTypeChoices.Add("macshot settings", [".json"]);

        // An unpackaged app has no implicit window for the picker to parent itself
        // to, so it has to be told which one to use or the call fails outright.
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            var export = SettingsPortability.Export(_settings.Current, Version, DateTimeOffset.Now);
            await File.WriteAllTextAsync(file.Path, export.Json);
            StatusText.Text = $"Exported {export.KeyCount} settings.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not export the settings: {exception.Message}";
        }
    }

    /// <summary>
    /// Replaces the preferences with the ones in a file, after asking.
    /// </summary>
    /// <remarks>
    /// The confirmation is macshot's, and it is worth keeping: an import replaces every
    /// portable setting at once, including the ones on tabs the user is not looking at,
    /// and there is no undo for it.
    /// </remarks>
    private async void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(file.Path);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not read that file: {exception.Message}";
            return;
        }

        // Read before asking, so a file that is not a settings file is refused without
        // a warning dialog about replacing anything.
        var imported = SettingsPortability.Import(json, _settings.Current);
        if (imported.Settings is not { } restored)
        {
            StatusText.Text = imported.Failure ?? "That file could not be imported.";
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = L("Replace your current settings?"),
            Content = "Importing replaces your preferences with the ones in this file. "
                + "Your save folder, the last selection, and screenshot history are kept. "
                + "This cannot be undone.",
            PrimaryButtonText = L("Import"),
            CloseButtonText = L("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        // Saved before the controls are refilled: Load suppresses the write-back, so
        // nothing here would reach the file otherwise.
        _settings.Save(restored);
        Load(restored);

        StatusText.Text = imported.SkippedKeys.Count == 0
            ? $"Imported {imported.AppliedCount} settings."
            : $"Imported {imported.AppliedCount} settings; {imported.SkippedKeys.Count} were not this version's to take.";
    }

    /// <summary>Opens the folder holding the settings file, with the file selected.</summary>
    private void ShowSettingsFile_Click(object sender, RoutedEventArgs e) => Reveal(_settings.Path);

    private void ResetDirectory_Click(object sender, RoutedEventArgs e)
    {
        DirectoryBox.Text = string.Empty;
        Apply();
    }

    /// <summary>
    /// Picks a folder for recordings alone.
    /// </summary>
    /// <remarks>
    /// Separate from the capture folder because a recording is a different kind of file:
    /// large, few, and usually on its way somewhere. Left empty it follows the captures,
    /// which is what someone who never opens this expects.
    /// </remarks>
    private async void BrowseRecordings_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        picker.FileTypeFilter.Add("*");

        // An unpackaged app has no implicit window for the picker to parent itself
        // to, so it has to be told which one to use or the call fails outright.
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        if (await picker.PickSingleFolderAsync() is { } folder)
        {
            RecordingDirectoryBox.Text = folder.Path;
            Apply();
        }
    }

    private void ClearRecordingDirectory_Click(object sender, RoutedEventArgs e)
    {
        RecordingDirectoryBox.Text = string.Empty;
        Apply();
    }

    /// <summary>
    /// Opens the folder holding the log and this settings file.
    /// </summary>
    /// <remarks>
    /// The folder rather than the log itself, because the two things worth collecting
    /// live side by side and because <c>%LOCALAPPDATA%</c> is not a path anyone types
    /// from memory. Selecting the log inside it saves the second step.
    /// </remarks>
    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(DiagnosticLog.Directory);
        Reveal(DiagnosticLog.Path);
    }

    /// <summary>
    /// Opens Explorer on <paramref name="path"/>'s folder with the file selected, or on
    /// the folder alone when the file is not there yet.
    /// </summary>
    private void Reveal(string path)
    {
        try
        {
            using var opened = Process.Start(new ProcessStartInfo("explorer.exe")
            {
                // Quoted: the path runs through the user's profile name, which can
                // contain a space.
                Arguments = File.Exists(path)
                    ? $"/select,\"{path}\""
                    : $"\"{Path.GetDirectoryName(path)}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not open the folder: {exception.Message}";
        }
    }

    /// <summary>What this build calls itself, shown on the About page and stamped into an export.</summary>
    private static string Version =>
        typeof(PreferencesWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    /// <summary>
    /// Deletes the kept copies now.
    /// </summary>
    /// <remarks>
    /// An action rather than a setting, so it says what it did: someone clearing history
    /// has just captured something they want gone, and every other control on this page
    /// reports nothing because taking effect is all there is to report.
    /// </remarks>
    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        ScreenshotHistory.Clear();
        StatusText.Text = L("History cleared.");
    }
}
