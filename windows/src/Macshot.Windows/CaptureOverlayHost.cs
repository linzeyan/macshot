using Macshot.Windows.Core.Capture;
using Macshot.Windows.Services;
using Microsoft.UI.Xaml;

using Windows.Graphics;

namespace Macshot.Windows;

/// <summary>
/// The full-screen window a <see cref="CaptureOverlayView"/> is shown in, kept and reused
/// across captures.
/// </summary>
/// <remarks>
/// <para>
/// WinUI never collects a window that has been closed. Measured here: after six captures,
/// all six closed overlays were still alive through a forced full collection, and the
/// managed heap had grown 35MB each time. Setting <c>Content</c> to null — the usual
/// advice — changed the figure by nothing at all. So a capture that builds a window per
/// display leaks one per display, for as long as macshot runs, and that is what took the
/// process from 22MB to 452MB over ten captures.
/// </para>
/// <para>
/// Content swapped out of a window, on the other hand, <em>is</em> collected — measured
/// the same way, with a probe. That is the whole design here: the shell is the part that
/// cannot be released, so there is one per display and it is reused; the interface is the
/// part that can, so it is built afresh for every capture and thrown away after. Nothing
/// is reset between captures because nothing survives one — which is the point. A reused
/// overlay would have to unwind all thirty-nine of its own state fields plus its toolbar
/// and canvas, and anything missed would be a stale selection or a stuck tool on the next
/// capture rather than a compile error.
/// </para>
/// <para>
/// Hidden rather than closed between uses, because closing is what the framework will not
/// take back.
/// </para>
/// </remarks>
public sealed class CaptureOverlayHost : Window
{
    /// <summary>
    /// One shell per display, by device name. Static because the shells outlive the
    /// controller's list of overlays, which is emptied after every capture.
    /// </summary>
    private static readonly Dictionary<string, CaptureOverlayHost> Shells = [];

    private CaptureOverlayHost()
    {
        // Nothing in it until a capture puts something there. A shell with no content is
        // what an idle macshot holds, one per display it has ever put an overlay on.
        Title = "Macshot Capture";
    }

    /// <summary>The shell for <paramref name="monitor"/>, made once and kept.</summary>
    public static CaptureOverlayHost For(CaptureMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        if (!Shells.TryGetValue(monitor.DeviceName, out var shell))
        {
            shell = new CaptureOverlayHost();
            Shells[monitor.DeviceName] = shell;
        }

        return shell;
    }

    /// <summary>
    /// Puts <paramref name="view"/> on screen over the whole of <paramref name="monitor"/>.
    /// </summary>
    /// <remarks>
    /// The chrome and the placement are reapplied on every capture rather than once when
    /// the shell is made: a display can change resolution, scale or position between two
    /// captures, and a shell placed to the display as it used to be would put the overlay
    /// somewhere the pointer is not.
    /// </remarks>
    public void Present(CaptureOverlayView view, CaptureMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(monitor);

        Content = view;

        var appWindow = this.GetAppWindow();
        var presenter = appWindow.MakeChromeless();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;

        // The client rect, not the window rect: the pointer's origin is the client's
        // origin, so a frame left round the window is a translation of every capture.
        // AppWindow positions in physical pixels, so the display's virtual-space bounds
        // go in unchanged — converting to layout units here would misplace the overlay
        // on every display that is not at 100%.
        appWindow.PlaceClient(new RectInt32(
            (int)monitor.Bounds.X,
            (int)monitor.Bounds.Y,
            (int)monitor.Bounds.Width,
            (int)monitor.Bounds.Height));

        // Shown here rather than in the constructor, because a shell that has been used
        // before is hidden and an AppWindow that is hidden does not answer Activate.
        appWindow.Show();
        this.TakeForeground();
    }

    /// <summary>
    /// Takes <paramref name="view"/> off screen, if it is still the one being shown.
    /// </summary>
    /// <remarks>
    /// Checked rather than assumed: a capture started before the last one finished tidying
    /// up would otherwise have its own view pulled out from under it. Dropping the content
    /// is what lets the interface — and the frozen screen behind it — be collected.
    /// </remarks>
    public void Retire(CaptureOverlayView view)
    {
        if (!ReferenceEquals(Content, view))
        {
            return;
        }

        this.GetAppWindow().Hide();
        Content = null;
    }
}
