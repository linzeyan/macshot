using System.Runtime.InteropServices;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Toolbar;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

using Windows.Graphics;
using Windows.UI;

namespace Macshot.Windows;

/// <summary>
/// The frame drawn round the part of the screen a recording is taking.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>SelectionBorderOverlay</c>: the accent colour at 0.8, 1.5 wide, laid
/// entirely <em>outside</em> the recorded rectangle so it is not itself in the file.
/// Once the recording panel has been dragged clear of the region, this is the only thing
/// left saying what is being recorded — and a recording of the wrong part of the screen
/// is not something anyone notices until they play it back.
/// </para>
/// <para>
/// The window is the border. A WinUI window has no per-pixel transparency, so a
/// full-screen overlay with a rectangle drawn on it would be an opaque sheet over the
/// desktop; instead the window is the size of the region plus its border, and its middle
/// is cut away with <c>SetWindowRgn</c> — the same answer the countdown dial uses for its
/// circle, and with the same cost: the edge is clipped rather than antialiased.
/// </para>
/// <para>
/// Deliberately <em>not</em> excluded from capture. The recording overlays exist to be in
/// the recording on macOS; here the border sits outside the crop, so a region recording
/// never contains it and a whole-display recording shows it at the screen's edge, which
/// is where it belongs.
/// </para>
/// </remarks>
public sealed partial class RecordedRegionWindow : Window
{
    /// <summary>macshot's stroke — <c>SelectionBorderOverlay.swift:50</c>.</summary>
    private const double StrokeDips = 1.5;

    private const int ExtendedStyle = -20;

    /// <summary>WS_EX_NOACTIVATE: never becomes the foreground window, not even on a click.</summary>
    private const long NoActivate = 0x08000000;

    /// <summary>WS_EX_TOOLWINDOW: keeps a panel this transient out of Alt+Tab.</summary>
    private const long ToolWindow = 0x00000080;

    /// <summary>
    /// WS_EX_TRANSPARENT: every click goes through to whatever is being recorded. A
    /// frame that swallowed the press on the edge of the window under it would make the
    /// thing being demonstrated undemonstrable.
    /// </summary>
    private const long ClickThrough = 0x00000020;

    public RecordedRegionWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Puts the frame round <paramref name="region"/>, given in that display's own
    /// pixels at <paramref name="scale"/>.
    /// </summary>
    public void ShowAround(CaptureRegion region, double scale)
    {
        var stroke = Math.Max(1, (int)Math.Round(StrokeDips * scale));

        // The accent the user chose, at macshot's 0.8 — the same purple the selection
        // marquee was drawn in a moment earlier, so the frame reads as the same thing.
        Frame.Background = new SolidColorBrush(Color.FromArgb(
            204,
            ToolbarPalette.Accent.R,
            ToolbarPalette.Accent.G,
            ToolbarPalette.Accent.B));

        var handle = WindowNative.GetWindowHandle(this);

        // Set before the window is shown, as the countdown does: applying
        // WS_EX_NOACTIVATE afterwards leaves the one frame that steals the foreground.
        var style = GetWindowLongPtr(handle, ExtendedStyle).ToInt64();
        SetWindowLongPtr(handle, ExtendedStyle, new IntPtr(style | NoActivate | ToolWindow | ClickThrough));

        var width = (int)region.Width + (stroke * 2);
        var height = (int)region.Height + (stroke * 2);

        var appWindow = this.GetAppWindow();
        appWindow.MakeChromeless().IsAlwaysOnTop = true;
        appWindow.MoveAndResize(new RectInt32(
            (int)region.X - stroke,
            (int)region.Y - stroke,
            width,
            height));

        CutOutTheMiddle(handle, width, height, stroke);

        // Activate rather than AppWindow.Show, because a WinUI window does not render
        // its content until it has been activated once. WS_EX_NOACTIVATE is what makes
        // that safe: the frame appears and the foreground stays where it was.
        Activate();
    }

    /// <summary>
    /// Leaves the window as a frame <paramref name="stroke"/> thick by subtracting its
    /// own middle from its region.
    /// </summary>
    private static void CutOutTheMiddle(IntPtr handle, int width, int height, int stroke)
    {
        var outer = CreateRectRgn(0, 0, width, height);
        var inner = CreateRectRgn(stroke, stroke, width - stroke, height - stroke);

        // RGN_DIFF, into the region that is kept: outer minus inner is the frame.
        CombineRgn(outer, outer, inner, 4);
        DeleteObject(inner);

        // outer is not deleted: SetWindowRgn takes ownership of what it is given.
        SetWindowRgn(handle, outer, true);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr destination, IntPtr first, IntPtr second, int mode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
