using System.Runtime.InteropServices.WindowsRuntime;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.Graphics;

namespace Macshot.Windows;

/// <summary>
/// The panel beside a scroll capture showing what has been stitched so far.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>ScrollCapturePreviewPanel</c>: 200 across, 12 clear of the region, the
/// image lengthening as the page is scrolled through. It answers the one question worth
/// asking while a scroll capture runs — has it drifted, or stitched the same rows twice?
/// Without it a bad capture is invisible until it finishes, which is exactly when
/// stopping early would have helped.
/// </para>
/// <para>
/// Its own window rather than part of the HUD, because it has to sit against the window
/// being captured while the HUD stays out of the way at the bottom of the screen — and
/// because it must not be over the target, or it would be in the capture's own frames.
/// </para>
/// </remarks>
public sealed partial class ScrollCapturePreviewWindow : Window
{
    /// <summary>macshot's width and its floor — <c>ScrollCapturePreviewPanel.swift:11–14</c>.</summary>
    private const double WidthDips = ScrollCaptureSession.PreviewWidth;

    private const double MinHeightDips = 100;

    private const double MarginDips = 12;

    private CaptureRegion _beside;
    private WriteableBitmap? _bitmap;

    public ScrollCapturePreviewWindow()
    {
        InitializeComponent();
    }

    /// <summary>Puts the panel beside <paramref name="target"/>, on whichever side has room.</summary>
    public void ShowBeside(CaptureRegion target)
    {
        _beside = target;

        var appWindow = this.GetAppWindow();
        appWindow.MakeChromeless().IsAlwaysOnTop = true;
        appWindow.MoveAndResize(Place(MinHeightDips));
        Activate();
    }

    /// <summary>Draws the capture as it stands, and grows the panel to fit it.</summary>
    public void ShowStitched(ScrollCapturePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        if (preview.Width <= 0 || preview.Height <= 0)
        {
            return;
        }

        // Rebuilt when the shape changes rather than reused: the image lengthens as the
        // page is scrolled, and a WriteableBitmap cannot be resized.
        if (_bitmap is not { } bitmap || bitmap.PixelWidth != preview.Width || bitmap.PixelHeight != preview.Height)
        {
            bitmap = new WriteableBitmap(preview.Width, preview.Height);
            _bitmap = bitmap;
            Stitched.Source = bitmap;
        }

        using (var buffer = bitmap.PixelBuffer.AsStream())
        {
            buffer.Write(preview.Pixels, 0, Math.Min(preview.Pixels.Length, (int)buffer.Length));
        }

        bitmap.Invalidate();
        this.GetAppWindow().MoveAndResize(Place(preview.Height * WidthDips / preview.Width));
    }

    private RectInt32 Place(double heightDips)
    {
        var layout = MonitorEnumerator.Enumerate().Layout;
        var monitor = layout.MonitorAt(new CapturePoint(_beside.X + (_beside.Width / 2), _beside.Y + (_beside.Height / 2)))
            ?? layout.Primary;

        var width = (int)(WidthDips * monitor.Scale);
        var margin = (int)(MarginDips * monitor.Scale);

        // Capped to the display rather than allowed to run off the bottom: a long page
        // would otherwise push the newest rows — the ones worth watching — off screen.
        var height = (int)Math.Clamp(
            heightDips * monitor.Scale,
            MinHeightDips * monitor.Scale,
            Math.Max(MinHeightDips * monitor.Scale, monitor.WorkArea.Height - (margin * 2)));

        var right = (int)_beside.Right + margin;
        var x = right + width <= monitor.WorkArea.Right
            ? right
            : (int)_beside.X - margin - width;

        // Grown from the vertical middle of the region, as macshot's is, so the panel
        // does not walk up the screen as the capture lengthens.
        var y = (int)(_beside.Y + ((_beside.Height - height) / 2));

        return new RectInt32(
            (int)Math.Clamp(x, monitor.WorkArea.X, Math.Max(monitor.WorkArea.X, monitor.WorkArea.Right - width)),
            (int)Math.Clamp(y, monitor.WorkArea.Y, Math.Max(monitor.WorkArea.Y, monitor.WorkArea.Bottom - height)),
            width,
            height);
    }
}
