using System.Diagnostics;
using Macshot.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using static Macshot.Windows.Services.Localization;

using Windows.Graphics;

namespace Macshot.Windows;

/// <summary>
/// One capture in the history panel: the picture, and when it was taken.
/// </summary>
/// <remarks>
/// Public because <c>x:Bind</c> in the item template compiles against the type, and a
/// template cannot read members it cannot see.
/// </remarks>
public sealed class HistoryTile(HistoryEntry entry, BitmapImage thumbnail)
{
    public HistoryEntry Entry { get; } = entry;

    public BitmapImage Thumbnail { get; } = thumbnail;

    public string Label => Entry.Label;
}

/// <summary>
/// The history panel: every capture macshot has kept, as pictures.
/// </summary>
/// <remarks>
/// <para>
/// The tray menu already lists the last few by time, which answers "the one I took a
/// minute ago" and nothing else. Choosing between yesterday's captures means looking at
/// them, and a menu of timestamps makes that three round trips through the editor. This is
/// the counterpart of the macOS <c>HistoryOverlayController</c>.
/// </para>
/// <para>
/// Thumbnails are decoded at tile width rather than full size. A history of forty 4K
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

    /// <summary>The width thumbnails are decoded at, matching the tile in the template.</summary>
    private const int ThumbnailWidth = 200;

    private const int WidthDips = 900;
    private const int HeightDips = 620;

    public HistoryWindow()
    {
        InitializeComponent();
        // Every string in the XAML is already the English text macshot keys by,
        // so the page is translated in place rather than written twice.
        this.Localize();
        this.GetAppWindow().UseAppIcon();
    }

    /// <summary>Raised with the capture the user picked, for the owner to open.</summary>
    public event EventHandler<HistoryEntry>? OpenRequested;

    public async Task ShowAsync()
    {
        var appWindow = this.GetAppWindow();
        var monitor = MonitorEnumerator.Enumerate().Layout.Primary;
        var width = (int)(WidthDips * monitor.Scale);
        var height = (int)(HeightDips * monitor.Scale);
        appWindow.MoveAndResize(new RectInt32(
            (int)monitor.WorkArea.X + (((int)monitor.WorkArea.Width - width) / 2),
            (int)monitor.WorkArea.Y + (((int)monitor.WorkArea.Height - height) / 2),
            width,
            height));

        Activate();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var entries = ScreenshotHistory.Recent(Depth);
        var tiles = new List<HistoryTile>(entries.Count);

        foreach (var entry in entries)
        {
            if (await LoadThumbnailAsync(entry.Path) is { } thumbnail)
            {
                tiles.Add(new HistoryTile(entry, thumbnail));
            }
        }

        Captures.ItemsSource = tiles;
        Captures.Visibility = tiles.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = tiles.Count == 0
            ? ScreenshotHistory.Directory
            : $"{tiles.Count} in {ScreenshotHistory.Directory}";
    }

    /// <summary>
    /// Decodes one capture small, or returns null for a file that will not decode.
    /// </summary>
    /// <remarks>
    /// A capture deleted or replaced behind macshot's back is skipped rather than shown as
    /// a broken tile: the panel is for choosing between pictures, and a blank square is
    /// not one to choose.
    /// </remarks>
    private static async Task<BitmapImage?> LoadThumbnailAsync(string path)
    {
        try
        {
            // Shared read, because a capture may well be open in whatever the machine
            // shows PNGs with, and refusing to show it for that reason would be a failure
            // the user cannot act on.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var image = new BitmapImage { DecodePixelWidth = ThumbnailWidth };
            await image.SetSourceAsync(file.AsRandomAccessStream());
            return image;
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"Could not show '{path}' in the history panel: {exception.Message}");
            return null;
        }
    }

    private void Captures_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HistoryTile tile)
        {
            OpenRequested?.Invoke(this, tile.Entry);

            // Closed, because the capture is now open in the editor and two windows
            // showing the same screenshot invites editing the wrong one.
            Close();
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var opened = Process.Start(new ProcessStartInfo(ScreenshotHistory.Directory)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not open the folder: {exception.Message}";
        }
    }

    /// <summary>
    /// Clears the history and reloads, which empties the panel in front of the user rather
    /// than leaving it showing captures that are no longer there.
    /// </summary>
    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        ScreenshotHistory.Clear();
        await ReloadAsync();
        StatusText.Text = L("History cleared.");
    }
}
