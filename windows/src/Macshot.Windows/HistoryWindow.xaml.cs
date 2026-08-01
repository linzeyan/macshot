using System.Globalization;
using Macshot.Windows.Core.Output;
using Macshot.Windows.Services;
using Macshot.Windows.Toolbar;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using static Macshot.Windows.Services.Localization;

using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.System;
using Windows.UI;

namespace Macshot.Windows;

/// <summary>
/// What the panel was asked to do with a capture, for the things it cannot do itself.
/// </summary>
/// <remarks>
/// Copying, saving elsewhere and deleting are all one service call, so the panel makes
/// them. Reopening and pinning need the windows the controller keeps, and it makes those.
/// </remarks>
public enum HistoryAction
{
    Open,
    Pin,
}

/// <summary>One capture, and what the user asked for it.</summary>
public sealed record HistoryRequest(HistoryEntry Entry, HistoryAction Action);

/// <summary>
/// The history panel: a shallow strip hanging from the top of the screen, holding every
/// capture macshot has kept as a picture that can be flicked through sideways.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>HistoryOverlayController</c>. The tray menu already lists the last few by
/// time, which answers "the one I took a minute ago" and nothing else; choosing between
/// yesterday's captures means looking at them. A strip rather than a window because that
/// choice is a glance — a full window of thumbnails is a file browser, and it covers the
/// work the capture was of.
/// </para>
/// <para>
/// Click copies and dismisses, which is what a capture is fetched out of history for.
/// Everything else — reopening it, pinning it, saving it somewhere — is on the card's
/// context menu, so the common case costs one click and the rest cost two.
/// </para>
/// <para>
/// Thumbnails are decoded at card width rather than full size. A history of forty 4K
/// screenshots is hundreds of megabytes of pixels nobody is looking at closely, and the
/// decoder can skip most of that work if it is told the size up front.
/// </para>
/// </remarks>
public sealed partial class HistoryWindow : Window
{
    /// <summary>
    /// How many captures the panel shows. Deep enough to cover more than the tray menu's
    /// handful, shallow enough that opening it does not decode a hundred images.
    /// </summary>
    private const int Depth = 60;

    /// <summary>The width thumbnails are decoded at, matching the card.</summary>
    private const int ThumbnailWidth = (int)HistoryPanelLayout.CardWidth;

    /// <summary>macshot's tabs. The English is the key the translations are under.</summary>
    private static readonly string[] FilterNames = ["All", "Screenshots", "GIFs"];

    private static readonly SolidColorBrush CardFill = Ink(0x0D);
    private static readonly SolidColorBrush CardFillLifted = Ink(0x1F);
    private static readonly SolidColorBrush LabelInk = Ink(0x73);
    private static readonly SolidColorBrush LabelInkLifted = Ink(0xD9);
    private static readonly SolidColorBrush HintInk = Ink(0xE6);
    private static readonly SolidColorBrush TabIdleFill = Ink(0x1A);
    private static readonly SolidColorBrush TabIdleInk = Ink(0x8C);
    private static readonly SolidColorBrush TabActiveInk = Ink(0xFF);
    private static readonly SolidColorBrush HintScrim = new(Color.FromArgb(0x59, 0, 0, 0));
    private static readonly SolidColorBrush NoFill = new(Colors.Transparent);

    private readonly SettingsStore _settings;
    private readonly List<HistoryTile> _tiles = [];
    private string _filter = FilterNames[0];

    /// <summary>
    /// Set while a drag or a save dialog is in hand. The panel closes when it loses
    /// focus, and both of those take focus away from it on purpose.
    /// </summary>
    private bool _holdOpen;

    /// <summary>
    /// Dark whatever the theme setting says, like macshot's own panel and for the same
    /// reason as the toolbar: it is drawn over a screenshot rather than inside a window,
    /// and a light one disappears into half the captures anyone takes.
    /// </summary>
    public HistoryWindow(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        InitializeComponent();

        // Every string in the XAML is already the English text macshot keys by,
        // so the panel is translated in place rather than written twice.
        this.Localize();
        ToolTipService.SetToolTip(Trash, L("Clear History"));
        BuildFilters();

        Activated += HistoryWindow_Activated;
    }

    /// <summary>Raised with the capture the user picked and what they asked for.</summary>
    public event EventHandler<HistoryRequest>? ActionRequested;

