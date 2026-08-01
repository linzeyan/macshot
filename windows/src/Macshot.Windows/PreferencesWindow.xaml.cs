using System.Diagnostics;
using System.Globalization;
using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Input;
using Macshot.Windows.Core.Localization;
using Macshot.Windows.Core.Output;
using Macshot.Windows.Core.Upload;
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

    /// <summary>
    /// The three the "When done" menu offers, in macshot's order.
    /// </summary>
    /// <remarks>
    /// A list rather than a cast from the selected index, because the enum's order is not
    /// this one: its values are written into the settings file by name, so they may be
    /// added to but not reshuffled to suit a menu.
    /// </remarks>
    private static readonly RecordingOnStop[] OnStopOrder =
        [RecordingOnStop.OpenEditor, RecordingOnStop.ShowInFolder, RecordingOnStop.CopyToClipboard];

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

    /// <summary>
    /// macshot's twelve global shortcuts. Named fields rather than a dictionary keyed by
    /// string, so that reading one back into the settings record is checked when this is
    /// compiled instead of when the window opens.
    /// </summary>
    private readonly HotkeyBox _captureAreaHotkey = new();
    private readonly HotkeyBox _captureAllScreensHotkey = new();
    private readonly HotkeyBox _recordAreaHotkey = new();
    private readonly HotkeyBox _recordScreenHotkey = new();
    private readonly HotkeyBox _historyHotkey = new();
    private readonly HotkeyBox _captureTextHotkey = new();
    private readonly HotkeyBox _quickCaptureHotkey = new();
    private readonly HotkeyBox _scrollCaptureHotkey = new();
    private readonly HotkeyBox _openFromClipboardHotkey = new();
    private readonly HotkeyBox _captureLastAreaHotkey = new();
    private readonly HotkeyBox _pinFromClipboardHotkey = new();
    private readonly HotkeyBox _clearHistoryHotkey = new();

    /// <summary>All twelve again, for the checks that do not care which is which.</summary>
    private readonly List<HotkeyBox> _globalShortcuts = [];

    private readonly ColorChoice _toolbarBackground = new("Background");
    private readonly ColorChoice _toolbarAccent = new("Accent");
    private readonly ColorChoice _toolbarIcon = new("Icon");

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
        BuildGlobalShortcutRows();
        BuildShortcutRows();
        Load(_settings.Current);
        PlaceOnScreen();

        _write.Tick += (_, _) => Persist();

        // A change still waiting out its delay when the window goes is a change the user
        // made and watched take effect on screen.
        Closed += (_, _) => Persist();
    }

    /// <summary>
    /// Builds the parts of the Tools page that come from the toolbar rather than from the
    /// markup, so a tool added later appears here without this page being edited.
    /// </summary>
    /// <summary>
    /// Adds one tick box to a two-column grid, filling left to right and then down.
    /// </summary>
    /// <remarks>
    /// macshot lays these three lists out in an NSGridView of two columns, read across
    /// rather than down, and fourteen tools in a single column is a page that has to be
    /// scrolled to be counted. Rows are added as they are needed, so a tool or an action
    /// appearing later needs nothing here.
    /// </remarks>
    private static void PlaceInColumns(Grid host, FrameworkElement child)
    {
        var index = host.Children.Count;
        var row = index / 2;
        if (host.RowDefinitions.Count <= row)
        {
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        Grid.SetRow(child, row);
        Grid.SetColumn(child, index % 2);
        host.Children.Add(child);
    }

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
            PlaceInColumns(ToolToggles, toggle);
        }

        foreach (var (actions, host) in new[]
        {
            (ToolbarCustomActions.Bottom, BottomActionToggles),
            (ToolbarCustomActions.Right, RightActionToggles),
        })
        {
            foreach (var action in actions)
            {
#if OFFLINE
                // The offline build draws no Upload button, so it offers no switch for
                // one. The entry stays in Core's list, which both variants compile.
                if (action.Command is ToolbarCommand.Upload)
                {
                    continue;
                }
#endif

                var toggle = new CheckBox { Content = L(action.Label), MinWidth = 0, FontSize = 13 };
                toggle.Checked += Setting_Changed;
                toggle.Unchecked += Setting_Changed;
                _actionToggles[action.Id] = toggle;
                PlaceInColumns(host, toggle);
            }
        }

        // macshot's order — accent, icon, background (SettingsWindowController.swift:539).
        foreach (var choice in new[] { _toolbarAccent, _toolbarIcon, _toolbarBackground })
        {
            choice.Changed += Setting_Changed;
            ToolbarColorRow.Children.Add(choice);
        }
    }

    /// <summary>
    /// Builds macshot's twelve global shortcut rows, in macshot's order and under
    /// macshot's names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built here rather than written as twelve rows of markup because each row is a
    /// label, a recorder and two buttons whose behaviour depends on the same default —
    /// written out, that default would appear twice per row and be free to disagree with
    /// itself.
    /// </para>
    /// <para>
    /// The label is macshot's own name with a colon added after translating, not before:
    /// <c>Capture Area</c> is a string macshot ships and <c>Capture Area:</c> is not, so
    /// keying by the second would give twelve English labels in every other language.
    /// </para>
    /// </remarks>
    private void BuildGlobalShortcutRows()
    {
        // Cast rather than tested: the style is declared in this window's own markup, and
        // rows silently losing their column would be a worse failure than not opening.
        var labelStyle = (Style)((Grid)Content).Resources["RowLabel"];

        Add("Capture Area", _captureAreaHotkey, HotkeyBinding.CaptureArea.ToString());
        Add("Capture Screen", _captureAllScreensHotkey, HotkeyBinding.CaptureAllScreens.ToString());
        Add("Record Area", _recordAreaHotkey, HotkeyBinding.RecordArea.ToString());
        Add("Record Screen", _recordScreenHotkey, string.Empty);
        Add("History", _historyHotkey, HotkeyBinding.History.ToString());
        Add("Capture OCR & QR", _captureTextHotkey, HotkeyBinding.CaptureText.ToString());
        Add("Quick Capture", _quickCaptureHotkey, HotkeyBinding.QuickCapture.ToString());
        Add("Scroll Capture", _scrollCaptureHotkey, string.Empty);
        Add("Open from Clipboard", _openFromClipboardHotkey, string.Empty);
        Add("Capture Last Area", _captureLastAreaHotkey, string.Empty);
        Add("Pin from Clipboard", _pinFromClipboardHotkey, string.Empty);
        Add("Clear History", _clearHistoryHotkey, string.Empty);

        void Add(string label, HotkeyBox box, string fallback)
        {
            box.BindingChanged += Setting_Changed;

            var clear = SmallButton(ClearGlyph, L("None"));
            clear.Click += (_, _) => box.Assign(string.Empty);

            // Present even where the default is nothing, because a row can be bound and
            // then wanted back the way it came, and six of these came unbound.
            var reset = SmallButton(ResetGlyph, L("Reset to default"));
            reset.Click += (_, _) => box.Assign(fallback);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = $"{L(label)}:", Style = labelStyle });
            row.Children.Add(box);
            row.Children.Add(clear);
            row.Children.Add(reset);

            _globalShortcuts.Add(box);
            GlobalShortcutRows.Children.Add(row);
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
#if OFFLINE
            // Core is compiled once for both variants, so its list carries Upload in
            // either. A row here for a button this build does not draw would offer a key
            // that appears to do nothing.
            if (shortcut.Command is ToolbarCommand.Upload)
            {
                continue;
            }
