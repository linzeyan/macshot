using System.Runtime.InteropServices;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Toolbar;

namespace Macshot.Windows.Services;

/// <summary>
/// The frame drawn round the part of the screen a recording is taking, for as long as it
/// runs.
/// </summary>
/// <remarks>
/// <para>
/// Once the recording panel has been dragged clear of the region, this is the only thing
/// left saying what is being recorded — and a recording of the wrong part of the screen is
/// not something anyone notices until they play it back. Where the frame goes is
/// <see cref="RecordedRegionFrame"/>.
/// </para>
/// <para>
/// One layered window per side rather than one WinUI window with its middle cut away,
/// which is what this was and which never drew the frame at all. A chromeless WinUI window
/// keeps its sizing frame, and DWM draws that frame whatever the window region says, inside
/// the window's rectangle: at 175% a pale strip ten pixels deep across the top of the
/// region and a dark line eight pixels in on the other three sides, all of it in every
/// recording, while the purple went with the middle. A layered popup has no frame, and
/// four bars cost the pixels of the frame rather than those of the region it surrounds.
/// </para>
/// <para>
/// Deliberately not excluded from capture: the bars are outside the crop, so a region
/// recording never contains them.
/// </para>
/// </remarks>
internal sealed class RecordedRegionOverlay : IDisposable
{
    /// <summary>macshot's 0.8.</summary>
    private const int Alpha = 204;

    private readonly List<LayeredOverlayWindow> _edges = [];

    /// <summary>
    /// Puts the frame round <paramref name="region"/> on <paramref name="display"/>, both
    /// in virtual-screen pixels. Must be made on the UI thread.
    /// </summary>
    public RecordedRegionOverlay(CaptureRegion region, CaptureRegion display, double scale)
    {
        // The accent the user chose — the same purple the selection marquee was drawn in a
        // moment earlier, so the frame reads as the same thing. Premultiplied, because that
        // is what UpdateLayeredWindow composites.
        var accent = ToolbarPalette.Accent;
        var bgra = ((uint)Alpha << 24)
            | ((uint)Premultiply(accent.R) << 16)
            | ((uint)Premultiply(accent.G) << 8)
            | Premultiply(accent.B);

        try
        {
            foreach (var (x, y, width, height) in RecordedRegionFrame.Edges(region, display, scale))
            {
                var pixels = new byte[width * height * 4];
                MemoryMarshal.Cast<byte, uint>(pixels.AsSpan()).Fill(bgra);

                var edge = new LayeredOverlayWindow(width, height);
                _edges.Add(edge);
                edge.Show(pixels, x, y);
            }
        }
        catch (InvalidOperationException error)
        {
            // A window that cannot be made is a missing frame, not a failed recording.
            DiagnosticLog.Verbose($"recorded region frame: {error.Message}");
        }
    }

    public void Dispose()
    {
        foreach (var edge in _edges)
        {
            edge.Dispose();
        }

        _edges.Clear();
    }

    private static byte Premultiply(byte channel) => (byte)(((channel * Alpha) + 127) / 255);
}
