using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace Macshot.Windows;

internal static class WindowExtensions
{
    public static AppWindow GetAppWindow(this Window window)
    {
        var windowId = Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(window));
        return AppWindow.GetFromWindowId(windowId);
    }

    /// <summary>
    /// Strips the border and title bar, and answers the presenter that did it so the
    /// caller can go on to say whether the window floats or resizes.
    /// </summary>
    /// <remarks>
    /// The presenter has to be created and set rather than cast to: a window WinUI
    /// made reports the default presenter, which is not an
    /// <see cref="OverlappedPresenter"/>, so <c>Presenter is OverlappedPresenter</c>
    /// silently matches nothing. Every window macshot puts on the screen was written
    /// that way, which is why the capture overlay opened full-screen, correct, and
    /// underneath the window it was meant to be covering.
    /// </remarks>
    public static OverlappedPresenter MakeChromeless(this AppWindow appWindow)
    {
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        appWindow.SetPresenter(presenter);
        return presenter;
    }

    /// <summary>
    /// Puts the window's <em>client</em> rect exactly on <paramref name="pixels"/>, in
    /// physical screen pixels.
    /// </summary>
    /// <remarks>
    /// <see cref="AppWindow.MoveAndResize"/> places the window rect, and a chromeless
    /// window still carries a sizing frame — 3 pixels a side at 175% — so the content
    /// lands inset by it. For the capture overlay that inset is not cosmetic: the
    /// pointer's origin is the client's origin, so every pixel the frame takes is a
    /// pixel of error between what the user framed and what is delivered, and the strip
    /// it leaves along each screen edge is neither dimmed nor selectable.
    ///
    /// The offset is measured rather than assumed to be half the difference: only the
    /// left and right of a frame are guaranteed to match, and a window that turns out to
    /// have any caption at all would place a display's worth of pixels vertically wrong.
    /// </remarks>
    public static void PlaceClient(this AppWindow appWindow, RectInt32 pixels)
    {
        appWindow.MoveAndResize(pixels);
        appWindow.ResizeClient(new SizeInt32(pixels.Width, pixels.Height));

        var handle = Win32Interop.GetWindowFromWindowId(appWindow.Id);
        var clientOrigin = default(ScreenPoint);
        if (!ClientToScreen(handle, ref clientOrigin))
        {
            return;
        }

        appWindow.Move(new PointInt32(
            appWindow.Position.X + (pixels.X - clientOrigin.X),
            appWindow.Position.Y + (pixels.Y - clientOrigin.Y)));
    }

    /// <summary>
    /// The macshot icon for a titled window's title bar, Alt+Tab entry, and taskbar
    /// button.
    /// </summary>
    /// <remarks>
    /// An unpackaged WinUI window does not pick up the executable's icon on its own, so
    /// without this every macshot window is the generic placeholder in Alt+Tab. Silent
    /// on failure: a window with the wrong icon still works, and there is nothing the
    /// user could do about it.
    /// </remarks>
    public static void UseAppIcon(this AppWindow appWindow)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "macshot.ico");
            if (File.Exists(path))
            {
                appWindow.SetIcon(path);
            }
        }
        catch (Exception)
        {
            // Cosmetic, and already reported once by the tray icon if the file is gone.
        }
    }

    /// <summary>
    /// Closes the window on Ctrl+W.
    /// </summary>
    /// <remarks>
    /// <para>
    /// macOS gets this from one File menu, whose Close Window item routes through the
    /// responder chain to whichever window is key
    /// (<c>AppDelegate.swift</c>, <c>de95d60</c>). There is no menu bar to hang it on
    /// here, so each window that ought to answer the key asks for it — which is why this
    /// is one call rather than one handler per window.
    /// </para>
    /// <para>
    /// Only the windows with a title bar call it, because that is what
    /// <c>performClose</c> acts on: a chromeless panel — the HUDs, the toast, the pin —
    /// has no close button, and a key that dismissed one would be a way to lose a
    /// recording's controls with the recording still running.
    /// </para>
    /// <para>
    /// On the content rather than the window: a WinUI <see cref="Window"/> is not a
    /// <see cref="UIElement"/> and has no accelerators of its own.
    /// </para>
    /// </remarks>
    public static void CloseOnControlW(this Window window)
    {
        if (window.Content is not UIElement content)
        {
            return;
        }

        var accelerator = new KeyboardAccelerator
        {
            Modifiers = VirtualKeyModifiers.Control,
            Key = VirtualKey.W,
        };

        accelerator.Invoked += (_, args) =>
        {
            // Handled, or the key carries on to whatever else is listening — a text box
            // being edited on the page would take it as a word delete.
            args.Handled = true;
            window.Close();
        };

        content.KeyboardAccelerators.Add(accelerator);
    }

    /// <summary>
    /// Puts the window in front and gives it the keyboard.
    /// </summary>
    /// <remarks>
    /// <see cref="Window.Activate"/> on its own asks the shell politely, and the shell
    /// refuses a process that is not already the foreground one — which macshot never
    /// is, since it is summoned by a hotkey from whatever the user was doing. Without
    /// this the overlay is on screen but every key still goes to the app behind it.
    /// </remarks>
    public static void TakeForeground(this Window window)
    {
        window.Activate();
        SetForegroundWindow(WindowNative.GetWindowHandle(window));
    }

    /// <summary>
    /// Rounds a chromeless window's corners, and optionally puts a hairline round it —
    /// the two things that tell a floating panel apart from the screen behind it.
    /// </summary>
    /// <param name="hairline">
    /// A COLORREF (0x00BBGGRR) for the border, or null to leave the window unbordered.
    /// </param>
    /// <remarks>
    /// DWM's, not drawn in the content: a WinUI window has no per-pixel transparency, so
    /// a rounded border inside it would leave the window's own square corners showing
    /// through behind the curve. That costs the exact radius — Windows' rather than
    /// macshot's — and any alpha in the hairline, since the attribute takes none.
    /// Windows 10 has neither attribute and fails harmlessly, leaving a square window.
    /// </remarks>
    public static void RoundCorners(this Window window, int? hairline = null)
    {
        var handle = WindowNative.GetWindowHandle(window);

        var rounded = CornerPreferenceRound;
        DwmSetWindowAttribute(handle, CornerPreference, ref rounded, sizeof(int));

        if (hairline is { } colour)
        {
            DwmSetWindowAttribute(handle, BorderColour, ref colour, sizeof(int));
        }
    }

    /// <summary>DWMWA_WINDOW_CORNER_PREFERENCE, Windows 11 and later.</summary>
    private const int CornerPreference = 33;

    /// <summary>DWMWCP_ROUND: the radius Windows rounds an ordinary window with.</summary>
    private const int CornerPreferenceRound = 2;

    /// <summary>DWMWA_BORDER_COLOR, Windows 11 and later.</summary>
    private const int BorderColour = 34;

    /// <summary>Win32 POINT, for <see cref="ClientToScreen"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref ScreenPoint point);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