    public async Task ShowAsync()
    {
        var appWindow = this.GetAppWindow();
        var monitor = MonitorEnumerator.Enumerate().Layout.Primary;
        var (x, y, width, height) = HistoryPanelLayout.For(monitor.WorkArea, monitor.Scale);

        appWindow.MakeChromeless().IsAlwaysOnTop = true;
        appWindow.MoveAndResize(new RectInt32(x, y, width, height));
        this.RoundCorners(HairlineColour);

        this.TakeForeground();
        _ = PanelRoot.Focus(FocusState.Programmatic);

        await ReloadAsync();
    }

    /// <summary>macshot's white 0.08 hairline, as a COLORREF the border attribute takes.</summary>
    private const int HairlineColour = 0x00303030;

    private static SolidColorBrush Ink(byte alpha) => new(Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF));

    /// <summary>
    /// Reads the history off disk and draws whatever the current tab admits.
    /// </summary>
    private async Task ReloadAsync()
    {
        _tiles.Clear();

        foreach (var entry in ScreenshotHistory.Recent(Depth))
        {
            if (await LoadTileAsync(entry) is { } tile)
            {
                _tiles.Add(tile);
            }
        }

        Draw();
    }

    /// <summary>
    /// Lays out the cards the current tab admits, without going back to disk — switching
    /// tabs is a filter over what was already read, not a reload.
    /// </summary>
    private void Draw()
    {
        Cards.Children.Clear();

        var shown = _tiles.Where(tile => Matches(_filter, tile.Entry)).ToList();
        foreach (var tile in shown)
        {
            Cards.Children.Add(BuildCard(tile));
        }

        Row.Visibility = shown.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Which captures a tab admits.
    /// </summary>
    /// <remarks>
    /// GIFs matches nothing today, in both products: macshot's history writes PNG for
    /// every entry it keeps, so the tab is there for recordings it does not yet archive.
    /// Kept rather than dropped because the panel is meant to be macshot's, and a tab
    /// that appears when recordings are archived is one fewer difference to explain.
    /// </remarks>
    private static bool Matches(string filter, HistoryEntry entry) => filter switch
    {
        "Screenshots" => Path.GetExtension(entry.Path).Equals(".png", StringComparison.OrdinalIgnoreCase),
        "GIFs" => Path.GetExtension(entry.Path).Equals(".gif", StringComparison.OrdinalIgnoreCase),
        _ => true,
    };

    private void BuildFilters()
    {
        foreach (var name in FilterNames)
        {
            var tab = new Button
            {
                Content = L(name),
                Height = HistoryPanelLayout.TabHeight,
                MinHeight = HistoryPanelLayout.TabHeight,
                MinWidth = 0,
                Padding = new Thickness(HistoryPanelLayout.TabPaddingHorizontal, 0, HistoryPanelLayout.TabPaddingHorizontal, 0),
                CornerRadius = new CornerRadius(HistoryPanelLayout.TabHeight / 2),
                BorderThickness = new Thickness(0),
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                Tag = name,
            };

            tab.Click += Filter_Click;
            Filters.Children.Add(tab);
        }

        PaintFilters();
    }

    private void PaintFilters()
    {
        foreach (var tab in Filters.Children.OfType<Button>())
        {
            var active = (string?)tab.Tag == _filter;
            tab.Background = active ? ToolbarPalette.AccentBrush : TabIdleFill;
            tab.Foreground = active ? TabActiveInk : TabIdleInk;
        }
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name } || name == _filter)
        {
            return;
        }

        _filter = name;
        PaintFilters();
        Draw();
    }

    /// <summary>
    /// One card: the picture, its size and its age, and everything that can be done with
    /// it.
    /// </summary>
    private Border BuildCard(HistoryTile tile)
    {
        var preview = new Border
        {
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(
                HistoryPanelLayout.CardInset,
                HistoryPanelLayout.CardInset,
                HistoryPanelLayout.CardInset,
                0),
            Child = new Image { Source = tile.Thumbnail, Stretch = Stretch.Uniform },
        };

        // Over the picture rather than under the card, because it says what clicking the
        // picture does and there is no room under it that is not the label.
        var hint = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = HintScrim,
            Visibility = Visibility.Collapsed,
            Margin = preview.Margin,
            Child = new TextBlock
            {
                Text = L("Click to copy · Drag to app"),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = HintInk,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            },
        };

        var label = new TextBlock
        {
            Text = tile.Caption,
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = LabelInk,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var rows = new Grid();
        rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HistoryPanelLayout.CardLabelHeight) });
        rows.Children.Add(preview);
        rows.Children.Add(hint);
        Grid.SetRow(label, 1);
        rows.Children.Add(label);

        var card = new Border
        {
            Width = HistoryPanelLayout.CardWidth,
            Height = HistoryPanelLayout.CardHeight,
            CornerRadius = new CornerRadius(HistoryPanelLayout.CardCornerRadius),
            Background = CardFill,
            BorderThickness = new Thickness(1.5),
            BorderBrush = NoFill,
            VerticalAlignment = VerticalAlignment.Top,
            CanDrag = true,
            Child = rows,
        };

        card.PointerEntered += (_, _) =>
        {
            card.Background = CardFillLifted;
            card.BorderBrush = Lift();
            label.Foreground = LabelInkLifted;
            hint.Visibility = Visibility.Visible;
        };

        card.PointerExited += (_, _) =>
        {
            card.Background = CardFill;
            card.BorderBrush = NoFill;
            label.Foreground = LabelInk;
            hint.Visibility = Visibility.Collapsed;
        };

        card.PointerPressed += (_, e) =>
        {
            // Left only: the right button is opening the context menu, and copying the
            // capture as well would put the wrong thing on the clipboard behind it.
            if (e.GetCurrentPoint(card).Properties.PointerUpdateKind is PointerUpdateKind.LeftButtonPressed)
            {
                e.Handled = true;
                _ = CopyAsync(tile.Entry);
            }
        };

        card.DragStarting += (_, e) => StartDrag(tile.Entry, e);
        card.DropCompleted += (_, _) =>
        {
            // The file has been handed over, which is the whole of what the drag was for.
            _holdOpen = false;
            Close();
        };

        card.ContextFlyout = BuildMenu(tile.Entry);
        return card;
    }

    /// <summary>The accent at macshot's 0.7, which is what marks the card under the pointer.</summary>
    private static SolidColorBrush Lift() => new(Color.FromArgb(
        0xB3,
        ToolbarPalette.Accent.R,
        ToolbarPalette.Accent.G,
        ToolbarPalette.Accent.B));

    private MenuFlyout BuildMenu(HistoryEntry entry)
    {
        var menu = new MenuFlyout
        {
            // The panel is 240 tall and this menu is not far off it. Constrained to the
            // window it would be squeezed into whatever is left under the pointer.
            ShouldConstrainToRootBounds = false,
        };

        // Its own window once it may overhang, and that takes focus off the panel —
        // which is how the panel is dismissed.
        menu.Opening += (_, _) => _holdOpen = true;
        menu.Closed += (_, _) => _holdOpen = false;

        void Add(string text, Action run)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
        }

        Add(L("Copy"), () => _ = CopyAsync(entry));
        Add(L("Save As..."), () => _ = SaveAsAsync(entry));
        Add(L("Open in Editor"), () => Ask(entry, HistoryAction.Open));
        Add(L("Pin to Screen"), () => Ask(entry, HistoryAction.Pin));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add(L("Delete"), () => Forget(entry));

        return menu;
    }

    /// <summary>
    /// Hands the capture to the owner and gets out of the way.
    /// </summary>
    /// <remarks>
    /// Closed rather than left open, because every action here puts the capture somewhere
    /// the user is about to look — the clipboard, the editor, a pin — and a panel still
    /// covering the top of the screen is in front of it.
    /// </remarks>
    private void Ask(HistoryEntry entry, HistoryAction action)
    {
        ActionRequested?.Invoke(this, new HistoryRequest(entry, action));
        Close();
    }

    /// <summary>
    /// What a plain click does, and what the panel is opened for: the capture on the
    /// clipboard, and the panel out of the way.
    /// </summary>
    private async Task CopyAsync(HistoryEntry entry)
    {
        if (await ReadAsync(entry) is { } frame)
        {
            await ImageDelivery.CopyToClipboardAsync(frame);
        }

        // Closed either way. A click that leaves the panel sitting there reads as a
        // click that did not register, and the user clicks again.
        Close();
    }

    /// <summary>
    /// Writes the capture wherever the user says, rather than where the preferences do.
    /// </summary>
    private async Task SaveAsAsync(HistoryEntry entry)
    {
        if (await ReadAsync(entry) is not { } frame)
        {
            return;
        }

        // The picker takes focus, and losing focus is how this panel is dismissed. Held
        // open across the dialog, or the window it belongs to is gone before it returns.
        _holdOpen = true;
        try
        {
            await SavePrompt.WriteAsync(this, frame, _settings.Current);
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"Could not save '{entry.Path}': {exception.Message}");
        }
        finally
        {
            _holdOpen = false;
        }

        Close();
    }

    /// <summary>
    /// The capture's pixels, or null if the file has gone since the panel drew it.
    /// </summary>
    private static async Task<CapturedFrame?> ReadAsync(HistoryEntry entry)
    {
        try
        {
            return await ImageLoader.LoadAsync(entry.Path);
        }
        catch (Exception exception)
        {
            // The panel drew this a moment ago, so a failure here is the file going away
            // underneath it — nothing the user did, and nothing they can answer.
            DiagnosticLog.Write($"Could not read '{entry.Path}' back: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Takes one capture out of the history and redraws, leaving the panel open.
    /// </summary>
    /// <remarks>
    /// The one action that does not dismiss: deleting is usually done to several in a
    /// row, and a panel that closed after each would have to be reopened between them.
    /// </remarks>
    private void Forget(HistoryEntry entry)
    {
        ScreenshotHistory.Forget(entry.Path);
        _tiles.RemoveAll(tile => tile.Entry.Path == entry.Path);
        Draw();
    }

    /// <summary>
    /// Puts the capture's file on the drag, so it can be dropped into any app that takes
    /// files rather than only ones that take a pasted image.
    /// </summary>
    private async void StartDrag(HistoryEntry entry, DragStartingEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            _holdOpen = true;
            e.Data.SetStorageItems([await StorageFile.GetFileFromPathAsync(entry.Path)]);
            e.Data.RequestedOperation = DataPackageOperation.Copy;

            // Out of the way for the drop, the way macshot's hideForDrag is: the panel
            // hangs across the top of the screen, which is where the app being dropped
            // into usually is.
            this.GetAppWindow().Hide();
        }
        catch (Exception exception)
        {
            // A capture deleted behind macshot's back. Cancelled rather than reported:
            // the user is mid-gesture, and a dialog under their pointer is worse than a
            // drag that does nothing.
            DiagnosticLog.Write($"Could not drag '{entry.Path}' out of history: {exception.Message}");
            _holdOpen = false;
            e.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void Trash_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = L("Clear History?"),
            Content = L("This will permanently delete all screenshots from history."),
            PrimaryButtonText = L("Clear All"),
            CloseButtonText = L("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        ScreenshotHistory.Clear();
        Close();
    }

    private void PanelRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    /// <summary>
    /// Closes when something else takes focus, which is how the panel is dismissed
    /// without a button to dismiss it.
    /// </summary>
    private void HistoryWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState is WindowActivationState.Deactivated && !_holdOpen)
        {
            Close();
        }
    }

    /// <summary>
    /// Decodes one capture small and works out what the card says under it, or returns
    /// null for a file that will not decode.
    /// </summary>
    /// <remarks>
    /// A capture deleted or replaced behind macshot's back is skipped rather than shown
    /// as a broken card: the panel is for choosing between pictures, and a blank square
    /// is not one to choose.
    /// </remarks>
    private static async Task<HistoryTile?> LoadTileAsync(HistoryEntry entry)
    {
        try
        {
            // Shared read, because a capture may well be open in whatever the machine
            // shows PNGs with, and refusing to show it for that reason would be a failure
            // the user cannot act on.
            using var file = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var stream = file.AsRandomAccessStream();

            // The header, for the size the card reports. The decoded bitmap is card-sized
            // and cannot answer it: its PixelWidth is what it was shrunk to.
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var width = (int)decoder.OrientedPixelWidth;
            var height = (int)decoder.OrientedPixelHeight;

            stream.Seek(0);
            var thumbnail = new BitmapImage { DecodePixelWidth = ThumbnailWidth };
            await thumbnail.SetSourceAsync(stream);

            return new HistoryTile(entry, thumbnail, $"{width} x {height}  ·  {Age(entry.TakenAt)}");
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"Could not show '{entry.Path}' in the history panel: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// How long ago the capture was taken, translated.
    /// </summary>
    /// <remarks>
    /// The number is put in after the lookup, not before: macshot's catalogue is keyed by
    /// <c>"%dm ago"</c>, and "5m ago" is not a key in it.
    /// </remarks>
    private static string Age(DateTimeOffset taken)
    {
        var (template, count) = TimeAgo.Phrase(taken, DateTimeOffset.Now);

        return template.Length == 0
            ? TimeAgo.OnDate(taken)
            : L(template).Replace("%d", count.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal);
    }
}

/// <summary>One capture in the history panel: the picture, and what is written under it.</summary>
internal sealed class HistoryTile(HistoryEntry entry, BitmapImage thumbnail, string caption)
{
    public HistoryEntry Entry { get; } = entry;

    public BitmapImage Thumbnail { get; } = thumbnail;

    /// <summary>The size in pixels and the age, as macshot writes them.</summary>
    public string Caption { get; } = caption;
}