#endif

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
        // Blank is not unreadable: it is macshot's None, which half of these ship as.
        var unreadable = _globalShortcuts
            .Select(box => box.Binding)
            .Where(text => !string.IsNullOrEmpty(text) && !HotkeyBinding.TryParse(text, out _))
            .ToArray();

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

#if !OFFLINE
        // Read again on the way in rather than bound, because both of these change from
        // outside this window: uploads happen while it is open, and the sign-in finishes
        // in a browser.
        if (chosen == "uploads")
        {
            ShowDriveAccount(_settings.Current);
            ShowUploadHistory(_settings.Current);
        }
#endif

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
        ("uploads", UploadsPage),
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
        FillUploads(settings);
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

        // macshot's two words for the same two choices, so the entries translate.
        KeystrokeModeBox.ItemsSource = new List<string> { L("Shortcuts Only"), L("All Keystrokes") };
        KeystrokeModeBox.SelectedIndex = settings.ShowEveryKeystroke ? 1 : 0;
        RecordingDirectoryBox.Text = settings.RecordingDirectory;

        // "Show in Explorer" rather than macshot's "Show in Finder", and untranslated
        // because of it: the translated string names a macOS app, and a Chinese reader on
        // Windows being told to look in the Finder is worse off than one reading English.
        RecordingOnStopBox.ItemsSource = new List<string>
        {
            L("Open editor"),
            "Show in Explorer",
            L("Copy to clipboard"),
        };

        // -1 for a settings file that still says "do nothing", which was offered while
        // the port had no video editor. Nothing is selected rather than something the
        // file does not say, and the next save writes whichever of the three is chosen.
        RecordingOnStopBox.SelectedIndex = Array.IndexOf(OnStopOrder, settings.RecordingOnStop);
        HideRecordingHudCheck.IsChecked = settings.HideRecordingHud;

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
        QuickCaptureBox.ItemsSource = new List<string>
        {
            L("Save to file"),
            L("Copy to clipboard"),

            // Not a macshot string: macshot ships "Save + copy to clipboard" with no
            // translation of its own, so there is nothing to look up and nothing lost by
            // writing it here as it is written there.
            "Save + copy to clipboard",
            L("Do nothing"),
        };

        QuickCaptureBox.SelectedIndex = (settings.AutoSave, settings.CopyToClipboard) switch
        {
            (true, false) => 0,
            (false, true) => 1,
            (true, true) => 2,
            _ => 3,
        };

        QuickCaptureEditorCheck.IsChecked = settings.QuickCaptureOpenEditor;
        CaptureSoundCheck.IsChecked = settings.PlayCaptureSound;
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
        HistorySizeBox.Value = settings.HistorySize;
        HistoryUnlimitedCheck.IsChecked = settings.HistoryUnlimited;
        HistorySizeBox.IsEnabled = !settings.HistoryUnlimited;
        RememberSelectionCheck.IsChecked = settings.RememberLastSelection;
        CaptureCursorCheck.IsChecked = settings.CaptureCursor;
        DoubleClickCopyCheck.IsChecked = settings.DoubleClickToCopy;
        HideInstructionsCheck.IsChecked = settings.HideCaptureInstructions;
        SelectionShadowCheck.IsChecked = settings.DisableSelectionShadow;
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

        _captureAreaHotkey.Binding = settings.CaptureAreaHotkey;
        _captureAllScreensHotkey.Binding = settings.CaptureAllScreensHotkey;
        _recordAreaHotkey.Binding = settings.RecordAreaHotkey;
        _recordScreenHotkey.Binding = settings.RecordScreenHotkey;
        _historyHotkey.Binding = settings.HistoryHotkey;
        _captureTextHotkey.Binding = settings.CaptureTextHotkey;
        _quickCaptureHotkey.Binding = settings.QuickCaptureHotkey;
        _scrollCaptureHotkey.Binding = settings.ScrollCaptureHotkey;
        _openFromClipboardHotkey.Binding = settings.OpenFromClipboardHotkey;
        _captureLastAreaHotkey.Binding = settings.CaptureLastAreaHotkey;
        _pinFromClipboardHotkey.Binding = settings.PinFromClipboardHotkey;
        _clearHistoryHotkey.Binding = settings.ClearHistoryHotkey;

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

        ShowAbout();
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

    /// <summary>
    /// Fills the Uploads page, or takes it away.
    /// </summary>
    /// <remarks>
    /// The markup is shared between the two build variants — one XAML file, compiled
    /// once — so the offline build hides the tab and its page here rather than by not
    /// having them. Everything that would talk to a service is behind the same condition
    /// as the services themselves.
    /// </remarks>
    private void FillUploads(CaptureSettings settings)
    {
#if OFFLINE
        UploadsTab.Visibility = Visibility.Collapsed;
        UploadsPage.Visibility = Visibility.Collapsed;
#else
        UploadProviderBox.ItemsSource = Enum.GetValues<UploadProvider>()
            .Select(provider => L(UploadProviders.Label(provider)))
            .ToList();
        UploadProviderBox.SelectedIndex = (int)settings.UploadProvider;
        UploadConfirmBox.IsChecked = settings.UploadConfirm;

        S3EndpointBox.Text = settings.S3Endpoint;
        S3RegionBox.Text = settings.S3Region;
        S3BucketBox.Text = settings.S3Bucket;
        S3AccessKeyBox.Text = settings.S3AccessKeyId;
        S3SecretKeyBox.Password = settings.S3SecretAccessKey;
        S3PublicUrlBox.Text = settings.S3PublicUrlBase;
        S3PathPrefixBox.Text = settings.S3PathPrefix;
        ImgbbKeyBox.Text = settings.ImgbbApiKey;

        ShowDriveAccount(settings);
        ShowUploadHistory(settings);
#endif
    }

#if !OFFLINE
    /// <summary>Says which account is signed in, and what the button now does.</summary>
    private void ShowDriveAccount(CaptureSettings settings)
    {
        var signedIn = Upload.GoogleDriveUploader.IsSignedIn;

        DriveAccountText.Text = signedIn
            ? (settings.GoogleDriveAccount.Length > 0 ? settings.GoogleDriveAccount : L("Signed in"))
            : L("Not signed in");

        DriveSignInButton.Content = signedIn ? L("Sign Out") : L("Sign In with Google");
    }

    /// <summary>
    /// Lists what has been uploaded to imgbb, newest first, each with the link that takes
    /// it down again.
    /// </summary>
    /// <remarks>
    /// Newest first, where the record keeps them oldest first: the one worth acting on is
    /// almost always the one just uploaded.
    /// </remarks>
    private void ShowUploadHistory(CaptureSettings settings)
    {
        UploadHistoryList.Children.Clear();
        UploadHistoryEmpty.Visibility = settings.ImgbbUploads.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (var entry in settings.ImgbbUploads.Reverse())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            row.Children.Add(new TextBlock
            {
                Text = entry.Link,
                FontSize = 12,
                Width = 300,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var copy = new Button { Content = L("Copy"), FontSize = 12 };
            copy.Click += (_, _) => CopyText(entry.Link);
            row.Children.Add(copy);

            var open = new Button { Content = L("Open"), FontSize = 12 };
            open.Click += (_, _) => OpenLink(entry.Link);
            row.Children.Add(open);

            // imgbb's delete link is a page that asks for confirmation, so this opens it
            // rather than deleting anything: taking the picture down is a decision made
            // on their site, not a button in a settings window that cannot undo itself.
            var delete = new Button { Content = L("Delete"), FontSize = 12 };
            delete.Click += (_, _) => OpenLink(entry.DeleteLink);
            row.Children.Add(delete);

            UploadHistoryList.Children.Add(row);
        }
    }

    private static void CopyText(string text)
    {
        // global::, because inside namespace Macshot.Windows the name "Windows" binds to
        // this assembly's own namespace rather than to the platform's.
        var package = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text);
        global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    private static void OpenLink(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(uri.ToString()) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
#endif

#if !OFFLINE
    /// <summary>
    /// A named theme brush, or a fixed colour close to it when the name is not in the
    /// dictionary — a missing brush must not be what stops a connection test reporting.
    /// </summary>
    /// <remarks>
    /// Compiled out of the offline build along with its only caller. Left in, it would be
    /// a private method nobody calls, which is a warning, which that build treats as an
    /// error.
    /// </remarks>
    private static Brush StatusBrush(string key, Color fallback)
    {
        // ContainsKey and the indexer, not TryGetValue: ResourceDictionary reaches C#
        // through the WinRT projection, where the IDictionary members are explicit
        // implementations and are not callable on the type itself.
        var resources = Application.Current.Resources;
        return resources.ContainsKey(key) && resources[key] is Brush themed
            ? themed
            : new SolidColorBrush(fallback);
    }
#endif

    /// <summary>Writes one small object to the bucket and says what came back.</summary>
    private async void TestS3_Click(object sender, RoutedEventArgs e)
    {
#if !OFFLINE
        // Persisted first, for the reason the sign-in is: the fields are written on a
        // delay, and a test that ran against the last saved values would report on
        // credentials that are no longer the ones on screen.
        Persist();

        S3TestButton.IsEnabled = false;
        S3TestStatus.Text = L("Testing...");
        S3TestStatus.Foreground = StatusBrush("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray);
        try
        {
            var failure = await new Upload.UploadService(_settings).TestS3Async(CancellationToken.None);
            S3TestStatus.Text = failure ?? L("Connection successful!");

            // Green for the one outcome that needs no reading, the system's error colour
            // for everything else: a failure here is a line of prose about which of six
            // fields is wrong, and it has to look like something meant to be read.
            S3TestStatus.Foreground = failure is null
                ? StatusBrush("SystemFillColorSuccessBrush", Microsoft.UI.Colors.SeaGreen)
                : StatusBrush("SystemFillColorCriticalBrush", Microsoft.UI.Colors.IndianRed);
        }
        finally
        {
            S3TestButton.IsEnabled = true;
        }
#else
        await Task.CompletedTask;
#endif
    }

    /// <summary>Signs in to Google Drive, or forgets the account that is signed in.</summary>
    private async void DriveSignIn_Click(object sender, RoutedEventArgs e)
    {
#if !OFFLINE
        // Written straight away rather than through the delayed write: a sign-in is not a
        // preference being adjusted, and the window may be closed while the browser is up.
        Persist();

        var uploads = new Upload.UploadService(_settings);
        if (Upload.GoogleDriveUploader.IsSignedIn)
        {
            uploads.SignOutOfDrive();
            ShowDriveAccount(_settings.Current);
            return;
        }

        DriveSignInButton.IsEnabled = false;
        StatusText.Text = L("Waiting for the browser...");
        try
        {
            var signedIn = await uploads.SignInToDriveAsync(CancellationToken.None);
            StatusText.Text = signedIn ? string.Empty : L("Sign-in was not completed.");
        }
        finally
        {
            DriveSignInButton.IsEnabled = true;
            ShowDriveAccount(_settings.Current);
        }
#else
        await Task.CompletedTask;
#endif
    }

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
            // No ShowClickHighlight, ShowKeystrokes, RecordSystemAudio, RecordMicAudio or
            // RecordWebcam. The recording strip owns those five, as it does on macOS, and
            // the with-expression carries whatever it last wrote straight through.
            ShowEveryKeystroke = KeystrokeModeBox.SelectedIndex == 1,
            RecordingDirectory = RecordingDirectoryBox.Text.Trim(),
            RecordingOnStop = OnStopOrder[Math.Max(RecordingOnStopBox.SelectedIndex, 0)],
            HideRecordingHud = HideRecordingHudCheck.IsChecked == true,
            WebcamCorner = (WebcamCorner)Math.Max(WebcamCornerBox.SelectedIndex, 0),
            WebcamSize = (WebcamSize)Math.Max(WebcamSizeBox.SelectedIndex, 0),
            WebcamShape = (WebcamShape)Math.Max(WebcamShapeBox.SelectedIndex, 0),
            // macshot's four: save, copy, both, neither. The port already had both
            // switches; this is the one list that spells out what each pair means.
            AutoSave = QuickCaptureBox.SelectedIndex is 0 or 2,
            CopyToClipboard = QuickCaptureBox.SelectedIndex is 1 or 2,
            QuickCaptureOpenEditor = QuickCaptureEditorCheck.IsChecked == true,
            PlayCaptureSound = CaptureSoundCheck.IsChecked == true,
            ShowThumbnail = ThumbnailCheck.IsChecked == true,

#if !OFFLINE
            UploadProvider = (UploadProvider)Math.Max(UploadProviderBox.SelectedIndex, 0),
            UploadConfirm = UploadConfirmBox.IsChecked == true,
            S3Endpoint = S3EndpointBox.Text,
            S3Region = S3RegionBox.Text,
            S3Bucket = S3BucketBox.Text,
            S3AccessKeyId = S3AccessKeyBox.Text,
            S3SecretAccessKey = S3SecretKeyBox.Password,
            S3PublicUrlBase = S3PublicUrlBox.Text,
            S3PathPrefix = S3PathPrefixBox.Text,
            ImgbbApiKey = ImgbbKeyBox.Text,
#endif

            // NaN is what an emptied NumberBox reports, and casting that would give a
            // nonsense interval rather than an obviously wrong one.
            ThumbnailSeconds = double.IsNaN(ThumbnailSecondsBox.Value)
                ? CaptureSettings.Default.ThumbnailSeconds
                : (int)ThumbnailSecondsBox.Value,
            // No DelaySeconds. The with-expression carries the stored value through, which
            // is what leaves the menu bar's Capture Delay submenu the one thing that sets it.
            HistorySize = double.IsNaN(HistorySizeBox.Value)
                ? CaptureSettings.Default.HistorySize
                : (int)HistorySizeBox.Value,
            HistoryUnlimited = HistoryUnlimitedCheck.IsChecked == true,
            RememberLastSelection = RememberSelectionCheck.IsChecked == true,
            CaptureCursor = CaptureCursorCheck.IsChecked == true,
            DoubleClickToCopy = DoubleClickCopyCheck.IsChecked == true,
            HideCaptureInstructions = HideInstructionsCheck.IsChecked == true,
            DisableSelectionShadow = SelectionShadowCheck.IsChecked == true,
            // No PencilSmoothing here. It is set from the tool options row while drawing,
            // and this record is built with "with", so leaving it out carries the stored
            // choice through rather than overwriting it with a control that is gone.
            VerboseLogging = VerboseLoggingCheck.IsChecked == true,
            AutomaticUpdateChecks = AutomaticUpdatesCheck.IsChecked == true,
            BetaUpdates = BetaUpdatesCheck.IsChecked == true,
            LaunchAtLogin = LaunchAtLoginCheck.IsChecked == true,
            HideTrayIcon = HideTrayIconCheck.IsChecked == true,
            Theme = ThemeBox.SelectedIndex >= 0 ? (AppTheme)ThemeBox.SelectedIndex : AppTheme.System,
            Language = LanguageBox.SelectedIndex >= 0 && LanguageBox.SelectedIndex < AppLanguages.All.Count
                ? AppLanguages.All[LanguageBox.SelectedIndex].Code
                : AppLanguages.System,
            CaptureAreaHotkey = _captureAreaHotkey.Binding,
            CaptureAllScreensHotkey = _captureAllScreensHotkey.Binding,
            RecordAreaHotkey = _recordAreaHotkey.Binding,
            RecordScreenHotkey = _recordScreenHotkey.Binding,
            HistoryHotkey = _historyHotkey.Binding,
            CaptureTextHotkey = _captureTextHotkey.Binding,
            QuickCaptureHotkey = _quickCaptureHotkey.Binding,
            ScrollCaptureHotkey = _scrollCaptureHotkey.Binding,
            OpenFromClipboardHotkey = _openFromClipboardHotkey.Binding,
            CaptureLastAreaHotkey = _captureLastAreaHotkey.Binding,
            PinFromClipboardHotkey = _pinFromClipboardHotkey.Binding,
            ClearHistoryHotkey = _clearHistoryHotkey.Binding,

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

    /// <summary>Fills in the About page: icon, name, version, and the offline note.</summary>
    private void ShowAbout()
    {
        AboutNameText.Text = BuildVariant.DisplayName;
        MadeByText.Text = $"{L("Made by")} sw33tLie";

        // macshot's own format string, filled in here rather than with string.Format:
        // the placeholders are Cocoa's %@, which .NET has no idea what to do with.
        var assembly = typeof(PreferencesWindow).Assembly.GetName().Version;
        var build = assembly?.Revision.ToString(CultureInfo.InvariantCulture) ?? "0";
        VersionText.Text = Fill(L("Version %@ (%@)"), Version, build);

        try
        {
            AboutIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri("ms-appx:///Assets/macshot.ico"));
        }
        catch (Exception error) when (error is UriFormatException or InvalidOperationException)
        {
            // The page is worth showing without its portrait.
            AboutIcon.Visibility = Visibility.Collapsed;
        }

        // #if rather than a test on BuildVariant.IsOffline: it is a const, so the branch
        // the other build never takes is unreachable code, which is a warning, which is an
        // error here.
#if OFFLINE
        AboutOfflineNote.Text = L(
            "Offline build: upload and cloud storage integrations are removed. Update checks may "
            + "still connect to MacShot's update server. Screenshots and recordings stay local "
            + "unless you share or save them yourself.");
        AboutOfflineNote.Visibility = Visibility.Visible;
#endif
    }

    /// <summary>
    /// Substitutes for Cocoa's <c>%@</c> placeholders in order.
    /// </summary>
    /// <remarks>
    /// macshot's translated strings are Cocoa format strings, and this port reads those
    /// files as they are shipped rather than re-authoring forty of them. Anything else —
    /// composing the sentence out of pieces here — puts the word order in the code, where
    /// no translator can reach it.
    /// </remarks>
    private static string Fill(string template, params string[] values)
    {
        var text = template;
        foreach (var value in values)
        {
            var at = text.IndexOf("%@", StringComparison.Ordinal);
            if (at < 0)
            {
                break;
            }

            text = string.Concat(text.AsSpan(0, at), value, text.AsSpan(at + 2));
        }

        return text;
    }

    /// <summary>
    /// Puts what the displays look like from in here on the clipboard, for a bug report
    /// about a capture that came out the wrong size or off the wrong screen.
    /// </summary>
    private async void CopyScreenInfo_Click(object sender, RoutedEventArgs e)
    {
        var report = new System.Text.StringBuilder()
            .Append(BuildVariant.DisplayName).Append(' ').AppendLine(Version);

        try
        {
            var layout = MonitorEnumerator.Enumerate().Layout;
            report.Append("Virtual bounds: ").AppendLine(Describe(layout.VirtualBounds));
            foreach (var monitor in layout.Monitors)
            {
                report
                    .Append(monitor.IsPrimary ? "* " : "  ")
                    .Append(monitor.DeviceName).Append("  ")
                    .Append(Describe(monitor.Bounds))
                    .Append("  scale ").Append(monitor.Scale.ToString("0.##", CultureInfo.InvariantCulture))
                    .Append("  work ").AppendLine(Describe(monitor.WorkArea));
            }
        }
        catch (Exception error)
        {
            // Whatever went wrong enumerating the displays is itself the most useful thing
            // this report could carry, so it goes in rather than emptying the clipboard.
            report.Append("Could not enumerate displays: ").AppendLine(error.Message);
        }

        // global::, because this file's own namespace is Macshot.Windows and a bare
        // "Windows." binds to that first.
        var package = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(report.ToString());
        global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        // The button says what happened and then goes back to saying what it does, as
        // macshot's does. Nothing else on this page reports, so a status line elsewhere
        // would be looked for in the wrong place.
        ScreenInfoButton.Content = L("Copied!");
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        ScreenInfoButton.Content = L("Copy Screen Info");

        static string Describe(CaptureRegion region) => string.Create(
            CultureInfo.InvariantCulture,
            $"{region.Width}x{region.Height} at {region.X},{region.Y}");
    }

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
